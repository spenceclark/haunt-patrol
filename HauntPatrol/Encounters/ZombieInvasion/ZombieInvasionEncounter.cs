using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Core;
using HauntPatrol.Rendering;

namespace HauntPatrol.Encounters;

public enum ZombieKind { Normal, Brute, Runner }

/// <summary>Zombies that climb out <paramref name="At"/> seconds after the first wave.</summary>
public sealed record ZombieWave(double At, ZombieKind[] Zombies);

/// <summary>Zombie Invasion tuning, DIPs and seconds.</summary>
public sealed class ZombieInvasionOptions
{
    public double EncounterSeconds { get; init; } = 25;
    public double FirstWaveDelay { get; init; } = 1.5; // after the ground cracks
    public IReadOnlyList<ZombieWave> Waves { get; init; } =
    [
        new(0, [ZombieKind.Normal, ZombieKind.Normal]),
        new(3, [ZombieKind.Normal, ZombieKind.Brute]),
        new(6, [ZombieKind.Runner]),
        new(9, [ZombieKind.Normal, ZombieKind.Brute]),
        new(12, [ZombieKind.Runner]),
    ];
    public int MaxConcurrentZombies { get; init; } = 6;
    public int BrainHealth { get; init; } = 2;

    public double NormalTravelSeconds { get; init; } = 5; // a normal zombie's trip from the bottom edge to the brain
    public double BruteSpeedMultiplier { get; init; } = 0.65;
    public double RunnerSpeedMultiplier { get; init; } = 1.65;
    public double[] SpeedRampByThird { get; init; } = [1.0, 1.1, 1.2];

    public double CriticalSeconds { get; init; } = 1.5;   // time-to-brain that triggers warnings
    public double WarningCooldown { get; init; } = 1.5;   // at most one warning sound this often
    public double BiteRange { get; init; } = 55;
    public double ClickPadding { get; init; } = 6;
    public double MinSpawnGap { get; init; } = 140;
    public double HitPause { get; init; } = 0.25;
    public double Recoil { get; init; } = 40;
    public double[] DemoSpawnX { get; init; } = [0.3, 0.7, 0.15, 0.85, 0.5, 0.25, 0.75, 0.6];

    public int TotalZombies => Waves.Sum(w => w.Zombies.Length);
}

/// <summary>
/// Zombies climb out of the bottom of the screen in escalating waves and shamble towards a glowing brain.
/// Click to zap them: brutes take two hits, runners are fast. A threat meter and a pulsing brain show
/// how close the nearest danger is; you choose where to spend your clicks.
/// </summary>
public sealed class ZombieInvasionEncounter(ZombieInvasionOptions? options = null) : IEncounter
{
    readonly ZombieInvasionOptions _o = options ?? new();

    public string Title => "Zombie Invasion";
    public double Duration => _o.EncounterSeconds;

    enum ZombieState { Spawning, Walking, Hit, Dying, Removed }

    sealed class Zombie
    {
        public required Sprite Sprite;
        public required ZombieKind Kind;
        public int Health;
        public ZombieState State;
        public Point Position;
        public double Speed, StateTime, Phase, SpawnTime, Eta;
        public bool Clickable => State is ZombieState.Spawning or ZombieState.Walking or ZombieState.Hit;
        public bool Approaching => State is ZombieState.Spawning or ZombieState.Walking or ZombieState.Hit;
    }

    /// <summary>The rules in one place: null while the fight goes on.</summary>
    public static EncounterResult? Judge(int spawned, int total, int remaining, int brainHealth, double timeRemaining) =>
        brainHealth <= 0 ? EncounterResult.Failure
        : spawned == total && remaining == 0 ? EncounterResult.Victory
        : timeRemaining <= 0 ? EncounterResult.Failure
        : null;

