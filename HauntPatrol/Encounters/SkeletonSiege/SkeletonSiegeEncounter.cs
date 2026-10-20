using System.Windows;
using System.Windows.Controls;
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

/// <summary>Skeleton Siege tuning, DIPs and seconds.</summary>
public sealed class SkeletonSiegeOptions
{
    public double EncounterSeconds { get; init; } = 20;
    public int MaxShots { get; init; } = 3;

    public double PatrolSpeed { get; init; } = 90;
    public double PatrolWidthMin { get; init; } = 300;
    public double PatrolWidthMax { get; init; } = 450;
    public double TurnPause { get; init; } = 0.28;   // brief taunt at each end of the patrol

    public double Gravity { get; init; } = 1500;
    public double LaunchPower { get; init; } = 6.5;  // launch speed per DIP of pull
    public double MaxPull { get; init; } = 230;
    public double MinPull { get; init; } = 25;
    public double GrabRadius { get; init; } = 70;
    public double ProjectileRadius { get; init; } = 27; // half the projectile art (54 DIP)
    public double HitPadding { get; init; } = 30;    // generous on purpose
    public double PreviewSeconds { get; init; } = 0.9; // only part of the arc, so aiming still takes skill
    public double WindUpSeconds { get; init; } = 5;

    /// <summary>Where the hand holds the bone on the last "attack" frame: DIPs from the feet pivot, facing right.</summary>
    public Vector BoneRelease { get; init; } = new(56, -158);
    public double BoneReleaseRotation { get; init; } = 80; // the bone is held upright; bone.png is horizontal
}

/// <summary>
/// A skeleton rises from the desktop and patrols back and forth. Pull the catapult's projectile back
/// slingshot-style and release to lob it; lead the moving target. Three shots, then it hurls a bone
/// at the screen.
/// </summary>
public sealed class SkeletonSiegeEncounter(SkeletonSiegeOptions? options = null) : IEncounter
{
    readonly SkeletonSiegeOptions _o = options ?? new();

    public string Title => "Skeleton Siege";
    public double Duration => _o.EncounterSeconds;

    /// <summary>One patrol step: walk, and at an end pause for <paramref name="turnPause"/>, then reverse.</summary>
    public static (double X, int Dir, double Pause) PatrolStep(double x, int dir, double pause, double dt,
        double min, double max, double speed, double turnPause)
    {
        if (pause > 0)
        {
            pause -= dt;
            return pause > 0 ? (x, dir, pause) : (x, -dir, 0);
        }
        x += dir * speed * dt;
        if (x > min && x < max) return (x, dir, 0);
        x = Math.Clamp(x, min, max);
        return turnPause > 0 ? (x, dir, turnPause) : (x, -dir, 0);
    }

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
    readonly List<Ellipse> _ammoPips = [];

    Phase _phase = Phase.Entering;
    Point _rest, _ballPos;
    Vector _velocity;
    double _groundY, _patrolMin, _patrolMax, _turnPause, _sinceTrail;
    int _dir, _shotsLeft;
    bool _mirrored, _patrolling;

