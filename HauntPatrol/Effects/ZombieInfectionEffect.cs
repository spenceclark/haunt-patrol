using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Audio;
using HauntPatrol.Rendering;

namespace HauntPatrol.Effects;

/// <summary>
/// Zombie infection: cartoon handprints slap onto the screen one after another, green mould creeps in
/// from the edges with bugs crawling through it, then everything dissolves. Purely visual and click-through.
/// </summary>
public sealed class ZombieInfectionEffect(IReadOnlyList<PenaltySurface> surfaces, AudioService audio, SpriteAssetLoader assets, Random random) : IPenaltyEffect
{
    public double Duration => 10;
    public IReadOnlyList<PenaltySurface> Surfaces => surfaces;

    const double FadeSeconds = 2.5, SlapGap = 0.28, SlapSeconds = 0.18, MouldGrowSeconds = 1.4, BlobSpacing = 70;

    sealed class Handprint
    {
        public required Sprite Sprite;
        public required ParticleSystem Particles;
        public double Delay, Scale;
        public bool Slapped;
    }

    sealed class Blob
    {
        public required Ellipse Shape;
        public Point Centre;
        public double Size, Delay;
    }

    sealed class Bug
    {
        public required Ellipse Shape;
        public required PenaltySurface Surface;
        public double Distance, Speed, Inset, Phase;
    }

    readonly List<Handprint> _hands = [];
    readonly List<Blob> _blobs = [];
    readonly List<Bug> _bugs = [];
    readonly List<ParticleSystem> _particles = [];
    double _time;

    public void Start()
    {
        var mould = new RadialGradientBrush(
        [
            new GradientStop(Color.FromArgb(170, 45, 90, 20), 0),
            new GradientStop(Color.FromArgb(110, 70, 120, 25), 0.6),
            new GradientStop(Color.FromArgb(0, 70, 120, 25), 1),
        ]);
        mould.Freeze();
        var bugBrush = ParticleSystem.Frozen(Color.FromRgb(25, 30, 15));

        foreach (var s in surfaces)
        {
            var particles = new ParticleSystem(s.Canvas, random);
            _particles.Add(particles);
            double w = s.Size.Width, h = s.Size.Height;

            // Mould creeping in from every edge (added first so the handprints sit on top).
            foreach (var (from, to) in new[] { (new Point(0, 0), new Point(w, 0)), (new Point(w, 0), new Point(w, h)), (new Point(w, h), new Point(0, h)), (new Point(0, h), new Point(0, 0)) })
            {
                var length = (to - from).Length;
                for (double d = 0; d <= length; d += BlobSpacing)
                {
                    var blob = new Blob
                    {
                        Shape = new Ellipse { Fill = mould, IsHitTestVisible = false },
                        Centre = Ease.Lerp(from, to, d / length),
                        Size = 90 + random.NextDouble() * 150,
                        Delay = 0.5 + random.NextDouble() * 1.6,
                    };
                    s.Canvas.Children.Add(blob.Shape);
                    _blobs.Add(blob);
                }
            }

            for (var i = 0; i < 10; i++)
            {
                var bug = new Bug
                {
                    Shape = new Ellipse { Width = 7, Height = 5, Fill = bugBrush, IsHitTestVisible = false, Opacity = 0 },
                    Surface = s,
                    Distance = random.NextDouble() * 2 * (w + h),
                    Speed = (random.Next(2) == 0 ? 1 : -1) * (40 + random.NextDouble() * 60),
                    Inset = 14 + random.NextDouble() * 30,
                    Phase = random.NextDouble() * 10,
                };
                s.Canvas.Children.Add(bug.Shape);
                _bugs.Add(bug);
            }

            for (var i = 0; i < 7; i++)
            {
                var at = i == 0 ? s.Impact : new Point(w * (0.15 + random.NextDouble() * 0.7), h * (0.15 + random.NextDouble() * 0.7));
                var hand = new Sprite(assets.Get("handprint"))
                {
                    X = at.X,
                    Y = at.Y,
                    Rotation = (random.NextDouble() - 0.5) * 60,
                    FlipX = random.Next(2) == 0,
                    Opacity = 0,
                };
                hand.Element.IsHitTestVisible = false;
                s.Canvas.Children.Add(hand.Element);
                hand.Update(0);
                _hands.Add(new Handprint { Sprite = hand, Particles = particles, Delay = i * SlapGap, Scale = 0.8 + random.NextDouble() * 0.5 });
            }
        }
    }

    public void Update(double dt)
    {
        _time += dt;

        foreach (var hand in _hands)
        {
            var k = (_time - hand.Delay) / SlapSeconds;
            if (k < 0) continue;
            if (!hand.Slapped)
            {
                hand.Slapped = true;
                audio.Play("slap");
                hand.Particles.Burst(hand.Sprite.Center, 10, Color.FromRgb(90, 120, 40), 300, 0.4, 8);
            }
            var e = Ease.OutBack(Math.Min(1, k));
            hand.Sprite.ScaleX = hand.Sprite.ScaleY = hand.Scale * (1.7 - 0.7 * e); // big, then smack onto the glass
            hand.Sprite.Opacity = Math.Min(1, k) * 0.88;
            hand.Sprite.Update(dt);
        }

        foreach (var b in _blobs)
        {
            var k = Math.Clamp((_time - b.Delay) / MouldGrowSeconds, 0, 1);
            if (k <= 0) continue;
            var size = b.Size * Ease.OutCubic(k);
            b.Shape.Width = b.Shape.Height = size;
            Canvas.SetLeft(b.Shape, b.Centre.X - size / 2);
            Canvas.SetTop(b.Shape, b.Centre.Y - size / 2);
        }

        foreach (var bug in _bugs)
        {
            if (_time < 2) continue; // crawl in once the mould has spread
            bug.Distance += bug.Speed * dt;
            var p = OnBorder(bug.Surface.Size, bug.Inset + Math.Sin(_time * 5 + bug.Phase) * 6, bug.Distance);
            Canvas.SetLeft(bug.Shape, p.X);
            Canvas.SetTop(bug.Shape, p.Y);
            bug.Shape.Opacity = Math.Min(1, (_time - 2) * 2);
        }

        foreach (var p in _particles) p.Update(dt);

        var opacity = Math.Clamp((Duration - _time) / FadeSeconds, 0, 1);
        foreach (var s in surfaces) s.Canvas.Opacity = opacity;
    }

    /// <summary>A point <paramref name="distance"/> along a rectangle inset from the screen edges (wraps around).</summary>
    static Point OnBorder(Size size, double inset, double distance)
    {
        double w = size.Width - 2 * inset, h = size.Height - 2 * inset, perimeter = 2 * (w + h);
        var d = (distance % perimeter + perimeter) % perimeter;
        if (d < w) return new Point(inset + d, inset);
        d -= w;
        if (d < h) return new Point(inset + w, inset + d);
        d -= h;
        if (d < w) return new Point(inset + w - d, inset + h);
        return new Point(inset, inset + h - (d - w));
    }

    public void Stop()
    {
        foreach (var s in surfaces) s.Canvas.Children.Clear();
        _hands.Clear();
        _blobs.Clear();
        _bugs.Clear();
        _particles.Clear();
    }
}
