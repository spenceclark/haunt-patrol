using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Core;
using HauntPatrol.Rendering;

namespace HauntPatrol.Encounters;

public enum LoopVerdict { TooSmall, Open, Missed, Captured }

/// <summary>How big and how closed a stroke must be to count as a loop, in DIPs.</summary>
public readonly record struct LoopRules(int MinPoints, double MinLength, double CloseDistance);

/// <summary>
/// Is a freehand stroke a closed loop around a target? Hands rarely end exactly where they started, so a
/// loop is any of: the whole stroke when its end is near its start (the spec rule); the stroke up to where
/// it first passes back near its start (overshoot); or the part enclosed where the stroke crosses its own
/// path (a lasso with a tail). Each loop must itself be big enough.
/// </summary>
public static class LoopCheck
{
    public static readonly LoopRules Default = new(12, 450, 45);

    public static LoopVerdict Evaluate(IReadOnlyList<Point> stroke, double length, Point target) =>
        Evaluate(stroke, length, [target], Default).Verdict;

    /// <summary>Captures at most one target: the first one found inside a loop.</summary>
    public static (LoopVerdict Verdict, int Target) Evaluate(IReadOnlyList<Point> stroke, double length, IReadOnlyList<Point> targets, LoopRules rules)
    {
        if (stroke.Count < rules.MinPoints || length < rules.MinLength) return (LoopVerdict.TooSmall, -1);
        var closed = false;
        foreach (var loop in Loops(stroke, rules))
        {
            for (var t = 0; t < targets.Count; t++)
                if (Contains(loop, targets[t])) return (LoopVerdict.Captured, t);
            closed = true;
        }
        return (closed ? LoopVerdict.Missed : LoopVerdict.Open, -1);
    }

    static IEnumerable<IReadOnlyList<Point>> Loops(IReadOnlyList<Point> s, LoopRules rules)
    {
        if ((s[^1] - s[0]).Length <= rules.CloseDistance) yield return s;

        // Overshoot: first point, after a loop's worth of travel, that comes back near the start.
        double travelled = 0;
        for (var k = 1; k < s.Count; k++)
        {
            travelled += (s[k] - s[k - 1]).Length;
            if (travelled >= rules.MinLength && (s[k] - s[0]).Length <= rules.CloseDistance)
            {
                yield return s.Take(k + 1).ToList();
                break;
            }
        }

        // Lasso: segment i crosses an earlier, non-adjacent segment j; x, s[j+1..i] is a loop.
        for (var i = 2; i < s.Count - 1; i++)
        for (var j = 0; j < i - 1; j++)
        {
            if (!Intersect(s[j], s[j + 1], s[i], s[i + 1], out var x)) continue;
            var loop = new List<Point>(i - j + 1) { x };
            for (var k = j + 1; k <= i; k++) loop.Add(s[k]);
            if (loop.Count >= rules.MinPoints && Perimeter(loop) >= rules.MinLength) yield return loop;
        }
    }

    static bool Intersect(Point a, Point b, Point c, Point d, out Point x)
    {
        x = default;
        Vector r = b - a, q = d - c;
        var den = Vector.CrossProduct(r, q);
        if (Math.Abs(den) < 1e-9) return false; // parallel
        var t = Vector.CrossProduct(c - a, q) / den;
        var u = Vector.CrossProduct(c - a, r) / den;
        if (t is < 0 or > 1 || u is < 0 or > 1) return false;
        x = a + r * t;
        return true;
    }

    static double Perimeter(IReadOnlyList<Point> loop)
    {
        var total = (loop[0] - loop[^1]).Length;
        for (var k = 1; k < loop.Count; k++) total += (loop[k] - loop[k - 1]).Length;
        return total;
    }

    /// <summary>Ray casting (even-odd); the polygon is closed implicitly from the last point back to the first.</summary>
    public static bool Contains(IReadOnlyList<Point> polygon, Point p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Point a = polygon[i], b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}

/// <summary>Ghost Trap tuning, DIPs and seconds.</summary>
public sealed class GhostTrapOptions
{
    public double EncounterSeconds { get; init; } = 25;  // counted from when the first ghost is playable
    public double ArrivalSeconds { get; init; } = 1.0;

