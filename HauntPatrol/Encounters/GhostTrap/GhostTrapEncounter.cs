using System.Windows;
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
    public double SplitSeconds { get; init; } = 0.85;
    public double SmallSpeedMin { get; init; } = 45;
    public double SmallSpeedMax { get; init; } = 65;
    public double AvoidanceRadius { get; init; } = 120;  // only while the pen is being dragged
    public double AvoidanceMultiplier { get; init; } = 1.4;
    public double Separation { get; init; } = 220;      // small ghosts keep this far apart when practical
    public Size SmallRegion { get; init; } = new(1040, 600);
    public LoopRules SmallLoop { get; init; } = new(12, 250, 38);

    public double SampleSpacing { get; init; } = 4;
    public double PenGrab { get; init; } = 30;
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
    bool _drawing, _reacting;
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
        var region = new Rect(_anchor.X - _o.SmallRegion.Width / 2, _anchor.Y - _o.SmallRegion.Height / 2, _o.SmallRegion.Width, _o.SmallRegion.Height);
        region.Intersect(new Rect(90, 190, c.Width - 180, c.Height - 280)); // fully on screen, clear of the HUD
        _region = region;

        // Ghosts are never clicked, so they live on the click-through layer; only the pen intercepts the mouse.
        _ghost = c.Spawn("ghost", _anchor.X, _anchor.Y, onEffectsLayer: true);
        _ghost.Opacity = 0;
        _ghost.ScaleX = _ghost.ScaleY = 0.3;
        _pen = c.Spawn("pen", _anchor.X - 150, _anchor.Y + 30);
        _pen.Opacity = 0;
        _pen.Rotation = PenTilt;
        c.Status = "1 GHOST";
    }

    public void Start()
    {
        _c.Particles.Burst(_anchor, 30, Color.FromArgb(220, 160, 210, 255), 380, 0.8, 10, 0);
        _c.Animate(0.8, k =>
        {
            _ghost.Opacity = 0.9 * k;
            _ghost.ScaleX = _ghost.ScaleY = 0.3 + 0.7 * Ease.OutBack(k);
        }, () => _ghost.BobAmplitude = 6);
        _c.After(0.5, () => _c.Animate(0.4, k =>
        {
            _pen.Opacity = k;
            _pen.ScaleX = _pen.ScaleY = Ease.OutBack(k);
        }, () => _pen.BobAmplitude = 3));
        _c.After(_o.ArrivalSeconds, () =>
        {
            if (_stage == Stage.Arriving) _stage = Stage.InitialGhost;
            _c.Banner("Draw a circle around the ghost!", _c.Height * 0.2, 2.5, Cyan);
        });
    }

    public void Update(double dt)
    {
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
            if (d < _o.Separation) desired += away / d * s.Speed * (1 - d / _o.Separation);
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
        s.Sprite.Play(s.LaughFor > 0 ? "laugh" : s.ScaredFor > 0 ? "scared" : "idle");
    }

    // ---- drawing -------------------------------------------------------------------------------

    Point PenTip
    {
        get
        {
            var a = PenTilt * Math.PI / 180;
            return new Point(_pen.X - Math.Sin(a) * _pen.Height / 2, _pen.Y + Math.Cos(a) * _pen.Height / 2);
        }
    }

    void MovePenTipTo(Point p)
    {
        var a = PenTilt * Math.PI / 180;
        _pen.X = p.X + Math.Sin(a) * _pen.Height / 2;
        _pen.Y = p.Y - Math.Cos(a) * _pen.Height / 2;
    }

    public void OnPointerDown(Point p)
    {
        if (_stage is not (Stage.InitialGhost or Stage.SmallGhosts)) return; // no drawing during arrival or the split
        var grab = _pen.Bounds;
        grab.Inflate(_o.PenGrab, _o.PenGrab);
        if (!grab.Contains(p)) return;

        _drawing = true;
        _pen.BobAmplitude = 0;
        _c.Audio.Play("pickup");
        _stroke.Clear();
        _strokeLength = 0;
        _glow = new Polyline { Stroke = ParticleSystem.Frozen(Color.FromArgb(70, Cyan.R, Cyan.G, Cyan.B)), StrokeThickness = 16, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        _core = new Polyline { Stroke = ParticleSystem.Frozen(Color.FromRgb(225, 255, 255)), StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, IsHitTestVisible = false };
        _c.Effects.Children.Add(_glow);
        _c.Effects.Children.Add(_core);
        MovePenTipTo(p);
        AddSample(p);
    }

    public void OnPointerMove(Point p)
    {
        if (!_drawing) return;
        MovePenTipTo(p);
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
    }

    public void OnPointerUp(Point p)
    {
        if (!_drawing || _stage == Stage.Done) return;
        _drawing = false;
        _pen.BobAmplitude = 3;
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
            spirit.LaughFor = 0.6;
        }
    }

    // ---- stages --------------------------------------------------------------------------------

    /// <summary>The "capture" fails: the ghost squashes, freezes, bursts and splits in two.</summary>
    void Split(Polyline glow, Polyline core)
    {
        _stage = Stage.Splitting;
        Flash(glow, core, Color.FromRgb(120, 255, 120));
        _c.Audio.Play("zap");
        _ghost.BobAmplitude = 0;
        _ghost.Play("scared");
        var centre = _ghost.Center;
        var squash = _o.SplitSeconds * 0.5;

        _c.Animate(squash, k =>
        {
            _ghost.ScaleX = 1 + 0.35 * k;
            _ghost.ScaleY = 1 - 0.4 * k;
            _ghost.ShakeAmount = 4 * k;
        }, () =>
        {
            Dissolve(glow, core, 0.2);
            _ghost.Opacity = 0;
            _ghost.ShakeAmount = 0;
            _c.Particles.Ring(centre, Colors.White, 140, 0.4, 8);
            _c.Particles.Burst(centre, 40, Cyan, 520, 0.7, 8, 0);
            _c.Audio.Play("laugh");

            for (var i = 0; i < _o.SplitGhostCount; i++)
            {
                var side = i % 2 == 0 ? -1 : 1;
                var sprite = _c.Spawn("ghost", centre.X, centre.Y, onEffectsLayer: true);
                sprite.Opacity = 0.9;
                sprite.ScaleX = sprite.ScaleY = 0;
                sprite.BobAmplitude = 4;
                var spirit = new Spirit
                {
                    Sprite = sprite,
                    Position = centre,
                    Velocity = new Vector(side * 280, -60 + i * 40), // fly apart in opposite directions
                    Speed = _c.Demo ? (i == 0 ? 50 : 60) : _o.SmallSpeedMin + _c.Random.NextDouble() * (_o.SmallSpeedMax - _o.SmallSpeedMin),
                    Heading = side < 0 ? Math.PI : 0,
                    Wobble = 0.7 + i * 0.35 + (_c.Demo ? 0 : _c.Random.NextDouble() * 0.3), // different rhythms
                    Turn = side * 0.15,
                };
                _spirits.Add(spirit);
                _c.Animate(0.3, k => sprite.ScaleX = sprite.ScaleY = _o.SmallGhostScale * Ease.OutBack(k));
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
        _drawing = false;
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
        _drawing = false;
    }
}