    public void Initialize(EncounterContext c)
    {
        _c = c;
        var r = c.Random;
        _groundY = c.Height * (c.Demo ? 0.78 : 0.62 + r.NextDouble() * 0.2);
        var catapultX = c.Width * (c.Demo ? 0.18 : 0.1 + r.NextDouble() * 0.15);
        var skeletonX = c.Width * (c.Demo ? 0.72 : 0.6 + r.NextDouble() * 0.2);
        _mirrored = !c.Demo && r.Next(2) == 0;
        if (_mirrored)
        {
            catapultX = c.Width - catapultX;
            skeletonX = c.Width - skeletonX;
        }

        var patrolWidth = c.Demo ? (_o.PatrolWidthMin + _o.PatrolWidthMax) / 2 : _o.PatrolWidthMin + r.NextDouble() * (_o.PatrolWidthMax - _o.PatrolWidthMin);
        _patrolMin = Math.Max(80, skeletonX - patrolWidth / 2);
        _patrolMax = Math.Min(c.Width - 80, skeletonX + patrolWidth / 2);
        _dir = r.Next(2) == 0 ? -1 : 1;

        _catapult = c.Spawn("catapult", catapultX, 0);
        var catapultHeight = _catapult.Height;
        _catapult.Y = _groundY - catapultHeight / 2; // props pivot at their centre
        _catapult.FlipX = _mirrored;

        _skeleton = c.Spawn("skeleton", skeletonX, _groundY); // character pivot: feet
        _skeleton.FlipX = _dir < 0; // art faces right; face the way it will walk

        _rest = new Point(catapultX, _groundY - catapultHeight + 12); // in the cup, just below the catapult's top centre
        _ballPos = _rest;
        _ball = c.Spawn("projectile", _rest.X, _rest.Y);

        c.Effects.Children.Add(_arc);
        c.Effects.Children.Add(_band);
        _band.Visibility = Visibility.Hidden;

        // Ammo pips under the catapult, readable at video size.
        _shotsLeft = _o.MaxShots;
        for (var i = 0; i < _o.MaxShots; i++)
        {
            var pip = new Ellipse { Width = 20, Height = 20, Fill = Brushes.Gold, Stroke = Brushes.DarkGoldenrod, StrokeThickness = 2, IsHitTestVisible = false, Opacity = 0 };
            Canvas.SetLeft(pip, catapultX + (i - (_o.MaxShots - 1) / 2.0) * 30 - 10);
            Canvas.SetTop(pip, _groundY + 14);
            c.Effects.Children.Add(pip);
            _ammoPips.Add(pip);
        }
        ShowAmmo();

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
        }, () => _patrolling = true); // starts walking once it has risen

        _c.After(0.35, () => _c.Animate(0.4, k =>
        {
            _catapult.Opacity = k;
            _catapult.ScaleX = _catapult.ScaleY = Ease.OutBack(k);
            foreach (var pip in _ammoPips) pip.Opacity = pip.Tag is "spent" ? 0.25 : k;
        }, () => _c.Animate(0.25, k => _ball.Opacity = k, () =>
        {
            if (_phase == Phase.Entering) _phase = Phase.Ready;
        })));
    }

    void ShowAmmo()
    {
        _c.Status = _shotsLeft switch { 0 => "NO SHOTS LEFT", 1 => "1 SHOT LEFT", var n => $"{n} SHOTS LEFT" };
        for (var i = 0; i < _ammoPips.Count; i++)
        {
            if (i < _shotsLeft) continue;
            _ammoPips[i].Tag = "spent";
            _ammoPips[i].Opacity = 0.25;
        }
    }

    public void OnPointerDown(Point p)
    {
        if (_phase != Phase.Ready || _shotsLeft <= 0) return;
        if ((p - _ballPos).Length > _o.GrabRadius && !_catapult.Bounds.Contains(p)) return;
        _phase = Phase.Aiming;
        _c.Audio.Play("pickup");
        _band.Visibility = Visibility.Visible;
        OnPointerMove(p);
    }

    public void OnPointerMove(Point p)
    {
        if (_phase != Phase.Aiming) return;
        var pull = _rest - p;
        if (pull.Length > _o.MaxPull) pull *= _o.MaxPull / pull.Length;
        _ballPos = _rest - pull;

        _band.X1 = _rest.X;
        _band.Y1 = _rest.Y;
        _band.X2 = _ballPos.X;
        _band.Y2 = _ballPos.Y;

        var points = new PointCollection();
        if (pull.Length >= _o.MinPull)
        {
            Point pos = _rest;
            var v = pull * _o.LaunchPower;
            for (double t = 0; t < _o.PreviewSeconds && pos.Y < _groundY; t += 1 / 30.0)
            {
                points.Add(pos);
                (pos, v) = Ballistics.Step(pos, v, _o.Gravity, 1 / 30.0);
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
        if (pull.Length < _o.MinPull)
        {
            _phase = Phase.Ready; // a nudge, not a shot: no ammo spent
            return;
        }
        _velocity = pull * _o.LaunchPower;
        _phase = Phase.Flying;
        _shotsLeft--;
        ShowAmmo();
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

        if (_patrolling)
        {
            var wasPaused = _turnPause > 0;
            (_skeleton.X, _dir, _turnPause) = PatrolStep(_skeleton.X, _dir, _turnPause, dt, _patrolMin, _patrolMax, _o.PatrolSpeed, _o.TurnPause);
            if (!wasPaused && _turnPause > 0) _skeleton.Shake(_o.TurnPause, 3); // taunt at the turn
            _skeleton.FlipX = _dir < 0;
            _skeleton.Play(_turnPause > 0 ? "idle" : "walk"); // no-op while the clip is already playing
        }

        // Winding up: the skeleton trembles harder as time runs out.
        _skeleton.ShakeAmount = _c.TimeRemaining < _o.WindUpSeconds ? (_o.WindUpSeconds - _c.TimeRemaining) * 0.7 : 0;

        if (_phase == Phase.Flying)
        {
            // Sub-step so a fast ball can't skip across the skeleton; test its current (moving) hitbox each step.
            var steps = (int)Math.Ceiling(dt / (1 / 240.0));
            var target = _skeleton.Bounds;
            target.Inflate(_o.HitPadding, _o.HitPadding);
            for (var i = 0; i < steps; i++)
            {
                var before = _ballPos;
                (_ballPos, _velocity) = Ballistics.Step(_ballPos, _velocity, _o.Gravity, dt / steps);
                if (Ballistics.Hits(_ballPos, _o.ProjectileRadius, target))
                {
                    Hit();
                    return;
                }
                if (_ballPos.Y >= _groundY || _ballPos.X < -200 || _ballPos.X > _c.Width + 200)
                {
                    Miss();
                    break;
                }
                _sinceTrail += (_ballPos - before).Length;
            }
            if (_sinceTrail > 16)
            {
                _sinceTrail = 0;
                _c.Particles.Burst(_ballPos, 1, Color.FromArgb(200, 255, 220, 110), 15, 0.4, 12, 0);
            }
            _ball.Rotation += _velocity.X * dt * 0.6;
        }

        _ball.X = _ballPos.X;
        _ball.Y = _ballPos.Y;
    }

    void Miss()
    {
        _phase = Phase.Reloading;
        var landed = new Point(Math.Clamp(_ballPos.X, 40, _c.Width - 40), Math.Min(_ballPos.Y, _groundY));
        _c.Particles.Burst(landed, 14, Color.FromRgb(160, 140, 110), 260, 0.5, 8);
        _c.Popup("MISS!", new Point(landed.X, landed.Y - 40), Color.FromRgb(255, 120, 90));
        _ball.Opacity = 0;
        _skeleton.Shake(0.35, 4); // rattling laugh
        if (_shotsLeft <= 0)
        {
            _c.After(0.5, Fail); // out of ammo
            return;
        }
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
        _phase = Phase.Done; // stops the patrol and the ball
        _ball.Opacity = 0;
        _skeleton.ShakeAmount = 0;
        var centre = _skeleton.Center;
        _c.Audio.Play("impact");
        _c.Particles.Ring(centre, Colors.White, 170, 0.5);
        _c.Particles.Burst(centre, 40, Color.FromRgb(240, 234, 214), 560, 0.9, 10);
        _c.Particles.Burst(centre, 16, Colors.Gold, 300, 0.6, 6);
        _skeleton.Shake(0.4, 8);
        // The killing shot plays "defeat" straight away: it has its own impact reaction, and "hit" ends in a
        // crouch that doesn't lead into it. "hit" is kept for a non-lethal reaction later.
        _skeleton.Play("defeat", () =>
        {
            _c.Audio.Play("success");
            // The defeat clip ends as a bone pile; just fade it and the props.
            _c.Animate(0.5, k =>
            {
                _skeleton.Opacity = _catapult.Opacity = 1 - k;
                foreach (var pip in _ammoPips) pip.Opacity = Math.Min(pip.Opacity, 1 - k);
            }, _c.Complete);
        });
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
            // Released from the hand on the throw's last frame; the skeleton is empty-handed again.
            // The bone flies "out of the screen" towards the viewer and smacks into the glass.
            var facing = _skeleton.FlipX ? -1 : 1;
            var from = new Point(_skeleton.X + facing * _o.BoneRelease.X, _skeleton.Y + _o.BoneRelease.Y);
            _skeleton.Play("idle");
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
                bone.Rotation = facing * (_o.BoneReleaseRotation + 900 * k);
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