    // Stage 1: the big ghost floats inside a ~350 x 180 region.
    public double RoamX { get; init; } = 175;
    public double RoamY { get; init; } = 90;
    public double DriftSpeed { get; init; } = 70;
    public LoopRules LargeLoop { get; init; } = LoopCheck.Default;

    // Stage 2: it splits.
    public int SplitGhostCount { get; init; } = 2;
    public double SmallGhostScale { get; init; } = 0.68;
    public double SplitSeconds { get; init; } = 0.75;   // squash, flash and burst apart; no drawing meanwhile
    public double SmallSpeedMin { get; init; } = 45;
    public double SmallSpeedMax { get; init; } = 65;
    public double AvoidanceRadius { get; init; } = 160;  // from a small ghost's centre (~80 DIP beyond its body); only while the pen is dragged
    public double AvoidanceMultiplier { get; init; } = 1.4;
    public double Separation { get; init; } = 220;      // small ghosts keep this far apart when practical
    public Size SmallRegion { get; init; } = new(1040, 600);
    public LoopRules SmallLoop { get; init; } = new(12, 250, 38);

    // Presentation. The production art already floats (~3.5 DIP), so the procedural bob only adds a little.
    public double Bob { get; init; } = 2;
    public double SmallBob { get; init; } = 1.5;
    public double SmallLaughSeconds { get; init; } = 0.6; // the same laugh clip, slowed to fill this
    public double PenScale { get; init; } = 1.25;          // the pen art drawn 25% larger so it's easy to find

    public double SampleSpacing { get; init; } = 4;
    public double PenGrab { get; init; } = 30;
    // Carrying the pen leaves no ink; ink switches on once the tip is within this many DIPs beyond a ghost's
    // body (half its height from its centre) and stays on until the pen is let go.
    public double InkReach { get; init; } = 100;
}

/// <summary>
/// A ghost floats over the desktop. Pick up the magic pen and draw a closed loop around it... and it splits
/// into two smaller, nervous ghosts that must each be trapped with their own loop before time runs out.
/// </summary>
public sealed class GhostTrapEncounter(GhostTrapOptions? options = null) : IEncounter
{
    readonly GhostTrapOptions _o = options ?? new();

    public string Title => "Ghost Trap";
    public double Duration => _o.EncounterSeconds;

    enum Stage { Arriving, InitialGhost, Splitting, SmallGhosts, Done }

    sealed class Spirit
    {
        public required Sprite Sprite;
        public Point Position;
        public Vector Velocity;
        public double Speed, Heading, Wobble, Turn, Time, ScaredFor, LaughFor;
        public bool Alive = true;
    }

    const double PenTilt = 25;
    static readonly Color Cyan = Color.FromRgb(120, 240, 255);
    static readonly Color Purple = Color.FromRgb(200, 110, 255);

    EncounterContext _c = null!;
    Sprite _ghost = null!, _pen = null!;
    Stage _stage = Stage.Arriving;
    Point _anchor;
    Rect _region;
    double _offsetX, _vx, _dir = 1, _turnIn, _time;
    bool _drawing, _reacting; // _drawing: the pen is held (carried, possibly inking)
    bool _inking;
    Ellipse? _nib;
    Polyline? _head;                          // brighter leading edge of the trail
    Ellipse _tipGlow = null!;                 // soft pulsing glow on the pen tip (click-through layer)
    readonly GradientStop _tipColour = new(Color.FromArgb(200, 120, 240, 255), 0);
    static readonly Color BrightCyan = Color.FromRgb(190, 255, 255);
    readonly List<Spirit> _spirits = [];

    readonly List<Point> _stroke = [];
    double _strokeLength, _sinceTrailParticle;
    Polyline? _glow, _core;

