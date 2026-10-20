using System.Windows;
using System.Windows.Media;
using HauntPatrol.Core;
using HauntPatrol.Rendering;

namespace HauntPatrol.Encounters;

/// <summary>
/// A witch's cat escapes through a portal. Drag the broom near it to make it flee, and herd it back
/// into the portal before it knocks over the cauldron.
/// </summary>
public sealed class WitchsFamiliarEncounter : IEncounter
{
    public string Title => "Witch's Familiar";
    public double Duration => 25;

    // Steering tuning, DIPs and seconds. Predictable and controllable beats realistic.
    public const double FleeRadius = 270, FleeForce = 4200;
    public const double WanderForce = 240, WanderTurn = 3;
    public const double PortalShyness = 650;  // the cat doesn't *want* to go home on its own
    public const double Damping = 1.8, MaxSpeed = 760;
    public const double CaptureRadius = 100, Margin = 50, HudBand = 120;
    const double ChaseMemory = 1.5; // the portal only counts if the broom drove the cat there

    EncounterContext _c = null!;
    Sprite _cat = null!, _broom = null!, _portal = null!, _cauldron = null!;
    Point _catPos, _broomPos, _lastBroomPos, _portalPos;
    Vector _catVel, _grab;
    double _wander, _hissCooldown, _sinceChased = double.MaxValue;
    bool _hissing, _running;

    // Animation thresholds (presentation only). Each has a gap between "on" and "off" so a value hovering
    // near the line can't flip the clip every frame and keep restarting it from frame 0.
    const double HissNear = 150, HissFar = 180;   // broom distance, DIPs
    const double RunFast = 180, RunSlow = 150;    // cat speed, DIP/s (wandering settles around 133: idle)
    bool _active, _dragging, _escaped, _done;

    /// <summary>One steering step: wander + flee from the broom + mild shyness of the portal, damped and capped.</summary>
    public static Vector Steer(Point cat, Vector velocity, Point broom, Point portal, double wanderAngle, double dt)
    {
        var force = new Vector(Math.Cos(wanderAngle), Math.Sin(wanderAngle)) * WanderForce;
        var away = cat - broom;
        var d = Math.Max(away.Length, 1);
        if (d < FleeRadius)
        {
            force += away / d * FleeForce * (1 - d / FleeRadius);
        }
        else
        {
            var fromPortal = cat - portal;
            var pd = Math.Max(fromPortal.Length, 1);
            var shyRadius = CaptureRadius * 2.5;
            if (pd < shyRadius) force += fromPortal / pd * PortalShyness * (1 - pd / shyRadius);
        }
        velocity += force * dt;
        velocity *= Math.Exp(-Damping * dt);
        if (velocity.Length > MaxSpeed) velocity *= MaxSpeed / velocity.Length;
        return velocity;
    }

    public void Initialize(EncounterContext c)
    {
        _c = c;
        var r = c.Random;
        var portalLeft = !c.Demo && r.Next(2) == 0;
        _portalPos = new Point(portalLeft ? 170 : c.Width - 170, c.Height * (c.Demo ? 0.45 : 0.3 + r.NextDouble() * 0.4));
        _broomPos = _lastBroomPos = new Point(portalLeft ? c.Width * 0.8 : c.Width * 0.2, c.Height * 0.62);
        var cauldronPos = new Point(c.Width * (c.Demo ? 0.5 : 0.35 + r.NextDouble() * 0.3), c.Height * 0.84);

        _portal = c.Spawn("portal", _portalPos.X, _portalPos.Y);
        _cauldron = c.Spawn("cauldron", cauldronPos.X, cauldronPos.Y);
        _broom = c.Spawn("broom", _broomPos.X, _broomPos.Y);
        _cat = c.Spawn("cat", _portalPos.X, _portalPos.Y);

        _catPos = _portalPos;
        _wander = portalLeft ? 0 : Math.PI; // head into the screen
        _catVel = new Vector(Math.Cos(_wander), 0) * 520;

        _portal.ScaleX = _portal.ScaleY = 0;
        _cat.ScaleX = _cat.ScaleY = 0;
        _broom.Opacity = _cauldron.Opacity = 0;
    }

    public void Start()
    {
        _c.Particles.Burst(_portalPos, 30, Color.FromRgb(170, 90, 255), 420, 0.8, 9, 0);
        _c.Animate(0.45, k => _portal.ScaleX = _portal.ScaleY = Ease.OutBack(k));
        _c.After(0.3, () =>
        {
            _active = true;
            _c.Audio.Play("hiss");
            _c.Animate(0.35, k => _cat.ScaleX = _cat.ScaleY = Ease.OutBack(k));
        });
        _c.Animate(0.5, k => _cauldron.Opacity = k);
        _c.After(0.6, () => _c.Animate(0.4, k =>
        {
            _broom.Opacity = k;
            _broom.Y = _broomPos.Y - 60 * (1 - Ease.OutBack(k));
        }));
    }

    public void OnPointerDown(Point p)
    {
        if (_done) return;
        var grabArea = _broom.Bounds;
        grabArea.Inflate(20, 20);
        if (!grabArea.Contains(p)) return;
        _dragging = true;
        _grab = _broomPos - p;
        _c.Audio.Play("pickup");
        _c.Animate(0.15, k => _broom.ScaleX = _broom.ScaleY = 1 + 0.12 * Math.Sin(k * Math.PI));
    }

    public void OnPointerMove(Point p)
    {
        if (!_dragging) return;
        var target = p + _grab;
        _broomPos = new Point(Math.Clamp(target.X, 0, _c.Width), Math.Clamp(target.Y, 0, _c.Height));
    }

    public void OnPointerUp(Point p) => _dragging = false;