    /// <summary>One zombie's danger (0-100) from its estimated seconds until it reaches the brain.</summary>
    public static double ThreatFor(double eta)
    {
        // >6 s low, 3-6 s moderate, <3 s high, <1.5 s critical.
        (double Eta, double Threat)[] curve = [(0, 100), (1.5, 70), (3, 55), (6, 30), (10, 0)];
        if (eta <= 0) return 100;
        for (var i = 1; i < curve.Length; i++)
            if (eta <= curve[i].Eta)
                return Ease.Lerp(curve[i - 1].Threat, curve[i].Threat, (eta - curve[i - 1].Eta) / (curve[i].Eta - curve[i - 1].Eta));
        return 0;
    }

    /// <summary>Overall threat: the most dangerous zombie, plus a little for every other one approaching.</summary>
    public static double CombinedThreat(IEnumerable<double> etas)
    {
        var threats = etas.Select(ThreatFor).OrderDescending().ToList();
        return threats.Count == 0 ? 0 : Math.Min(100, threats[0] + 0.15 * threats.Skip(1).Sum());
    }

    EncounterContext _c = null!;
    Sprite _brain = null!, _blaster = null!;
    Ellipse _glow = null!;
    readonly GradientStop _glowCore = new(Colors.MediumPurple, 0), _glowEdge = new(Colors.Transparent, 1);
    readonly List<Zombie> _zombies = [];
    readonly Queue<ZombieKind> _pending = new();
    Border _meter = null!;
    Rectangle _meterBar = null!;
    TextBlock _meterLabel = null!;
    Point _brainPos;
    int _spawned, _nextWave, _brainHealth;
    double _threat, _warningIn, _pulse, _meterWidth = 300;
    bool _done;

    public void Initialize(EncounterContext c)
    {
        _c = c;
        _brainHealth = _o.BrainHealth;
        _brainPos = new Point(c.Width / 2, c.Height / 2);

        // A soft glow behind the brain that shifts from calm purple to red with danger.
        _glow = new Ellipse
        {
            Width = 240,
            Height = 200,
            IsHitTestVisible = false,
            Opacity = 0,
            Fill = new RadialGradientBrush { GradientStops = { _glowCore, _glowEdge } },
        };
        Canvas.SetLeft(_glow, _brainPos.X - 120);
        Canvas.SetTop(_glow, _brainPos.Y - 100);
        c.Effects.Children.Add(_glow);

        // Brain and blaster are never clicked: click-through layer.
        _brain = c.Spawn("brain", _brainPos.X, _brainPos.Y, onEffectsLayer: true);
        _brain.ScaleX = _brain.ScaleY = 0;
        _blaster = c.Spawn("blaster", 80, c.Height * 0.86, onEffectsLayer: true);
        _blaster.Opacity = 0;

        BuildThreatMeter();
        ShowBrainHealth();
    }