    public void Initialize(EncounterContext c)
    {
        _c = c;
        var r = c.Random;
        c.TimerHold = _o.ArrivalSeconds;
        _anchor = new Point(c.Width * (c.Demo ? 0.5 : 0.4 + r.NextDouble() * 0.2), c.Height * (c.Demo ? 0.45 : 0.4 + r.NextDouble() * 0.12));
        _turnIn = 2 + r.NextDouble() * 2;
        // Ghosts are never clicked, so they live on the click-through layer; only the pen intercepts the mouse.
        _ghost = c.Spawn("ghost", _anchor.X, _anchor.Y, onEffectsLayer: true);
        _ghost.Opacity = 0;
        _ghost.ScaleX = _ghost.ScaleY = 0.3;

        // Where the small ghosts may roam: around the anchor, with each small ghost fully on screen and clear of the HUD.
        double halfW = _ghost.Width * _o.SmallGhostScale / 2, halfH = _ghost.Height * _o.SmallGhostScale / 2;
        var region = new Rect(_anchor.X - _o.SmallRegion.Width / 2, _anchor.Y - _o.SmallRegion.Height / 2, _o.SmallRegion.Width, _o.SmallRegion.Height);
        region.Intersect(new Rect(halfW + 20, 130 + halfH, c.Width - 2 * halfW - 40, c.Height - 150 - 2 * halfH));
        _region = region;

        // The pen waits to the ghost's left, just outside ink range (the ghost is drawn above it).
        _pen = c.Spawn("pen", _anchor.X - _ghost.Width / 2 - _o.InkReach - 60, _anchor.Y + 30);
        _pen.Opacity = 0;
        _pen.Rotation = PenTilt;
        _tipGlow = new Ellipse
        {
            Width = 34,
            Height = 34,
            IsHitTestVisible = false,
            Opacity = 0,
            Fill = new RadialGradientBrush { GradientStops = { _tipColour, new GradientStop(Color.FromArgb(0, 170, 110, 255), 1) } },
        };
        c.Effects.Children.Add(_tipGlow);
        c.Status = "1 GHOST";
    }

    public void Start()
    {
        _c.Particles.Burst(_anchor, 30, Color.FromArgb(220, 160, 210, 255), 380, 0.8, 10, 0);
        _c.Animate(0.8, k =>
        {
            _ghost.Opacity = 0.9 * k;
            _ghost.ScaleX = _ghost.ScaleY = 0.3 + 0.7 * Ease.OutBack(k);
        }, () => _ghost.BobAmplitude = _o.Bob);
        _c.After(0.5, () => _c.Animate(0.4, k =>
        {
            _pen.Opacity = k;
            _pen.ScaleX = _pen.ScaleY = _o.PenScale * Ease.OutBack(k);
        }, () => _pen.BobAmplitude = 3));
        _c.After(_o.ArrivalSeconds, () =>
        {
            if (_stage == Stage.Arriving) _stage = Stage.InitialGhost;
            _c.Banner("Draw a circle around the ghost!", _c.Height * 0.2, 2.5, Cyan);
        });
    }

    public void Update(double dt)
    {
        UpdateTipGlow();
        if (_stage == Stage.Done) return;
        if (_c.TimeRemaining <= 0)
        {
            Fail();
            return;
        }
        if (_stage == Stage.InitialGhost) MoveBigGhost(dt);
        foreach (var s in _spirits.Where(s => s.Alive)) MoveSpirit(s, dt);
    }

    void MoveBigGhost(double dt)
    {
        // Slow sinusoidal float; occasionally ease into the other horizontal direction.
        _time += dt;
        _turnIn -= dt;
        if (_turnIn <= 0)
        {
            _dir = -_dir;
            _turnIn = 2 + _c.Random.NextDouble() * 2.5;
        }
        _vx += (_dir * _o.DriftSpeed - _vx) * Math.Min(1, 1.5 * dt);
        _offsetX += _vx * dt;
        if (Math.Abs(_offsetX) > _o.RoamX)
        {
            _offsetX = Math.Sign(_offsetX) * _o.RoamX;
            _dir = -Math.Sign(_offsetX);
        }
        _ghost.X = _anchor.X + _offsetX;
        _ghost.Y = _anchor.Y + _o.RoamY * Math.Sin(_time * 0.8);
        if (Math.Abs(_vx) > 5) _ghost.FlipX = _vx < 0; // art faces right
        if (!_reacting)
            _ghost.Play(_drawing && (PenTip - _ghost.Center).Length < 220 ? "scared" : "idle");
    }

