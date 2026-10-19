using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Core;
using HauntPatrol.Rendering;

namespace HauntPatrol.Encounters;

/// <summary>
/// Zombies climb out of the bottom of the screen and shamble towards a glowing brain. Click them to zap
/// them; brutes take two hits. Two zombies reaching the brain, or the timer, means the desktop is overrun.
/// </summary>
public sealed class ZombieInvasionEncounter : IEncounter
{
    public string Title => "Zombie Invasion";
    public double Duration => 25;

    // Wave and pacing tuning, DIPs and seconds.
    public const int TotalZombies = 8, BrainHealth = 2;
    const int MaxActive = 4;
    const double FirstSpawn = 1.6, DemoSpawnGap = 1.75;
    const double NormalTravel = 7, BruteTravel = 9.5; // seconds from the ground to the brain
    const double BiteRange = 55, MinSpawnGap = 140, ClickPadding = 6;
    const double RiseSeconds = 0.6, HitPause = 0.35, Recoil = 40;
    static readonly bool[] BrutePattern = [false, false, true, false, false, true, false, false];
    static readonly double[] DemoSpawnX = [0.25, 0.7, 0.45, 0.15, 0.85, 0.35, 0.6, 0.5];

    enum ZombieState { Spawning, Walking, Hit, Dying, Removed }

    sealed class Zombie
    {
        public required Sprite Sprite;
        public required bool Brute;
        public int Health;
        public ZombieState State;
        public Point Position;
        public double Speed, StateTime, Phase, SpawnTime;
        public bool Clickable => State is ZombieState.Spawning or ZombieState.Walking or ZombieState.Hit;
    }

    /// <summary>The rules in one place: null while the fight goes on.</summary>
    public static EncounterResult? Judge(int spawned, int remaining, int brainHealth, double timeRemaining) =>
        brainHealth <= 0 ? EncounterResult.Failure
        : spawned == TotalZombies && remaining == 0 ? EncounterResult.Victory
        : timeRemaining <= 0 ? EncounterResult.Failure
        : null;

    EncounterContext _c = null!;
    Sprite _brain = null!, _blaster = null!;
    readonly Ellipse[] _hearts = new Ellipse[BrainHealth];
    readonly List<Zombie> _zombies = [];
    Point _brainPos;
    int _spawned, _brainHealth = BrainHealth;
    double _nextSpawn = FirstSpawn;
    bool _done;

