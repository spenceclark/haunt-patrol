using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Audio;
using HauntPatrol.Rendering;

namespace HauntPatrol.Effects;

/// <summary>
/// Spectral haunting: blue/purple fog rolls in from the lower corners, ghostly faces surface and fade,
/// glowing eyes blink in the mist, then it all dissipates. Stays in the lower part of the screen, never
/// a full-screen veil. Purely visual and click-through.
/// </summary>
public sealed class SpectralFogEffect(IReadOnlyList<PenaltySurface> surfaces, AudioService audio, SpriteAssetLoader assets, Random random) : IPenaltyEffect
{
    public double Duration => 10;
    public IReadOnlyList<PenaltySurface> Surfaces => surfaces;

    const double FadeSeconds = 2.5, FaceEvery = 1.3, FaceLife = 2.2, EyesEvery = 0.45, EyesLife = 1.6;

    sealed class Puff
    {
        public required Ellipse Shape;
        public Point Position;
        public Vector Velocity;
        public double Size, Phase;
    }

    sealed class Apparition
    {
        public required PenaltySurface Surface;
        public required FrameworkElement Element;
        public Sprite? Sprite;
        public double Age, Life;
    }

    readonly List<Puff> _puffs = [];
    readonly List<Apparition> _apparitions = [];
    double _time, _nextFace = 0.8, _nextEyes = 0.5;

    public void Start()
    {
        audio.Play("fog");
        var blue = Mist(Color.FromRgb(110, 140, 255));
        var purple = Mist(Color.FromRgb(170, 110, 255));

        foreach (var s in surfaces)
        {
            for (var i = 0; i < 14; i++)
            {
                var fromLeft = i % 2 == 0;
                var size = 380 + random.NextDouble() * 380;
                var puff = new Puff
                {
                    Shape = new Ellipse { Width = size, Height = size * 0.6, Fill = i % 3 == 0 ? purple : blue, IsHitTestVisible = false },
                    Size = size,
                    Phase = random.NextDouble() * Math.PI * 2,
                    // Start beyond a lower corner and roll inwards, staggered so the fog keeps arriving.
                    Position = new Point(fromLeft ? -size * (0.6 + i * 0.04) : s.Size.Width + size * (0.6 + i * 0.04) - size,
                                         s.Size.Height - size * 0.3 - random.NextDouble() * s.Size.Height * 0.22),
                    Velocity = new Vector((fromLeft ? 1 : -1) * (110 + random.NextDouble() * 120), -random.NextDouble() * 18),
                };
                s.Canvas.Children.Add(puff.Shape);
                _puffs.Add(puff);
                Place(puff, 0);
            }
        }
    }

    static RadialGradientBrush Mist(Color c)
    {
        var brush = new RadialGradientBrush(
        [
            new GradientStop(Color.FromArgb(140, c.R, c.G, c.B), 0),
            new GradientStop(Color.FromArgb(80, c.R, c.G, c.B), 0.55),
            new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
        ]);
        brush.Freeze();
        return brush;
    }

    public void Update(double dt)
    {
        _time += dt;

        foreach (var p in _puffs)
        {
            p.Position += p.Velocity * dt;
            p.Velocity *= Math.Exp(-0.22 * dt); // rolls in, then lingers
            Place(p, _time);
        }

        var spawning = _time < Duration - FadeSeconds - 1;
        _nextFace -= dt;
        if (spawning && _nextFace <= 0)
        {
            _nextFace = FaceEvery;
            foreach (var s in surfaces) SpawnFace(s);
        }
        _nextEyes -= dt;
        if (spawning && _nextEyes <= 0)
        {
            _nextEyes = EyesEvery;
            foreach (var s in surfaces) SpawnEyes(s);
        }

        for (var i = _apparitions.Count - 1; i >= 0; i--)
        {
            var a = _apparitions[i];
            a.Age += dt;
            if (a.Age >= a.Life)
            {
                a.Surface.Canvas.Children.Remove(a.Element);
                _apparitions.RemoveAt(i);
                continue;
            }
            var k = a.Age / a.Life;
            if (a.Sprite is { } face)
            {
                face.Opacity = 0.55 * Math.Sin(k * Math.PI);
                face.ScaleX = face.ScaleY = 0.85 + 0.3 * k;
                face.Y -= 12 * dt;
                face.Update(dt);
            }
            else
            {
                // Eyes fade in, blink once mid-life, fade out.
                var blink = k is > 0.45 and < 0.52 ? 0 : 1;
                a.Element.Opacity = Math.Sin(k * Math.PI) * blink;
            }
        }

        var opacity = Math.Clamp((Duration - _time) / FadeSeconds, 0, 1);
        foreach (var s in surfaces) s.Canvas.Opacity = opacity;
    }

    static void Place(Puff p, double time)
    {
        Canvas.SetLeft(p.Shape, p.Position.X);
        Canvas.SetTop(p.Shape, p.Position.Y + Math.Sin(time * 0.7 + p.Phase) * 14);
    }

    // Apparitions appear in the lower ~40% of the screen, where the fog is.
    Point InMist(PenaltySurface s) =>
        new(s.Size.Width * (0.08 + random.NextDouble() * 0.84), s.Size.Height * (0.62 + random.NextDouble() * 0.3));

    void SpawnFace(PenaltySurface s)
    {
        var at = InMist(s);
        var face = new Sprite(assets.Get("ghostface")) { X = at.X, Y = at.Y, Opacity = 0, FlipX = random.Next(2) == 0 };
        face.Element.IsHitTestVisible = false;
        s.Canvas.Children.Add(face.Element);
        face.Update(0);
        _apparitions.Add(new Apparition { Surface = s, Element = face.Element, Sprite = face, Life = FaceLife });
    }

    void SpawnEyes(PenaltySurface s)
    {
        var at = InMist(s);
        var glow = ParticleSystem.Frozen(random.Next(3) == 0 ? Color.FromRgb(255, 90, 90) : Color.FromRgb(230, 255, 120));
        var size = 7 + random.NextDouble() * 5;
        var eyes = new Canvas { IsHitTestVisible = false, Opacity = 0 };
        eyes.Children.Add(new Ellipse { Width = size, Height = size * 0.8, Fill = glow });
        var right = new Ellipse { Width = size, Height = size * 0.8, Fill = glow };
        Canvas.SetLeft(right, size * 2.2);
        eyes.Children.Add(right);
        Canvas.SetLeft(eyes, at.X);
        Canvas.SetTop(eyes, at.Y);
        s.Canvas.Children.Add(eyes);
        _apparitions.Add(new Apparition { Surface = s, Element = eyes, Life = EyesLife });
    }

    public void Stop()
    {
        foreach (var s in surfaces) s.Canvas.Children.Clear();
        _puffs.Clear();
        _apparitions.Clear();
    }
}