    /// <summary>
    /// Small ghost: a smoothly wandering heading at its own speed, steered back inside the region, kept apart
    /// from the other ghost, and (only while the pen is being dragged) nudged away from the pen.
    /// </summary>
    void MoveSpirit(Spirit s, double dt)
    {
        s.Time += dt;
        s.ScaredFor -= dt;
        s.LaughFor -= dt;
        var heading = s.Heading + s.Turn * s.Time + Math.Sin(s.Time * s.Wobble) * 1.2;
        var desired = new Vector(Math.Cos(heading), Math.Sin(heading)) * s.Speed;

        const double edge = 110;
        var r = _region;
        var p = s.Position;
        if (p.X < r.Left + edge) desired.X += (r.Left + edge - p.X) * 1.5;
        if (p.X > r.Right - edge) desired.X -= (p.X - (r.Right - edge)) * 1.5;
        if (p.Y < r.Top + edge) desired.Y += (r.Top + edge - p.Y) * 1.5;
        if (p.Y > r.Bottom - edge) desired.Y -= (p.Y - (r.Bottom - edge)) * 1.5;

        foreach (var other in _spirits.Where(o => o != s && o.Alive))
        {
            var away = p - other.Position;
            var d = Math.Max(away.Length, 1);
            var separation = Math.Max(_o.Separation, s.Sprite.Width * _o.SmallGhostScale * 1.6); // never closer than about a body apart
            if (d < separation) desired += away / d * s.Speed * (1 - d / separation);
        }

        if (_drawing)
        {
            var fromPen = p - PenTip;
            var d = Math.Max(fromPen.Length, 1);
            if (d < _o.AvoidanceRadius)
            {
                // Nervous, not impossible: weaker when cornered against the region edge.
                var room = Math.Clamp(Math.Min(Math.Min(p.X - r.Left, r.Right - p.X), Math.Min(p.Y - r.Top, r.Bottom - p.Y)) / edge, 0.25, 1);
                desired += fromPen / d * s.Speed * _o.AvoidanceMultiplier * 2 * (1 - d / _o.AvoidanceRadius) * room;
                s.ScaredFor = Math.Max(s.ScaredFor, 0.3);
            }
        }

        s.Velocity += (desired - s.Velocity) * Math.Min(1, 2.2 * dt); // smooth acceleration, no snapping
        var cap = s.Speed * _o.AvoidanceMultiplier;
        if (s.Velocity.Length > cap && _stage != Stage.Splitting) s.Velocity *= cap / s.Velocity.Length;
        s.Position += s.Velocity * dt;
        s.Position = new Point(Math.Clamp(s.Position.X, r.Left, r.Right), Math.Clamp(s.Position.Y, r.Top, r.Bottom));

        s.Sprite.X = s.Position.X;
        s.Sprite.Y = s.Position.Y;
        if (Math.Abs(s.Velocity.X) > 5) s.Sprite.FlipX = s.Velocity.X < 0;
        if (s.LaughFor <= 0)
        {
            // Re-playing the current clip is a no-op, so the loop never restarts. A laugh is left to run its course.
            s.Sprite.PlaybackSpeed = 1;
            s.Sprite.Play(s.ScaredFor > 0 ? "scared" : "idle");
        }
    }

    // ---- drawing -------------------------------------------------------------------------------

    Point PenTip
    {
        get
        {
            var a = PenTilt * Math.PI / 180;
            return new Point(_pen.X - Math.Sin(a) * PenHalfLength, _pen.Y + Math.Cos(a) * PenHalfLength);
        }
    }

    double PenHalfLength => _pen.Height * _o.PenScale / 2;

