using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HauntPatrol.Audio;
using HauntPatrol.Effects;
using HauntPatrol.Encounters;
using HauntPatrol.Overlays;
using HauntPatrol.Platform;
using HauntPatrol.Rendering;
using Microsoft.Win32;

namespace HauntPatrol.Core;

public enum HauntState { Stopped, Idle, EncounterStarting, EncounterActive, EncounterEnding, PenaltyActive }
public enum EncounterKind { Skeleton, Cat, Ghost, Zombie }
public enum PenaltyKind { Glass, Slime, Fog, Infection }

/// <summary>
/// Owns the state machine, the scheduler, the frame loop and every overlay. Runs entirely on the UI
/// dispatcher. Every tick and input handler goes through <see cref="Guard"/>: any exception tears everything down.
/// </summary>
public sealed class HauntingController
{
    const double ResolveGrace = 1;  // seconds past the timer before an unresolved encounter is failed
    const double CompleteGrace = 6; // seconds past the timer before a stuck outro is force-completed
    const double HudWidth = 340;

    public event Action<string>? Log;
    public event Action? StateChanged;

    public HauntState State { get; private set; } = HauntState.Stopped;
    public bool AutoEnabled { get; private set; }
    public bool AllMonitors { get; set; }
    public bool DemoMode { get; set; }
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
    public AudioService Audio { get; } = new();
    public bool Busy => State is not (HauntState.Stopped or HauntState.Idle);