    void BuildThreatMeter()
    {
        _meterBar = new Rectangle { Height = 14, Width = 0, RadiusX = 7, RadiusY = 7, HorizontalAlignment = HorizontalAlignment.Left, Fill = Brushes.LimeGreen };
        _meterLabel = new TextBlock { Text = "SAFE", Foreground = Brushes.LimeGreen, FontSize = 18, FontWeight = FontWeights.Black, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 110 };
        _meter = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 20, 10, 30)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 8, 10, 8),
            IsHitTestVisible = false,
            Opacity = 0,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Border { Width = _meterWidth, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), Child = _meterBar, VerticalAlignment = VerticalAlignment.Center },
                    _meterLabel,
                },
            },
        };
        _meter.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_meter, (_c.Width - _meter.DesiredSize.Width) / 2);
        Canvas.SetTop(_meter, 160); // just under the HUD
        _c.Effects.Children.Add(_meter);
    }

    void ShowBrainHealth() =>
        _c.Status = "BRAIN " + new string('♥', _brainHealth) + new string('♡', _o.BrainHealth - _brainHealth);

    public void Start()
    {
        // Warning: the ground cracks, two bony hands poke out, the brain appears.
        var crack = new Polyline { Stroke = ParticleSystem.Frozen(Color.FromRgb(40, 25, 20)), StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        var crackPoints = new List<Point>();
        for (var x = _c.Width * 0.08; x < _c.Width * 0.92; x += 40)
            crackPoints.Add(new Point(x, _c.Height - 8 - _c.Random.NextDouble() * 14));
        _c.Effects.Children.Add(crack);
        _c.Animate(0.5, k =>
        {
            var pts = new PointCollection(crackPoints.Take((int)Math.Ceiling(crackPoints.Count * k)));
            pts.Freeze();
            crack.Points = pts;
        }, () => _c.After(_o.FirstWaveDelay, () => _c.Animate(0.6, k => crack.Opacity = 1 - k)));
        _c.Audio.Play("groan");

        foreach (var fx in new[] { 0.3, 0.7 })
        {
            var hand = _c.Spawn("bonehand", _c.Width * fx, _c.Height + 40, onEffectsLayer: true);
            var top = _c.Height - hand.Height / 2 + 6;
            _c.Animate(1.3, k =>
            {
                hand.Y = Ease.Lerp(_c.Height + hand.Height / 2, top, Math.Sin(k * Math.PI));
                hand.Rotation = Math.Sin(k * Math.PI * 4) * 12;
            });
        }

        _c.After(0.3, () => _c.Animate(0.45, k =>
        {
            _brain.ScaleX = _brain.ScaleY = Ease.OutBack(k);
            _blaster.Opacity = k;
            _glow.Opacity = 0.6 * k;
            _meter.Opacity = k;
        }, () => _brain.BobAmplitude = 5));
        _c.After(0.6, () => _c.Banner("DEFEND THE BRAIN!", _c.Height * 0.3, 2.2, Color.FromRgb(150, 255, 90)));
    }

    double SpeedRamp => _o.SpeedRampByThird[Math.Clamp((int)(_c.Elapsed / Duration * 3), 0, _o.SpeedRampByThird.Length - 1)];
    int Active => _zombies.Count(z => z.State != ZombieState.Removed);

    public void Update(double dt)
    {
        if (_done) return;

        // Waves arrive on a fixed schedule; anything over the active cap waits its turn.
        var t = _c.Elapsed - _o.FirstWaveDelay;
        while (_nextWave < _o.Waves.Count && t >= _o.Waves[_nextWave].At)
        {
            var wave = _o.Waves[_nextWave++];
            foreach (var kind in wave.Zombies) _pending.Enqueue(kind);
            _c.Audio.Play(wave.Zombies.Contains(ZombieKind.Runner) ? "screech" : "groan");
        }
        while (_pending.Count > 0 && Active < _o.MaxConcurrentZombies) SpawnZombie(_pending.Dequeue());

        foreach (var z in _zombies) Step(z, dt);
        _zombies.RemoveAll(z => z.State == ZombieState.Removed);
        UpdateThreat(dt);

        switch (Judge(_spawned, _o.TotalZombies, _zombies.Count, _brainHealth, _c.TimeRemaining))
        {
            case EncounterResult.Victory: Win(); break;
            case EncounterResult.Failure: Fail(); break;
        }
    }

    void SpawnZombie(ZombieKind kind)
    {
        var x = _c.Demo ? _c.Width * _o.DemoSpawnX[_spawned % _o.DemoSpawnX.Length] : PickSpawnX();
        var sprite = _c.Spawn(kind switch { ZombieKind.Brute => "brute", ZombieKind.Runner => "runner", _ => "zombie" }, x, _c.Height);
        sprite.ScaleY = 0;
        sprite.Play("walk");
        var start = new Point(x, _c.Height);
        var baseSpeed = (_brainPos - start).Length / _o.NormalTravelSeconds;
        _zombies.Add(new Zombie
        {
            Sprite = sprite,
            Kind = kind,
            Health = kind == ZombieKind.Brute ? 2 : 1,
            Position = start,
            Speed = baseSpeed * kind switch { ZombieKind.Brute => _o.BruteSpeedMultiplier, ZombieKind.Runner => _o.RunnerSpeedMultiplier, _ => 1 },
            Phase = _c.Random.NextDouble() * Math.PI * 2,
            SpawnTime = _c.Elapsed,
        });
        _spawned++;
        _c.Particles.Burst(new Point(x, _c.Height - 6), 14, Color.FromRgb(110, 80, 50), 300, 0.6, 9);
    }

    double PickSpawnX()
    {
        var x = 0.0;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            x = _c.Width * (0.08 + _c.Random.NextDouble() * 0.84);
            if (_zombies.All(z => Math.Abs(z.Position.X - x) > _o.MinSpawnGap)) break;
        }
        return x;
    }

    double RiseSeconds(Zombie z) => z.Kind == ZombieKind.Runner ? 0.35 : 0.6;

    void Step(Zombie z, double dt)
    {
        z.StateTime += dt;
        var toBrain = _brainPos - z.Position;
        var distance = toBrain.Length;
        switch (z.State)
        {
            case ZombieState.Spawning:
                z.Sprite.ScaleY = Ease.OutBack(Math.Min(1, z.StateTime / RiseSeconds(z)));
                if (z.StateTime >= RiseSeconds(z)) SetState(z, ZombieState.Walking);
                break;

            case ZombieState.Walking:
                if (distance < _o.BiteRange)
                {
                    Bite(z);
                    break;
                }
                z.Position += toBrain / distance * z.Speed * SpeedRamp * dt;
                z.Sprite.FlipX = toBrain.X < 0; // art faces right
                var age = _c.Elapsed - z.SpawnTime;
                z.Sprite.Rotation = z.Kind == ZombieKind.Runner
                    ? (toBrain.X < 0 ? -12 : 12) + Math.Sin(age * 14 + z.Phase) * 5 // leaning sprint
                    : Math.Sin(age * (z.Kind == ZombieKind.Brute ? 4 : 6) + z.Phase) * 7; // shamble
                break;

            case ZombieState.Hit:
                if (z.StateTime >= _o.HitPause) SetState(z, ZombieState.Walking);
                break;
        }

        // Time until it bites, for the threat meter and warnings.
        var delay = z.State switch
        {
            ZombieState.Spawning => RiseSeconds(z) - z.StateTime,
            ZombieState.Hit => _o.HitPause - z.StateTime,
            _ => 0,
        };
        z.Eta = Math.Max(0, distance - _o.BiteRange) / (z.Speed * SpeedRamp) + Math.Max(0, delay);

        // Critical zombies pulse so the danger reads at a glance; movement is unchanged.
        var critical = z.State is ZombieState.Walking or ZombieState.Hit && z.Eta < _o.CriticalSeconds;
        if (z.State is ZombieState.Walking or ZombieState.Hit)
            z.Sprite.ScaleX = z.Sprite.ScaleY = critical ? 1 + 0.12 * Math.Abs(Math.Sin(_c.Elapsed * 12)) : 1;

        z.Sprite.X = z.Position.X;
        z.Sprite.Y = z.Position.Y;
    }

    static void SetState(Zombie z, ZombieState state)
    {
        z.State = state;
        z.StateTime = 0;
        if (state == ZombieState.Walking) z.Sprite.Play("walk");
    }

    void UpdateThreat(double dt)
    {
        var approaching = _zombies.Where(z => z.Approaching).ToList();
        var target = CombinedThreat(approaching.Select(z => z.Eta));
        _threat += (target - _threat) * Math.Min(1, 4 * dt); // smoothed, so the meter doesn't flicker
        var critical = approaching.Any(z => z.Eta < _o.CriticalSeconds);

        var (colour, label) = _threat >= 70 && critical ? (Color.FromRgb(255, 60, 60), "CRITICAL!")
            : _threat >= 30 ? (Color.FromRgb(255, 160, 40), "DANGER")
            : (Color.FromRgb(120, 230, 110), "SAFE");
        _meterBar.Width = _meterWidth * _threat / 100;
        if (_meterLabel.Text != label)
        {
            _meterLabel.Text = label;
            var brush = ParticleSystem.Frozen(colour);
            _meterLabel.Foreground = brush;
            _meterBar.Fill = brush;
        }

        // The brain pulses faster and glows hotter as danger rises.
        var danger = _threat / 100;
        _pulse += dt * (2.5 + 10 * danger);
        _brain.ScaleX = _brain.ScaleY = 1 + (0.02 + 0.08 * danger) * Math.Sin(_pulse);
        var calm = Color.FromRgb(160, 110, 255);
        _glowCore.Color = danger < 0.5
            ? Lerp(calm, Color.FromRgb(255, 150, 40), danger * 2)
            : Lerp(Color.FromRgb(255, 150, 40), Color.FromRgb(255, 40, 40), (danger - 0.5) * 2);
        _glow.Opacity = 0.45 + 0.45 * danger;

        _warningIn -= dt;
        if (critical && _warningIn <= 0)
        {
            _warningIn = _o.WarningCooldown; // rate-limited: one warning, not a barrage
            _c.Audio.Play("warning");
        }
    }

    static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)Ease.Lerp(a.R, b.R, t), (byte)Ease.Lerp(a.G, b.G, t), (byte)Ease.Lerp(a.B, b.B, t));

    public void OnPointerDown(Point p)
    {
        if (_done) return;
        // Topmost (latest added) zombie under the click. The OS only delivers clicks on visible pixels.
        var target = _zombies.LastOrDefault(z =>
        {
            if (!z.Clickable) return false;
            var r = z.Sprite.Bounds;
            r.Inflate(_o.ClickPadding, _o.ClickPadding);
            return r.Contains(p);
        });
        if (target != null) Zap(target, p);
    }

    public void OnPointerMove(Point p) { }
    public void OnPointerUp(Point p) { }

    void Zap(Zombie z, Point at)
    {
        _c.Audio.Play("zap");
        Bolt(new Point(_blaster.X + 30, _blaster.Y - 22), at);
        _blaster.Shake(0.12, 3);
        _c.Particles.Ring(at, Color.FromRgb(160, 255, 255), 45, 0.25, 4);
        _c.Particles.Burst(at, 8, Color.FromRgb(200, 255, 255), 260, 0.35, 5, 0);

        z.Health--;
        if (z.Health <= 0)
        {
            Die(z);
            return;
        }

        // Survived (a brute): an obvious stagger.
        _c.Audio.Play("thud");
        SetState(z, ZombieState.Hit);
        z.Sprite.Play("hit");
        z.Sprite.Shake(0.3, 6);
        _c.Popup("!", new Point(z.Sprite.Center.X, z.Sprite.Bounds.Top - 10), Color.FromRgb(255, 230, 90), 34);
        var away = z.Position - _brainPos;
        away.Normalize();
        var from = z.Position;
        var recoil = z.Kind == ZombieKind.Brute ? _o.Recoil * 1.5 : _o.Recoil;
        _c.Animate(0.14, k => { if (z.State == ZombieState.Hit) z.Position = from + away * recoil * Ease.OutCubic(k); });
    }

    void Bolt(Point from, Point to)
    {
        // A jagged lightning bolt with a soft glow, gone in a blink.
        var points = new PointCollection { from };
        const int segments = 7;
        var normal = new Vector(-(to - from).Y, (to - from).X);
        normal.Normalize();
        for (var i = 1; i < segments; i++)
            points.Add(Ease.Lerp(from, to, (double)i / segments) + normal * (_c.Random.NextDouble() - 0.5) * 36);
        points.Add(to);
        points.Freeze();
        var glow = new Polyline { Points = points, Stroke = ParticleSystem.Frozen(Color.FromArgb(90, 120, 230, 255)), StrokeThickness = 10, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        var core = new Polyline { Points = points, Stroke = Brushes.White, StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        _c.Effects.Children.Add(glow);
        _c.Effects.Children.Add(core);
        _c.Animate(0.18, k => glow.Opacity = core.Opacity = 1 - k, () =>
        {
            _c.Effects.Children.Remove(glow);
            _c.Effects.Children.Remove(core);
        });
    }

    void Die(Zombie z)
    {
        SetState(z, ZombieState.Dying); // no longer clickable
        z.Sprite.Rotation = 0;
        z.Sprite.ScaleX = z.Sprite.ScaleY = 1;
        z.Sprite.Shake(0.15, 5);
        _c.Particles.Burst(z.Sprite.Center, z.Kind == ZombieKind.Brute ? 30 : 20, Color.FromRgb(120, 200, 60), 380, 0.7, 9);
        var fade = z.Kind == ZombieKind.Runner ? 0.12 : 0.3; // runners vanish fast so they don't hide the others
        z.Sprite.Play("die", () => _c.Animate(fade, k =>
        {
            z.Sprite.Opacity = 1 - k;
            z.Sprite.ScaleY = 1 - 0.6 * k;
        }, () => z.State = ZombieState.Removed));
    }

    void Bite(Zombie z)
    {
        SetState(z, ZombieState.Dying); // spent: out of play, not clickable
        var from = z.Position;
        _c.Animate(0.15, k => z.Position = Ease.Lerp(from, _brainPos, 0.5 * k), () =>
        {
            _brainHealth = Math.Max(0, _brainHealth - 1);
            ShowBrainHealth();
            _brain.Play("hit");
            _brain.Shake(0.35, 8);
            _c.Particles.Burst(_brainPos, 24, Color.FromRgb(255, 110, 150), 360, 0.6, 9);
            _c.Particles.Ring(_brainPos, Color.FromRgb(255, 80, 80), 120, 0.35, 6);
            _c.Audio.Play("impact");
            _c.Animate(0.2, k => z.Sprite.Opacity = 1 - k, () => z.State = ZombieState.Removed);
        });
    }

    void Win()
    {
        if (!_c.Resolve(EncounterResult.Victory)) return;
        _done = true;
        _brain.Play("victory");
        _glowCore.Color = Color.FromRgb(120, 255, 140); // calm again
        _c.Audio.Play("success");
        _c.Particles.Burst(_brainPos, 40, Color.FromRgb(120, 255, 90), 450, 0.8, 8);
        _c.Banner("INVASION REPELLED", _c.Height * 0.3, 0.7, Color.FromRgb(150, 255, 90));
        _c.Animate(0.6, k => _brain.ScaleX = _brain.ScaleY = 1 + 0.3 * Math.Sin(k * Math.PI * 2) * (1 - k), () =>
            _c.Animate(0.35, k => _brain.Opacity = _blaster.Opacity = _glow.Opacity = _meter.Opacity = 1 - k, _c.Complete));
    }

    void Fail()
    {
        if (!_c.Resolve(EncounterResult.Failure)) return;
        _done = true;
        _c.Audio.Play("failure");
        _brain.Play("hit");
        _brain.Shake(0.8, 8);
        _glowCore.Color = Colors.Red;
        _c.ImpactPoint = _brainPos;
        // Whoever is left swarms the brain.
        foreach (var z in _zombies.Where(z => z.State != ZombieState.Removed))
        {
            var from = z.Position;
            z.Sprite.Play("walk");
            _c.Animate(0.6, k =>
            {
                z.Position = Ease.Lerp(from, _brainPos, Ease.OutCubic(k) * 0.8);
                z.Sprite.X = z.Position.X;
                z.Sprite.Y = z.Position.Y;
                z.Sprite.Rotation = Math.Sin(k * 30) * 10;
            });
        }
        _c.After(0.8, _c.Complete);
    }

    public void End() => _done = true;
}