    /// <summary>A soft glow that rides the pen tip, pulsing between cyan and violet, fading with the pen.</summary>
    void UpdateTipGlow()
    {
        var tip = PenTip + _pen.DrawOffset;
        var pulse = 0.5 + 0.5 * Math.Sin(_c.Elapsed * 4);
        _tipColour.Color = Color.FromArgb(200, (byte)Ease.Lerp(120, 190, pulse), (byte)Ease.Lerp(240, 130, pulse), 255);
        _tipGlow.Opacity = _pen.Opacity * (0.55 + 0.3 * pulse);
        Canvas.SetLeft(_tipGlow, tip.X - _tipGlow.Width / 2);
        Canvas.SetTop(_tipGlow, tip.Y - _tipGlow.Height / 2);
    }

    void MovePenTipTo(Point p)
    {
        var a = PenTilt * Math.PI / 180;
        _pen.X = p.X + Math.Sin(a) * PenHalfLength;
        _pen.Y = p.Y - Math.Cos(a) * PenHalfLength;
    }

    public void OnPointerDown(Point p)
    {
        if (_stage is not (Stage.InitialGhost or Stage.SmallGhosts)) return; // no drawing during arrival or the split
        var grab = _pen.Bounds;
        grab.Inflate(_o.PenGrab, _o.PenGrab);
        if (!grab.Contains(p)) return;

        // Picked up: carried with no ink until the tip reaches a ghost.
        _drawing = true;
        _inking = false;
        _pen.BobAmplitude = 0;
        _c.Audio.Play("pickup");
        _c.Particles.Burst(p, 8, Cyan, 160, 0.45, 5, -80);  // a little magic as it's picked up
        _c.Particles.Burst(p, 5, Purple, 120, 0.45, 5, -80);
        MovePenTipTo(p);
        TryStartInk(p);
    }

