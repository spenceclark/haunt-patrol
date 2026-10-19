using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Core;
using HauntPatrol.Rendering;

namespace HauntPatrol.Encounters;

public enum LoopVerdict { TooSmall, Open, Missed, Captured }

/// <summary>
/// Is a freehand stroke a closed loop around a point? All values in DIPs. Hands rarely end exactly where
/// they started, so a loop is any of: the whole stroke when its end is near its start (the spec rule);
/// the stroke up to where it first passes back near its start (overshoot); or the part enclosed where
/// the stroke crosses its own path (a lasso with a tail). Each loop must itself be big enough.
/// </summary>
public static class LoopCheck
{
    public const int MinPoints = 12;
    public const double MinLength = 450, CloseDistance = 45;

    public static LoopVerdict Evaluate(IReadOnlyList<Point> stroke, double length, Point target)
    {
        if (stroke.Count < MinPoints || length < MinLength) return LoopVerdict.TooSmall;
        var closed = false;
        foreach (var loop in Loops(stroke))
        {
            if (Contains(loop, target)) return LoopVerdict.Captured;
            closed = true;
        }
        return closed ? LoopVerdict.Missed : LoopVerdict.Open;
    }

    static IEnumerable<IReadOnlyList<Point>> Loops(IReadOnlyList<Point> s)
    {
        if ((s[^1] - s[0]).Length <= CloseDistance) yield return s;

        // Overshoot: first point, after a loop's worth of travel, that comes back near the start.
        double travelled = 0;
        for (var k = 1; k < s.Count; k++)
        {
            travelled += (s[k] - s[k - 1]).Length;
            if (travelled >= MinLength && (s[k] - s[0]).Length <= CloseDistance)
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
            if (loop.Count >= MinPoints && Perimeter(loop) >= MinLength) yield return loop;
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

/// <summary>
/// A ghost floats over the desktop. Pick up the magic pen and draw a closed loop around it to trap it.
/// Failed loops fade and you can try again until the timer runs out.
/// </summary>
public sealed class GhostTrapEncounter : IEncounter
{
    public string Title => "Ghost Trap";
    public double Duration => 20;

    // Movement and drawing tuning, DIPs and seconds.
    const double RoamX = 175, RoamY = 90;        // inside the spec's ~350 x 250 region
    const double DriftSpeed = 70, TurnEase = 1.5;
    const double SampleSpacing = 4, PenGrab = 30, PenTilt = 25;
    const double ArrivalSeconds = 1.0;

    EncounterContext _c = null!;
    Sprite _ghost = null!, _pen = null!;
    Point _anchor;
    double _offsetX, _vx, _dir = 1, _turnIn, _time;
    bool _arrived, _drawing, _done, _reacting;

    readonly List<Point> _stroke = [];
    double _strokeLength, _sinceTrailParticle;
    Polyline? _glow, _core;

    static readonly Color Cyan = Color.FromRgb(120, 240, 255);
    static readonly Color Purple = Color.FromRgb(200, 110, 255);

    public void Initialize(EncounterContext c)
    {
        _c = c;
        var r = c.Random;
        _anchor = new Point(c.Width * (c.Demo ? 0.5 : 0.4 + r.NextDouble() * 0.2), c.Height * (c.Demo ? 0.45 : 0.4 + r.NextDouble() * 0.12));
        _turnIn = 2 + r.NextDouble() * 2;

        // The ghost is never clicked, so it lives on the click-through layer; only the pen intercepts the mouse.
        _ghost = c.Spawn("ghost", _anchor.X, _anchor.Y, onEffectsLayer: true);
        _ghost.Opacity = 0;
        _ghost.ScaleX = _ghost.ScaleY = 0.3;
        _pen = c.Spawn("pen", _anchor.X - 150, _anchor.Y + 30);
        _pen.Opacity = 0;
        _pen.Rotation = PenTilt;
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
        _c.After(ArrivalSeconds, () =>
        {
            _arrived = true;
            _c.Banner("Draw a circle around the ghost!", _c.Height * 0.2, 2.5, Cyan);
        });
    }

    public void Update(double dt)
    {
        if (_done) return;
        if (_c.TimeRemaining <= 0)
        {
            Fail();
            return;
        }
        if (!_arrived) return;

        // Slow sinusoidal float; occasionally ease into the other horizontal direction.
        _time += dt;
        _turnIn -= dt;
        if (_turnIn <= 0)
        {
            _dir = -_dir;
            _turnIn = 2 + _c.Random.NextDouble() * 2.5;
        }
        _vx += (_dir * DriftSpeed - _vx) * Math.Min(1, TurnEase * dt);
        _offsetX += _vx * dt;
        if (Math.Abs(_offsetX) > RoamX)
        {
            _offsetX = Math.Sign(_offsetX) * RoamX;
            _dir = -Math.Sign(_offsetX);
        }
        _ghost.X = _anchor.X + _offsetX;
        _ghost.Y = _anchor.Y + RoamY * Math.Sin(_time * 0.8);
        if (Math.Abs(_vx) > 5) _ghost.FlipX = _vx < 0; // art faces right

        if (!_reacting)
            _ghost.Play(_drawing && (PenTip - _ghost.Center).Length < 220 ? "scared" : "idle");
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
        if (!_arrived || _done) return;
        var grab = _pen.Bounds;
        grab.Inflate(PenGrab, PenGrab);
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
        if (step < SampleSpacing) return;
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
        if (!_drawing || _done) return;
        _drawing = false;
        _pen.BobAmplitude = 3;
        var glow = _glow!;
        var core = _core!;
        _glow = _core = null;

        var verdict = LoopCheck.Evaluate(_stroke, _strokeLength, _ghost.Center);
        _c.Log($"ghost loop: {verdict} ({_stroke.Count} pts, {_strokeLength:0} DIP long, ends {(_stroke[^1] - _stroke[0]).Length:0} DIP apart, ghost at {_ghost.Center.X:0},{_ghost.Center.Y:0})");
        switch (verdict)
        {
            case LoopVerdict.Captured:
                Win(glow, core, [.. _stroke]);
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
                Laugh();
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

    void Laugh()
    {
        _reacting = true;
        _c.Audio.Play("laugh");
        _ghost.Play("laugh", () => _reacting = false);
        _ghost.Shake(0.5, 3);
    }

    // ---- outcomes ------------------------------------------------------------------------------

    void Win(Polyline glow, Polyline core, Point[] loop)
    {
        if (!_c.Resolve(EncounterResult.Victory)) return;
        _done = true;
        _ghost.BobAmplitude = 0;
        _ghost.Play("scared");
        Flash(glow, core, Color.FromRgb(120, 255, 120));
        _c.Audio.Play("zap");
        var centre = _ghost.Center;

        _c.After(0.15, () =>
        {
            Flash(glow, core, Purple);
            _c.Animate(0.8, k =>
            {
                var e = Ease.OutCubic(k);
                var pts = new PointCollection(loop.Select(p => Ease.Lerp(p, centre, e)));
                pts.Freeze();
                glow.Points = core.Points = pts;
                _ghost.ScaleX = _ghost.ScaleY = 1 - 0.9 * k;
                _ghost.Rotation = 720 * k;
            }, () =>
            {
                _ghost.Opacity = 0;
                _c.Effects.Children.Remove(glow);
                _c.Effects.Children.Remove(core);
                _c.Particles.Ring(centre, Colors.White, 90, 0.35, 8);
                _c.Particles.Burst(centre, 36, Purple, 520, 0.7, 7, 0);
                _c.Audio.Play("success");
                _c.Animate(0.45, k => _pen.Opacity = 1 - k, _c.Complete);
            });
        });
    }

    void Fail()
    {
        if (!_c.Resolve(EncounterResult.Failure)) return;
        _done = true;
        if (_glow != null && _core != null) Dissolve(_glow, _core, 0.2);
        _drawing = false;
        _reacting = true;
        _ghost.Play("laugh");
        _c.Audio.Play("laugh");
        _c.ImpactPoint = _ghost.Center;
        var from = new Point(_ghost.X, _ghost.Y);
        _c.Animate(1.1, k =>
        {
            _ghost.Y = from.Y - 500 * Ease.InQuad(k);
            _ghost.X = from.X + 120 * Math.Sin(k * Math.PI * 2);
            _ghost.Opacity = 0.9 * (1 - k);
            _ghost.ScaleX = _ghost.ScaleY = 1 + 0.4 * k;
            _pen.Opacity = 1 - k;
        }, _c.Complete);
    }

    public void End()
    {
        _done = true;
        _drawing = false;
    }
}