    readonly OverlayManager _overlays;
    readonly SpriteAssetLoader _assets = new();
    readonly DispatcherTimer _schedule = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Random _random = new();
    readonly string _logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "haunt.log");

    double _lastTick, _phaseStart;
    bool _framesRunning;
    TimeSpan _lastRenderingTime;
    int _frames, _longFrames;
    double _worstFrame;
    int _demoNext;
    IEncounter? _encounter;
    EncounterContext? _ctx;
    EncounterKind _kind;
    MonitorInfo? _monitor;
    IPenaltyEffect? _penalty;
    TextBlock? _hudTime;
    Rectangle? _hudBar;

    double Now => _clock.Elapsed.TotalSeconds;

    public HauntingController()
    {
        _overlays = new OverlayManager(Write);
        _schedule.Tick += (_, _) => Guard(OnScheduleTick);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void ReportDiagnostics()
    {
        Write($"DPI awareness: {(NativeMethods.IsPerMonitorV2 ? "PerMonitorV2" : "NOT PerMonitorV2 - overlay positions will be wrong")}");
        foreach (var m in MonitorService.Enumerate(allMonitors: true))
            Write($"monitor #{m.Index}{(m.IsPrimary ? " (primary)" : "")} [{m.Bounds.X},{m.Bounds.Y} {m.Bounds.Width}x{m.Bounds.Height}]");
    }

    // ---- user controls -------------------------------------------------------------------------

    public void StartHaunting()
    {
        AutoEnabled = true;
        Write("Hauntings enabled");
        if (State == HauntState.Stopped) SetState(HauntState.Idle);
        if (!Busy) ScheduleNext();
    }

    public void StopHaunting()
    {
        AutoEnabled = false;
        _schedule.Stop();
        Cleanup(silence: true);
        SetState(HauntState.Stopped);
        Write("Hauntings stopped; all overlays removed");
    }

    public void Shutdown()
    {
        StopHaunting();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }

    /// <summary>Emergency teardown: close everything, keep the scheduler setting.</summary>
    public void Abort(string reason)
    {
        Write($"ABORT: {reason}");
        Cleanup(silence: true);
        Finish();
    }

    public void Trigger(string what)
    {
        switch (what.ToLowerInvariant())
        {
            case "skeleton": StartEncounter(EncounterKind.Skeleton); break;
            case "cat": StartEncounter(EncounterKind.Cat); break;
            case "glass": StartPenalty(PenaltyKind.Glass); break;
            case "slime": StartPenalty(PenaltyKind.Slime); break;
            case "ghost": StartEncounter(EncounterKind.Ghost); break;
            case "zombie": StartEncounter(EncounterKind.Zombie); break;
            case "fog": StartPenalty(PenaltyKind.Fog); break;
            case "infection": StartPenalty(PenaltyKind.Infection); break;
            default: Write($"unknown trigger '{what}'"); break;
        }
    }

    public void StartEncounter(EncounterKind kind) => Guard(() =>
    {
        if (Busy) { Write($"{kind} ignored: {State} in progress"); return; }
        _schedule.Stop();
        SetState(HauntState.EncounterStarting);
        Write($"foreground before encounter: {NativeMethods.ForegroundHandle}");

        var monitors = MonitorService.Enumerate(AllMonitors);
        _monitor = DemoMode ? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0] : monitors[_random.Next(monitors.Count)];
        var stage = _overlays.Open(_monitor, passive: false);
        var effects = _overlays.Open(_monitor, passive: true); // opened second, so it sits above the stage

        _kind = kind;
        IEncounter encounter = kind switch
        {
            EncounterKind.Skeleton => new SkeletonSiegeEncounter(),
            EncounterKind.Cat => new WitchsFamiliarEncounter(),
            EncounterKind.Ghost => new GhostTrapEncounter(),
            _ => new ZombieInvasionEncounter(),
        };
        var ctx = new EncounterContext(stage.Canvas, effects.Canvas, stage.SizeInDips, _assets, Audio,
            DemoMode ? new Random(1031) : new Random(), DemoMode, encounter.Duration, () => Guard(OnEncounterComplete));
        _encounter = encounter;
        _ctx = ctx;

        stage.PointerDown += p => Guard(() => { if (ctx.Outcome == null) encounter.OnPointerDown(p); });
        stage.PointerMove += p => Guard(() => { if (ctx.Outcome == null) encounter.OnPointerMove(p); });
        stage.PointerUp += p => Guard(() => { if (ctx.Outcome == null) encounter.OnPointerUp(p); }); // Resolve() ends drags; that Up must not re-enter

        BuildHud(effects.Canvas, ctx.Width, encounter.Title);
        encounter.Initialize(ctx);
        encounter.Start();
        Audio.Play("encounter");
        StartPhase();
        SetState(HauntState.EncounterActive);
        Write($"{encounter.Title} started on monitor #{_monitor.Index} ({encounter.Duration:0}s, {ctx.Width:0}x{ctx.Height:0} DIP)");
    });

    /// <summary>Diagnostics: end the running encounter now with the given outcome (skips its outro).</summary>
    public void ForceOutcome(EncounterResult result) => Guard(() =>
    {
        if (_ctx is not { } ctx) { Write($"force {result} ignored: no encounter running"); return; }
        Write($"forcing {result}");
        ctx.Resolve(result);
        ctx.Complete();
    });

    public void StartPenalty(PenaltyKind kind) => Guard(() =>
    {
        if (Busy) { Write($"{kind} penalty ignored: {State} in progress"); return; }
        _schedule.Stop();
        BeginPenalty(kind, null, null);
    });

    // ---- lifecycle -----------------------------------------------------------------------------

    void OnEncounterComplete()
    {
        var ctx = _ctx!;
        var result = ctx.Outcome ?? EncounterResult.Cancelled;
        Write($"{_encounter!.Title}: {result} after {ctx.Elapsed:0.0}s");
        var monitor = _monitor;
        var impact = ctx.ImpactPoint;
        SetState(HauntState.EncounterEnding);
        Cleanup(silence: false);

        if (result == EncounterResult.Failure)
            BeginPenalty(_kind switch
            {
                EncounterKind.Skeleton => PenaltyKind.Glass,
                EncounterKind.Cat => PenaltyKind.Slime,
                EncounterKind.Ghost => PenaltyKind.Fog,
                _ => PenaltyKind.Infection,
            }, monitor, impact);
        else
            Finish();
    }

    void BeginPenalty(PenaltyKind kind, MonitorInfo? origin, Point? impact)
    {
        SetState(HauntState.PenaltyActive);
        var rng = DemoMode ? new Random(2027) : new Random();
        var surfaces = MonitorService.Enumerate(AllMonitors).Select(m =>
        {
            var w = _overlays.Open(m, passive: true);
            var size = w.SizeInDips;
            var at = m.Bounds == origin?.Bounds && impact is Point p
                ? p
                : DemoMode ? new Point(size.Width / 2, size.Height / 2)
                : new Point(size.Width * (0.3 + rng.NextDouble() * 0.4), size.Height * (0.3 + rng.NextDouble() * 0.4));
            return new PenaltySurface(w.Canvas, size, at);
        }).ToList();

        _penalty = kind switch
        {
            PenaltyKind.Glass => new GlassEffect(surfaces, Audio, rng),
            PenaltyKind.Slime => new SlimeEffect(surfaces, Audio, _assets, rng),
            PenaltyKind.Fog => new SpectralFogEffect(surfaces, Audio, _assets, rng),
            _ => new ZombieInfectionEffect(surfaces, Audio, _assets, rng),
        };
        _penalty.Start();
        StartPhase();
        Write($"{kind} penalty on {surfaces.Count} monitor(s) for {_penalty.Duration:0}s (click-through)");
    }

    void Finish()
    {
        SetState(AutoEnabled ? HauntState.Idle : HauntState.Stopped);
        Write($"desktop clear; foreground: {NativeMethods.ForegroundHandle}");
        if (AutoEnabled) ScheduleNext();
    }

    /// <summary>Close every overlay and release every resource. Safe to call at any time, any number of times.</summary>
    void Cleanup(bool silence)
    {
        StopFrames();
        if (_frames > 0)
            Write($"frames: {_frames} in {Now - _phaseStart:0.0}s ({_frames / (Now - _phaseStart):0} fps), {_longFrames} over 25 ms, worst {_worstFrame * 1000:0} ms");
        _frames = 0;
        Try(() => _encounter?.End());
        Try(() => _penalty?.Stop());
        Try(() => Mouse.Capture(null));
        if (silence) Try(Audio.StopAll);
        Try(_overlays.CloseAll);
        _encounter = null;
        _ctx = null;
        _penalty = null;
        _hudTime = null;
        _hudBar = null;
    }

    void StartPhase()
    {
        _phaseStart = _lastTick = Now;
        _frames = _longFrames = 0;
        _worstFrame = 0;
        if (_framesRunning) return;
        CompositionTarget.Rendering += OnRendering;
        _framesRunning = true;
    }

    void StopFrames()
    {
        if (!_framesRunning) return;
        CompositionTarget.Rendering -= OnRendering;
        _framesRunning = false;
    }

    // Tick once per WPF frame, in step with rendering. (A 16 ms DispatcherTimer snaps to the
    // 15.6 ms system timer, giving an uneven ~32 fps that changes when other apps alter timer resolution.)
    void OnRendering(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        if (time == _lastRenderingTime) return; // Rendering can fire more than once per frame
        _lastRenderingTime = time;
        Guard(Tick);
    }

    void Tick()
    {
        var now = Now;
        var interval = now - _lastTick;
        var dt = Math.Min(interval, 0.1);
        _lastTick = now;
        _frames++;
        if (interval > 0.025) _longFrames++;
        if (interval > 0.1) Write($"slow frame {interval * 1000:0} ms at +{now - _phaseStart:0.00}s");
        _worstFrame = Math.Max(_worstFrame, interval);

        if (_ctx is { } ctx && _encounter is { } encounter)
        {
            ctx.Elapsed = now - _phaseStart;
            encounter.Update(dt);
            if (_ctx != ctx) return; // completed inside Update
            ctx.Update(dt);
            if (_ctx != ctx) return; // completed inside a tween
            UpdateHud(ctx);

            if (ctx.Outcome == null && ctx.Elapsed > ctx.Duration + ResolveGrace)
            {
                Write("encounter did not resolve on time; forcing failure");
                ctx.Resolve(EncounterResult.Failure);
            }
            if (ctx.Elapsed > ctx.Duration + CompleteGrace)
            {
                Write("encounter outro stuck; forcing completion");
                ctx.Complete();
            }
        }
        else if (_penalty is { } penalty)
        {
            penalty.Update(dt);
            if (now - _phaseStart >= penalty.Duration)
            {
                Write("penalty faded");
                Cleanup(silence: false);
                Finish();
            }
        }
    }

    // ---- scheduler -----------------------------------------------------------------------------

    void ScheduleNext()
    {
        var factor = DemoMode ? 1 : 0.5 + _random.NextDouble();
        _schedule.Interval = Interval * factor;
        _schedule.Start();
        Write($"next haunting in {_schedule.Interval.TotalSeconds:0}s");
    }

    void OnScheduleTick()
    {
        _schedule.Stop();
        if (!AutoEnabled || Busy) return; // Finish() reschedules after the current event
        var kinds = Enum.GetValues<EncounterKind>();
        StartEncounter(DemoMode ? kinds[_demoNext++ % kinds.Length] : kinds[_random.Next(kinds.Length)]);
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(() => Guard(() =>
        {
            if (Busy) Abort("display settings changed");
        }));

    // ---- HUD (on the click-through layer, so it never eats clicks) ------------------------------

    void BuildHud(Canvas canvas, double width, string title)
    {
        var titleText = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _hudTime = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 214, 120)),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 6),
        };
        _hudBar = new Rectangle { Height = 8, Width = HudWidth, RadiusX = 4, RadiusY = 4, HorizontalAlignment = HorizontalAlignment.Left, Fill = Brushes.Orange };
        var track = new Border { Width = HudWidth, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), Child = _hudBar };
        var hud = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 28, 12, 40)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 150, 90, 220)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 10, 16, 12),
            Child = new StackPanel { Children = { titleText, _hudTime, track } },
        };
        Canvas.SetLeft(hud, (width - HudWidth - 36) / 2);
        Canvas.SetTop(hud, 20);
        canvas.Children.Add(hud);
    }

    void UpdateHud(EncounterContext ctx)
    {
        if (_hudTime == null || _hudBar == null) return;
        if (ctx.Outcome is { } outcome)
        {
            _hudTime.Text = outcome == EncounterResult.Victory ? "BANISHED!" : "TOO SLOW!";
            return;
        }
        var remaining = ctx.TimeRemaining;
        _hudTime.Text = $"{remaining:0.0}s";
        _hudBar.Width = HudWidth * remaining / ctx.Duration;
        _hudBar.Fill = remaining < 5 ? Brushes.OrangeRed : Brushes.Orange;
    }

    // ---- plumbing ------------------------------------------------------------------------------

    void SetState(HauntState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }

    void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            try { Abort($"{ex.GetType().Name}: {ex.Message}"); }
            catch { /* last resort: nothing left to do */ }
        }
    }

    void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { Write($"cleanup step failed: {ex.Message}"); }
    }

    void Write(string line)
    {
        Log?.Invoke(line);
        try { File.AppendAllText(_logPath, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}"); }
        catch (IOException) { }
    }
}