    /// <summary>Switch the ink on if the pen tip is within reach of a ghost that can be trapped right now.</summary>
    void TryStartInk(Point tip)
    {
        var targets = _stage == Stage.InitialGhost ? [_ghost] : _spirits.Where(s => s.Alive).Select(s => s.Sprite).ToList();
        if (!targets.Any(t => (t.Center - tip).Length < t.Height * Math.Abs(t.ScaleY) / 2 + _o.InkReach)) return;

        _inking = true;
        _c.Log($"ghost pen: ink on at {tip.X:0},{tip.Y:0}");
        _stroke.Clear();
        _strokeLength = 0;
        _glow = new Polyline { Stroke = ParticleSystem.Frozen(Color.FromArgb(70, Cyan.R, Cyan.G, Cyan.B)), StrokeThickness = 16, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        _core = new Polyline { Stroke = ParticleSystem.Frozen(Color.FromRgb(225, 255, 255)), StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        _head = new Polyline { Stroke = ParticleSystem.Frozen(BrightCyan), StrokeThickness = 6, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        _c.Effects.Children.Add(_glow);
        _c.Effects.Children.Add(_core);
        _c.Effects.Children.Add(_head);
        AddSample(tip);

        // "Ink on": a sparkle, and a glowing nib that rides on the pen tip while it draws.
        _c.Particles.Burst(tip, 12, Cyan, 220, 0.4, 6, 0);
        _nib ??= new Ellipse
        {
            Width = 30,
            Height = 30,
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush(Color.FromArgb(220, 200, 255, 255), Color.FromArgb(0, Cyan.R, Cyan.G, Cyan.B)),
        };
        if (!_c.Effects.Children.Contains(_nib)) _c.Effects.Children.Add(_nib);
        PlaceNib(tip);
    }

    void PlaceNib(Point tip)
    {
        if (_nib == null) return;
        Canvas.SetLeft(_nib, tip.X - _nib.Width / 2);
        Canvas.SetTop(_nib, tip.Y - _nib.Height / 2);
    }

    void RemoveHead()
    {
        if (_head != null) _c.Effects.Children.Remove(_head);
        _head = null;
    }

    void HideNib()
    {
        if (_nib != null) _c.Effects.Children.Remove(_nib);
    }

    public void OnPointerMove(Point p)
    {
        if (!_drawing) return;
        MovePenTipTo(p);
        if (!_inking)
        {
            TryStartInk(p);
            return;
        }
        PlaceNib(p);
        var step = (p - _stroke[^1]).Length;
        if (step < _o.SampleSpacing) return;
        _strokeLength += step;
        AddSample(p);
        _sinceTrailParticle += step;
        if (_sinceTrailParticle > 30)
        {
            _sinceTrailParticle = 0;
            _c.Particles.Burst(p, 2, Cyan, 60, 0.5, 5, -40);
        }
    }

    void AddSample(Point p)
    {
        _stroke.Add(p);
        _glow!.Points.Add(p);
        _core!.Points.Add(p);
        if (_head != null) _head.Points = new PointCollection(_stroke.Skip(Math.Max(0, _stroke.Count - 10))); // the last ~40 DIP glow brighter
    }

    public void OnPointerUp(Point p)
    {
        if (!_drawing || _stage == Stage.Done) return;
        _drawing = false;
        _pen.BobAmplitude = 3;
        HideNib();
        if (!_inking)
        {
            _c.Log("ghost pen: put down without ink"); // just carried: nothing to judge
            return;
        }
        RemoveHead();
        _inking = false;
        var glow = _glow!;
        var core = _core!;
        _glow = _core = null;

        // Judge against where the ghosts are right now.
        var targets = _stage == Stage.InitialGhost ? [_ghost] : _spirits.Where(s => s.Alive).Select(s => s.Sprite).ToList();
        var rules = _stage == Stage.InitialGhost ? _o.LargeLoop : _o.SmallLoop;
        var (verdict, hit) = LoopCheck.Evaluate(_stroke, _strokeLength, targets.Select(t => t.Center).ToList(), rules);
        _c.Log($"ghost loop ({_stage}): {verdict} ({_stroke.Count} pts, {_strokeLength:0} DIP long, ends {(_stroke[^1] - _stroke[0]).Length:0} DIP apart)");

        switch (verdict)
        {
            case LoopVerdict.Captured when _stage == Stage.InitialGhost:
                Split(glow, core);
                break;
            case LoopVerdict.Captured:
                CaptureSpirit(_spirits.First(s => s.Sprite == targets[hit]), glow, core, [.. _stroke]);
                break;
            case LoopVerdict.TooSmall:
                Dissolve(glow, core, 0.25);
                break;
            case LoopVerdict.Open:
                Flash(glow, core, Colors.Red);
                Dissolve(glow, core, 0.5);
                break;
            case LoopVerdict.Missed:
                Flash(glow, core, Colors.Red);
                Dissolve(glow, core, 0.5);
                Laugh(targets.MinBy(t => (t.Center - _stroke[0]).Length)!);
                break;
        }
    }

    static void Flash(Polyline glow, Polyline core, Color color)
    {
        core.Stroke = ParticleSystem.Frozen(color);
        glow.Stroke = ParticleSystem.Frozen(Color.FromArgb(90, color.R, color.G, color.B));
    }

    void Dissolve(Polyline glow, Polyline core, double seconds) =>
        _c.Animate(seconds, k => glow.Opacity = core.Opacity = 1 - k, () =>
        {
            _c.Effects.Children.Remove(glow);
            _c.Effects.Children.Remove(core);
        });

    /// <summary>Pull the drawn loop in onto a point (the "circle tightens" effect).</summary>
    void Collapse(Polyline glow, Polyline core, Point[] loop, Point centre, double seconds, Action<double>? alongside = null, Action? done = null) =>
        _c.Animate(seconds, k =>
        {
            var pts = new PointCollection(loop.Select(p => Ease.Lerp(p, centre, Ease.OutCubic(k))));
            pts.Freeze();
            glow.Points = core.Points = pts;
            alongside?.Invoke(k);
        }, () =>
        {
            _c.Effects.Children.Remove(glow);
            _c.Effects.Children.Remove(core);
            done?.Invoke();
        });

    void Laugh(Sprite who)
    {
        _c.Audio.Play("laugh");
        who.Shake(0.5, 3);
        if (who == _ghost)
        {
            _reacting = true;
            _ghost.Play("laugh", () => _reacting = false);
        }
        else if (_spirits.FirstOrDefault(s => s.Sprite == who) is { } spirit)
        {
            // One laugh stretched over SmallLaughSeconds, rather than the short clip restarting partway through.
            spirit.LaughFor = _o.SmallLaughSeconds;
            spirit.Sprite.PlaybackSpeed = (who.Definition.Clips.GetValueOrDefault("laugh")?.Duration ?? _o.SmallLaughSeconds) / _o.SmallLaughSeconds;
            spirit.Sprite.Play("laugh");
        }
    }

    // ---- stages --------------------------------------------------------------------------------

    /// <summary>
    /// The "capture" fails, theatrically: the loop flashes, the ghost squashes (narrow and tall, trembling),
    /// a cyan/violet flash bursts from it, and two smaller ghosts shoot out on opposite curving paths, wobbling
    /// and trailing wisps, before settling into their own movement. No drawing until it's over.
    /// </summary>
    void Split(Polyline glow, Polyline core)
    {
        _stage = Stage.Splitting;
        Flash(glow, core, BrightCyan);
        _c.Audio.Play("zap");
        _ghost.BobAmplitude = 0;
        _ghost.Play("scared");
        var centre = _ghost.Center;
        var squash = _o.SplitSeconds * 0.4;

        _c.Animate(squash, k =>
        {
            var e = Ease.OutCubic(k);
            _ghost.ScaleX = 1 - 0.3 * e;
            _ghost.ScaleY = 1 + 0.22 * e;
            _ghost.ShakeAmount = 5 * k;
        }, () =>
        {
            Dissolve(glow, core, 0.25);
            _ghost.Opacity = 0; // the big ghost is gone; only the two new ones remain
            _ghost.ShakeAmount = 0;
            _c.Particles.Ring(centre, BrightCyan, 190, 0.45, 12);
            _c.Particles.Ring(centre, Purple, 130, 0.35, 8);
            _c.Particles.Burst(centre, 36, Cyan, 560, 0.7, 8, 0);
            _c.Particles.Burst(centre, 20, Purple, 420, 0.6, 7, 0);
            _c.Audio.Play("laugh");

            for (var i = 0; i < _o.SplitGhostCount; i++)
            {
                var side = i % 2 == 0 ? -1 : 1;
                var sprite = _c.Spawn("ghost", centre.X, centre.Y, onEffectsLayer: true);
                sprite.Opacity = 0.9;
                sprite.ScaleX = sprite.ScaleY = 0;
                sprite.BobAmplitude = _o.SmallBob;
                var spirit = new Spirit
                {
                    Sprite = sprite,
                    Position = centre,
                    // Out and up on one side, out and down on the other; steering bends both into arcs.
                    Velocity = new Vector(side * 340, -side * 180),
                    Speed = _c.Demo ? (i == 0 ? 50 : 60) : _o.SmallSpeedMin + _c.Random.NextDouble() * (_o.SmallSpeedMax - _o.SmallSpeedMin),
                    Heading = side < 0 ? Math.PI : 0,
                    Wobble = 0.7 + i * 0.35 + (_c.Demo ? 0 : _c.Random.NextDouble() * 0.3), // different rhythms
                    Turn = side * 0.15,
                };
                _spirits.Add(spirit);
                _c.Animate(0.3, k => sprite.ScaleX = sprite.ScaleY = _o.SmallGhostScale * Ease.OutBack(k));

                // Wobble away from the other ghost, trailing fading wisps; a startled puff as it settles.
                var sinceWisp = 0.0;
                var last = 0.0;
                _c.Animate(0.6, k =>
                {
                    sprite.Rotation = side * 28 * Math.Sin(k * Math.PI * 2) * (1 - k);
                    sinceWisp += k - last;
                    last = k;
                    if (sinceWisp < 0.06 || !spirit.Alive) return;
                    sinceWisp = 0;
                    _c.Particles.Burst(sprite.Center, 1, k < 0.5 ? Cyan : Purple, 25, 0.45, 14, 0);
                }, () =>
                {
                    sprite.Rotation = 0;
                    if (!spirit.Alive) return;
                    _c.Particles.Burst(sprite.Center, 10, Cyan, 200, 0.4, 5, 0);
                    spirit.ScaredFor = Math.Max(spirit.ScaredFor, 0.7); // startled, then back to idle
                });
            }
            _c.Banner("CATCH THEM BOTH!", _c.Height * 0.2, 2.0, Cyan);
            UpdateStatus();
            _c.After(_o.SplitSeconds - squash, () =>
            {
                if (_stage == Stage.Splitting) _stage = Stage.SmallGhosts;
            });
        });
    }

    void CaptureSpirit(Spirit s, Polyline glow, Polyline core, Point[] loop)
    {
        s.Alive = false;
        var centre = s.Sprite.Center;
        var remaining = _spirits.Count(x => x.Alive);
        Flash(glow, core, Purple);
        _c.Audio.Play("zap");
        UpdateStatus();

        if (remaining > 0)
        {
            // Partial: a small implosion; the survivor panics.
            Collapse(glow, core, loop, centre, 0.35, k =>
            {
                s.Sprite.ScaleX = s.Sprite.ScaleY = _o.SmallGhostScale * (1 - k);
                s.Sprite.Rotation = 540 * k;
            }, () =>
            {
                s.Sprite.Opacity = 0;
                _c.Particles.Burst(centre, 20, Purple, 360, 0.5, 6, 0);
                _c.Popup(remaining == 1 ? "1 REMAINING" : $"{remaining} REMAINING", centre, Cyan, 30);
            });
            foreach (var other in _spirits.Where(x => x.Alive)) other.ScaredFor = 1.5;
            return;
        }

        // Final capture: bigger burst, the circle collapses, brief victory text.
        if (!_c.Resolve(EncounterResult.Victory)) return;
        _stage = Stage.Done;
        Collapse(glow, core, loop, centre, 0.6, k =>
        {
            s.Sprite.ScaleX = s.Sprite.ScaleY = _o.SmallGhostScale * (1 - 0.9 * k);
            s.Sprite.Rotation = 720 * k;
        }, () =>
        {
            s.Sprite.Opacity = 0;
            _c.Particles.Ring(centre, Colors.White, 160, 0.45, 10);
            _c.Particles.Burst(centre, 50, Purple, 600, 0.8, 8, 0);
            _c.Audio.Play("success");
            _c.Banner("GHOSTS BANISHED!", _c.Height * 0.2, 0.8, Purple);
            _c.Animate(0.9, k => _pen.Opacity = 1 - k, _c.Complete);
        });
    }

    void UpdateStatus()
    {
        var left = _spirits.Count(s => s.Alive);
        _c.Status = left == 1 ? "1 GHOST LEFT" : $"{left} GHOSTS LEFT";
    }

    void Fail()
    {
        if (!_c.Resolve(EncounterResult.Failure)) return;
        _stage = Stage.Done;
        if (_glow != null && _core != null) Dissolve(_glow, _core, 0.2);
        _drawing = _inking = false;
        HideNib();
        RemoveHead();
        _c.Audio.Play("failure");
        _c.Audio.Play("laugh");

        // Whatever is still loose laughs and escapes upwards.
        var escaping = _spirits.Count > 0 ? _spirits.Where(s => s.Alive).Select(s => s.Sprite).ToList() : [_ghost];
        _c.ImpactPoint = escaping.Count > 0 ? escaping[0].Center : _ghost.Center;
        foreach (var g in escaping)
        {
            g.Play("laugh");
            var from = new Point(g.X, g.Y);
            var scale = g.ScaleX;
            _c.Animate(1.1, k =>
            {
                g.Y = from.Y - 500 * Ease.InQuad(k);
                g.X = from.X + 120 * Math.Sin(k * Math.PI * 2);
                g.Opacity = 0.9 * (1 - k);
                g.ScaleX = g.ScaleY = scale * (1 + 0.4 * k);
            });
        }
        _c.Animate(1.1, k => _pen.Opacity = 1 - k, _c.Complete);
    }

    public void End()
    {
        _stage = Stage.Done;
        _drawing = _inking = false;
    }
}
