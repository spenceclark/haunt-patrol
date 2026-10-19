using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Core;
using HauntPatrol.Rendering;

namespace HauntPatrol.Encounters;

/// <summary>Pure 2D projectile physics (DIPs, seconds, +Y down).</summary>
public static class Ballistics
{
    public static (Point Position, Vector Velocity) Step(Point p, Vector v, double gravity, double dt)
    {
        v.Y += gravity * dt;
        return (p + v * dt, v);
    }

    public static bool Hits(Point centre, double radius, Rect target)
    {
        var dx = centre.X - Math.Clamp(centre.X, target.Left, target.Right);
        var dy = centre.Y - Math.Clamp(centre.Y, target.Top, target.Bottom);
        return dx * dx + dy * dy <= radius * radius;
    }
}

/// <summary>
/// A skeleton rises from the desktop; pull the catapult's projectile back slingshot-style and release
/// to lob it in a ballistic arc. Hit before the timer runs out or the skeleton hurls a bone at the screen.
/// </summary>
public sealed class SkeletonSiegeEncounter : IEncounter
{
    public string Title => "Skeleton Siege";
    public double Duration => 20;

    // Gameplay tuning, DIPs and seconds.
    public const double Gravity = 1500;
    public const double LaunchPower = 6.5;   // launch speed per DIP of pull
    const double MaxPull = 230, MinPull = 25, GrabRadius = 70;
    const double ProjectileRadius = 18, HitPadding = 30; // generous on purpose
    const double PreviewSeconds = 0.9;               // only part of the arc, so aiming still takes skill
    const double WindUpSeconds = 5;

    enum Phase { Entering, Ready, Aiming, Flying, Reloading, Done }