    public void Update(double dt)
    {
        _portal.Rotation += 90 * dt;
        if (_done) return;
        if (_c.TimeRemaining <= 0)
        {
            Fail();
            return;
        }

        UpdateBroom(dt);
        if (!_active) return;

        _wander += (_c.Random.NextDouble() * 2 - 1) * WanderTurn * dt;
        var bristles = BroomBristles;
        _catVel = Steer(_catPos, _catVel, bristles, _portalPos, _wander, dt);
        _catPos += _catVel * dt;
        KeepCatOnScreen();

        var broomDistance = (_catPos - bristles).Length;
        _sinceChased = broomDistance < FleeRadius ? 0 : _sinceChased + dt;
        var portalDistance = (_catPos - _portalPos).Length;
        if (!_escaped && portalDistance > CaptureRadius * 1.8) _escaped = true;
        if (_escaped && _sinceChased < ChaseMemory && portalDistance < CaptureRadius)
        {
            Win();
            return;
        }

        _hissCooldown -= dt;
        _hissing = broomDistance < (_hissing ? HissFar : HissNear);
        var speed = _catVel.Length;
        _running = speed > (_running ? RunSlow : RunFast);
        if (_hissing)
        {
            _cat.Play("hiss"); // takes priority over idle/run
            if (_hissCooldown <= 0)
            {
                _c.Audio.Play("hiss");
                _hissCooldown = 2;
            }
        }
        else
        {
            _cat.Play(_running ? "run" : "idle"); // re-playing the current clip is a no-op, so it never restarts
        }
        // The run cycle keeps pace with the cat (0.5x-2.4x); idle and hiss play at their authored rate.
        _cat.PlaybackSpeed = !_hissing && _running ? 0.5 + speed / 400 : 1;
        if (Math.Abs(_catVel.X) > 10) _cat.FlipX = _catVel.X < 0; // art faces right
        _cat.X = _catPos.X;
        _cat.Y = _catPos.Y;
    }

    Point BroomBristles => _broomPos + new Vector(0, _broom.Height * 0.35);

    void UpdateBroom(double dt)
    {
        // Tilt in the direction of travel, eased so it sways rather than snaps.
        var vx = dt > 0 ? (_broomPos.X - _lastBroomPos.X) / dt : 0;
        _lastBroomPos = _broomPos;
        var tilt = Math.Clamp(vx * 0.03, -35, 35);
        _broom.Rotation += (tilt - _broom.Rotation) * Math.Min(1, 10 * dt);
        _broom.X = _broomPos.X;
        if (_broom.Opacity >= 1) _broom.Y = _broomPos.Y;
    }

    void KeepCatOnScreen()
    {
        double minX = Margin, maxX = _c.Width - Margin, minY = Margin + HudBand, maxY = _c.Height - Margin;
        if (_catPos.X < minX || _catPos.X > maxX)
        {
            _catPos.X = Math.Clamp(_catPos.X, minX, maxX);
            _catVel.X = -_catVel.X * 0.5;
            _wander = Math.PI - _wander;
        }
        if (_catPos.Y < minY || _catPos.Y > maxY)
        {
            _catPos.Y = Math.Clamp(_catPos.Y, minY, maxY);
            _catVel.Y = -_catVel.Y * 0.5;
            _wander = -_wander;
        }
    }

    void Win()
    {
        if (!_c.Resolve(EncounterResult.Victory)) return;
        _done = true;
        _dragging = false;
        _c.Audio.Play("success");
        _c.Particles.Burst(_portalPos, 40, Color.FromRgb(200, 140, 255), 500, 0.9, 9, 0);
        var from = _catPos;
        _cat.Play("run");
        _c.Animate(0.6, k =>
        {
            var pos = Ease.Lerp(from, _portalPos, Ease.OutCubic(k));
            _cat.X = pos.X;
            _cat.Y = pos.Y;
            _cat.ScaleX = _cat.ScaleY = 1 - k;
            _cat.Rotation = 540 * k;
        }, () =>
        {
            _c.Particles.Ring(_portalPos, Color.FromRgb(200, 140, 255), 160, 0.5);
            _c.Animate(0.45, k =>
            {
                _portal.ScaleX = _portal.ScaleY = 1 - Ease.InQuad(k);
                _broom.Opacity = _cauldron.Opacity = 1 - k;
            }, _c.Complete);
        });
    }

    void Fail()
    {
        if (!_c.Resolve(EncounterResult.Failure)) return;
        _done = true;
        _dragging = false;
        _cat.Play("run");
        var from = _catPos;
        var side = from.X < _cauldron.X ? -1 : 1;
        var bump = new Point(_cauldron.X + side * _cauldron.Width * 0.55, _cauldron.Y);
        _cat.FlipX = side > 0;
        _c.Animate(0.5, k =>
        {
            var pos = Ease.Lerp(from, bump, k);
            _cat.X = pos.X;
            _cat.Y = pos.Y;
        }, () =>
        {
            _c.Audio.Play("slime");
            var lip = new Point(_cauldron.X, _cauldron.Y - _cauldron.Height / 2);
            _c.Particles.Burst(lip, 50, Color.FromRgb(110, 230, 40), 650, 1.0, 14);
            _c.Animate(0.25, k => _cauldron.Rotation = -side * 100 * Ease.OutCubic(k));
            _c.ImpactPoint = lip;
            var exitX = side < 0 ? -150 : _c.Width + 150;
            _cat.FlipX = side < 0;
            _c.Animate(0.7, k => _cat.X = Ease.Lerp(bump.X, exitX, Ease.InQuad(k)), _c.Complete);
        });
    }

    public void End() => _done = true;
}