    public void Initialize(EncounterContext c)
    {
        _c = c;
        // ~30% up from the bottom, clear of the taskbar so it stays visible in recordings.
        _brainPos = new Point(c.Width * (c.Demo ? 0.5 : 0.35 + c.Random.NextDouble() * 0.3), c.Height * 0.7);

        // Brain and blaster are never clicked: click-through layer.
        _brain = c.Spawn("brain", _brainPos.X, _brainPos.Y, onEffectsLayer: true);
        _brain.ScaleX = _brain.ScaleY = 0;
        _blaster = c.Spawn("blaster", 80, c.Height * 0.86, onEffectsLayer: true);
        _blaster.Opacity = 0;

        for (var i = 0; i < BrainHealth; i++)
        {
            _hearts[i] = new Ellipse { Width = 16, Height = 16, Fill = ParticleSystem.Frozen(Color.FromRgb(255, 92, 140)), IsHitTestVisible = false, Opacity = 0 };
            System.Windows.Controls.Canvas.SetLeft(_hearts[i], _brainPos.X - 22 + i * 28);
            System.Windows.Controls.Canvas.SetTop(_hearts[i], _brainPos.Y - _brain.Height / 2 - 30);
            c.Effects.Children.Add(_hearts[i]);
        }
    }

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
        }, () => _c.After(FirstSpawn, () => _c.Animate(0.6, k => crack.Opacity = 1 - k)));
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
            foreach (var h in _hearts) h.Opacity = k;
        }, () => _brain.BobAmplitude = 5));
        _c.After(0.6, () => _c.Banner("DEFEND THE BRAIN!", _c.Height * 0.25, 2.2, Color.FromRgb(150, 255, 90)));
    }

    public void Update(double dt)
    {
        if (_done) return;

        _nextSpawn -= dt;
        var active = _zombies.Count(z => z.State != ZombieState.Removed);
        if (_spawned < TotalZombies && _nextSpawn <= 0 && active < MaxActive)
        {
            SpawnZombie();
            _nextSpawn = _c.Demo ? DemoSpawnGap : 1.5 + _c.Random.NextDouble() * 0.5;
        }

        foreach (var z in _zombies) Step(z, dt);
        _zombies.RemoveAll(z => z.State == ZombieState.Removed);

        switch (Judge(_spawned, _zombies.Count, _brainHealth, _c.TimeRemaining))
        {
            case EncounterResult.Victory: Win(); break;
            case EncounterResult.Failure: Fail(); break;
        }
    }

    void SpawnZombie()
    {
        var brute = BrutePattern[_spawned];
        var x = _c.Demo ? _c.Width * DemoSpawnX[_spawned] : PickSpawnX();
        var sprite = _c.Spawn(brute ? "brute" : "zombie", x, _c.Height);
        sprite.ScaleY = 0;
        sprite.Play("walk");
        var start = new Point(x, _c.Height);
        var z = new Zombie
        {
            Sprite = sprite,
            Brute = brute,
            Health = brute ? 2 : 1,
            Position = start,
            Speed = (_brainPos - start).Length / (brute ? BruteTravel : NormalTravel),
            Phase = _c.Random.NextDouble() * Math.PI * 2,
            SpawnTime = _c.Elapsed,
        };
        _zombies.Add(z);
        _spawned++;
        _c.Particles.Burst(new Point(x, _c.Height - 6), 14, Color.FromRgb(110, 80, 50), 300, 0.6, 9);
        if (brute) _c.Audio.Play("groan");
    }

    double PickSpawnX()
    {
        var x = 0.0;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            x = _c.Width * (0.08 + _c.Random.NextDouble() * 0.84);
            if (_zombies.All(z => Math.Abs(z.Position.X - x) > MinSpawnGap)) break;
        }
        return x;
    }

    void Step(Zombie z, double dt)
    {
        z.StateTime += dt;
        switch (z.State)
        {
            case ZombieState.Spawning:
                z.Sprite.ScaleY = Ease.OutBack(Math.Min(1, z.StateTime / RiseSeconds));
                if (z.StateTime >= RiseSeconds) SetState(z, ZombieState.Walking);
                break;

            case ZombieState.Walking:
                var toBrain = _brainPos - z.Position;
                var distance = toBrain.Length;
                if (distance < BiteRange)
                {
                    Bite(z);
                    break;
                }
                z.Position += toBrain / distance * z.Speed * dt;
                z.Sprite.FlipX = toBrain.X < 0; // art faces right
                z.Sprite.Rotation = Math.Sin((_c.Elapsed - z.SpawnTime) * (z.Brute ? 4 : 6) + z.Phase) * 7; // shamble
                break;

            case ZombieState.Hit:
                if (z.StateTime >= HitPause) SetState(z, ZombieState.Walking);
                break;
        }
        z.Sprite.X = z.Position.X;
        z.Sprite.Y = z.Position.Y;
    }

    static void SetState(Zombie z, ZombieState state)
    {
        z.State = state;
        z.StateTime = 0;
        if (state == ZombieState.Walking) z.Sprite.Play("walk");
    }

    public void OnPointerDown(Point p)
    {
        if (_done) return;
        // Topmost (latest added) zombie under the click. The OS only delivers clicks on visible pixels.
        var target = _zombies.LastOrDefault(z =>
        {
            if (!z.Clickable) return false;
            var r = z.Sprite.Bounds;
            r.Inflate(ClickPadding, ClickPadding);
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
        SetState(z, ZombieState.Hit);
        z.Sprite.Play("hit");
        z.Sprite.Shake(0.2, 4);
        var away = z.Position - _brainPos;
        away.Normalize();
        var from = z.Position;
        _c.Animate(0.15, k => { if (z.State == ZombieState.Hit) z.Position = from + away * Recoil * Ease.OutCubic(k); });
    }

    void Bolt(Point from, Point to)
    {
        // A jagged lightning bolt with a soft glow, gone in a blink.
        var points = new PointCollection { from };
        var segments = 7;
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
        _c.Particles.Burst(z.Sprite.Center, z.Brute ? 30 : 20, Color.FromRgb(120, 200, 60), 380, 0.7, 9);
        z.Sprite.Play("die", () => _c.Animate(0.3, k =>
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
            _hearts[_brainHealth].Opacity = 0.2;
            _brain.Play("hit");
            _brain.Shake(0.35, 7);
            _c.Particles.Burst(_brainPos, 18, Color.FromRgb(255, 110, 150), 320, 0.5, 8);
            _c.Audio.Play("impact");
            _c.Animate(0.2, k => z.Sprite.Opacity = 1 - k, () => z.State = ZombieState.Removed);
        });
    }

    void Win()
    {
        if (!_c.Resolve(EncounterResult.Victory)) return;
        _done = true;
        _brain.Play("victory");
        _c.Audio.Play("success");
        _c.Particles.Burst(_brainPos, 40, Color.FromRgb(120, 255, 90), 450, 0.8, 8);
        _c.Banner("INVASION REPELLED", _c.Height * 0.3, 0.7, Color.FromRgb(150, 255, 90));
        _c.Animate(0.6, k => _brain.ScaleX = _brain.ScaleY = 1 + 0.3 * Math.Sin(k * Math.PI * 2) * (1 - k), () =>
            _c.Animate(0.35, k =>
            {
                _brain.Opacity = _blaster.Opacity = 1 - k;
                foreach (var h in _hearts) h.Opacity = 1 - k;
            }, _c.Complete));
    }

    void Fail()
    {
        if (!_c.Resolve(EncounterResult.Failure)) return;
        _done = true;
        _c.Audio.Play("failure");
        _brain.Play("hit");
        _brain.Shake(0.8, 8);
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