    EncounterContext _c = null!;
    Sprite _skeleton = null!, _catapult = null!, _ball = null!;
    readonly Polyline _arc = new()
    {
        Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 230, 120)),
        StrokeThickness = 4,
        StrokeDashArray = [0.5, 2],
        StrokeDashCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };
    readonly Line _band = new() { Stroke = new SolidColorBrush(Color.FromRgb(120, 72, 36)), StrokeThickness = 5, IsHitTestVisible = false };

    Phase _phase = Phase.Entering;
    Point _rest, _ballPos;
    Vector _velocity;
    double _groundY;
    bool _mirrored;

    public void Initialize(EncounterContext c)
    {
        _c = c;
        var r = c.Random;
        _groundY = c.Height * (c.Demo ? 0.78 : 0.62 + r.NextDouble() * 0.2);
        var catapultX = c.Width * (c.Demo ? 0.18 : 0.1 + r.NextDouble() * 0.15);
        var skeletonX = c.Width * (c.Demo ? 0.72 : 0.58 + r.NextDouble() * 0.27);
        _mirrored = !c.Demo && r.Next(2) == 0;
        if (_mirrored)
        {
            catapultX = c.Width - catapultX;
            skeletonX = c.Width - skeletonX;
        }

        _catapult = c.Spawn("catapult", catapultX, 0);
        var catapultHeight = _catapult.Height;
        _catapult.Y = _groundY - catapultHeight / 2; // props pivot at their centre
        _catapult.FlipX = _mirrored;

        _skeleton = c.Spawn("skeleton", skeletonX, _groundY); // character pivot: feet
        _skeleton.FlipX = !_mirrored; // face the catapult

        _rest = new Point(catapultX, _groundY - catapultHeight + 8);
        _ballPos = _rest;
        _ball = c.Spawn("projectile", _rest.X, _rest.Y);

        c.Effects.Children.Add(_arc);
        c.Effects.Children.Add(_band);
        _band.Visibility = Visibility.Hidden;

        _skeleton.ScaleY = 0;
        _skeleton.Opacity = 0;
        _catapult.Opacity = 0;
        _ball.Opacity = 0;
    }

    public void Start()
    {
        _c.Particles.Burst(new Point(_skeleton.X, _groundY), 26, Color.FromRgb(150, 130, 110), 380, 0.7, 10);
        _c.Animate(0.6, k =>
        {
            _skeleton.ScaleY = Ease.OutBack(k);
            _skeleton.Opacity = Math.Min(1, k * 2);
        }, () => _skeleton.BobAmplitude = 3);

        _c.After(0.35, () => _c.Animate(0.4, k =>
        {
            _catapult.Opacity = k;
            _catapult.ScaleX = _catapult.ScaleY = Ease.OutBack(k);
        }, () => _c.Animate(0.25, k => _ball.Opacity = k, () =>
        {
            if (_phase == Phase.Entering) _phase = Phase.Ready;
        })));
    }

    public void OnPointerDown(Point p)
    {
        if (_phase != Phase.Ready) return;
        if ((p - _ballPos).Length > GrabRadius && !_catapult.Bounds.Contains(p)) return;
        _phase = Phase.Aiming;
        _c.Audio.Play("pickup");
        _band.Visibility = Visibility.Visible;
        OnPointerMove(p);
    }

    public void OnPointerMove(Point p)
    {
        if (_phase != Phase.Aiming) return;
        var pull = _rest - p;
        if (pull.Length > MaxPull) pull *= MaxPull / pull.Length;
        _ballPos = _rest - pull;

        _band.X1 = _rest.X;
        _band.Y1 = _rest.Y;
        _band.X2 = _ballPos.X;
        _band.Y2 = _ballPos.Y;

        var points = new PointCollection();
        if (pull.Length >= MinPull)
        {
            Point pos = _rest;
            var v = pull * LaunchPower;
            for (double t = 0; t < PreviewSeconds && pos.Y < _groundY; t += 1 / 30.0)
            {
                points.Add(pos);
                (pos, v) = Ballistics.Step(pos, v, Gravity, 1 / 30.0);
            }
        }
        _arc.Points = points;
    }

    public void OnPointerUp(Point p)
    {
        if (_phase != Phase.Aiming) return;
        var pull = _rest - _ballPos;
        ClearAim();
        _ballPos = _rest;
        if (pull.Length < MinPull)
        {
            _phase = Phase.Ready;
            return;
        }
        _velocity = pull * LaunchPower;
        _phase = Phase.Flying;
        _c.Audio.Play("launch");
        var recoil = _mirrored ? 1 : -1;
        _c.Animate(0.3, k =>
        {
            _catapult.Rotation = recoil * 10 * Math.Sin(k * Math.PI);
            _catapult.ScaleY = 1 - 0.12 * Math.Sin(k * Math.PI);
        });
    }

    public void Update(double dt)
    {
        if (_phase == Phase.Done) return;
        if (_c.TimeRemaining <= 0)
        {
            Fail();
            return;
        }

        // Winding up: the skeleton trembles harder as time runs out.
        _skeleton.ShakeAmount = _c.TimeRemaining < WindUpSeconds ? (WindUpSeconds - _c.TimeRemaining) * 0.7 : 0;

        if (_phase == Phase.Flying)
        {
            // Sub-step so a dropped frame can't tunnel through the skeleton.
            var steps = (int)Math.Ceiling(dt / (1 / 240.0));
            for (var i = 0; i < steps; i++)
            {
                (_ballPos, _velocity) = Ballistics.Step(_ballPos, _velocity, Gravity, dt / steps);
                var target = _skeleton.Bounds;
                target.Inflate(HitPadding, HitPadding);
                if (Ballistics.Hits(_ballPos, ProjectileRadius, target))
                {
                    Hit();
                    return;
                }
                if (_ballPos.Y >= _groundY || _ballPos.X < -200 || _ballPos.X > _c.Width + 200)
                {
                    Miss();
                    break;
                }
            }
            _ball.Rotation += _velocity.X * dt * 0.6;
        }

        _ball.X = _ballPos.X;
        _ball.Y = _ballPos.Y;
    }

    void Miss()
    {
        _phase = Phase.Reloading;
        _c.Particles.Burst(new Point(_ballPos.X, Math.Min(_ballPos.Y, _groundY)), 14, Color.FromRgb(160, 140, 110), 260, 0.5, 8);
        _ball.Opacity = 0;
        _skeleton.Shake(0.35, 4); // rattling laugh
        _c.After(0.45, () =>
        {
            if (_phase != Phase.Reloading) return;
            _ballPos = _rest;
            _c.Animate(0.2, k => _ball.Opacity = k);
            _phase = Phase.Ready;
        });
    }

    void Hit()
    {
        if (!_c.Resolve(EncounterResult.Victory)) return;
        _phase = Phase.Done;
        _ball.Opacity = 0;
        _skeleton.ShakeAmount = 0;
        _skeleton.BobAmplitude = 0;
        var centre = _skeleton.Center;
        _c.Audio.Play("impact");
        _c.Particles.Ring(centre, Colors.White, 170, 0.5);
        _c.Particles.Burst(centre, 40, Color.FromRgb(240, 234, 214), 560, 0.9, 10);
        _c.Particles.Burst(centre, 16, Colors.Gold, 300, 0.6, 6);
        _skeleton.Shake(0.4, 8);
        _skeleton.Play("hit", () => _skeleton.Play("defeat", () =>
        {
            _c.Audio.Play("success");
            _c.Animate(0.5, k =>
            {
                _skeleton.Opacity = _catapult.Opacity = 1 - k;
                _skeleton.ScaleY = 1 - 0.7 * k;
                _skeleton.Rotation = (_skeleton.FlipX ? 1 : -1) * 25 * k;
            }, _c.Complete);
        }));
    }

    void Fail()
    {
        if (!_c.Resolve(EncounterResult.Failure)) return;
        _phase = Phase.Done;
        ClearAim();
        _ball.Opacity = 0;
        _skeleton.ShakeAmount = 0;
        _c.Audio.Play("failure");
        _skeleton.Play("attack", () =>
        {
            // The bone flies "out of the screen" towards the viewer and smacks into the glass.
            var from = new Point(_skeleton.X, _skeleton.Y - _skeleton.Height * 0.75);
            var target = _c.Demo
                ? new Point(_c.Width / 2, _c.Height / 2)
                : new Point(_c.Width * (0.35 + _c.Random.NextDouble() * 0.3), _c.Height * (0.3 + _c.Random.NextDouble() * 0.3));
            var bone = _c.Spawn("bone", from.X, from.Y, onEffectsLayer: true);
            _c.Audio.Play("launch");
            _c.Animate(0.6, k =>
            {
                var e = Ease.InQuad(k);
                var pos = Ease.Lerp(from, target, e);
                bone.X = pos.X;
                bone.Y = pos.Y - Math.Sin(k * Math.PI) * 140;
                bone.ScaleX = bone.ScaleY = 1 + 9 * e;
                bone.Rotation = 900 * k;
            }, () =>
            {
                bone.Opacity = 0;
                _c.ImpactPoint = target;
                _c.Complete();
            });
        });
    }

    void ClearAim()
    {
        _arc.Points = [];
        _band.Visibility = Visibility.Hidden;
    }

    public void End() => _phase = Phase.Done;
}
