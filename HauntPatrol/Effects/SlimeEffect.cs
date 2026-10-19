using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Audio;
using HauntPatrol.Rendering;
using static HauntPatrol.Rendering.ParticleSystem;

namespace HauntPatrol.Effects;

/// <summary>
/// Green slime: splats pop out from the impact and slowly ooze down, a slime band clings to the top
/// edge with drips stretching from it and dropping off, bubbles pop, then it all dissolves.
/// </summary>
public sealed class SlimeEffect(IReadOnlyList<PenaltySurface> surfaces, AudioService audio, SpriteAssetLoader assets, Random random) : IPenaltyEffect
{
    public double Duration => 10;
    public IReadOnlyList<PenaltySurface> Surfaces => surfaces;

    const double FadeSeconds = 2.5, OozeSpeed = 7, DropGravity = 1600;
    static readonly Color SlimeLight = Color.FromRgb(150, 240, 70), SlimeDark = Color.FromRgb(60, 170, 20);

    sealed class Splat
    {
        public required Sprite Sprite;
        public double Delay, Size;
    }

    sealed class Drip
    {
        public required Rectangle Body;
        public required Ellipse Tip;
        public double X, Width, Length, MaxLength, Speed, Delay, Bottom, DropVelocity;
        public double? DropY;
    }

    sealed class Bubble
    {
        public required Ellipse Shape;
        public Point At;
        public double Age, Size;
    }

    readonly List<Splat> _splats = [];
    readonly List<Drip> _drips = [];
    readonly List<Bubble> _bubbles = [];
    readonly List<ParticleSystem> _particles = [];
    double _time, _nextBubble;

    public void Start()
    {
        audio.Play("slime");
        var body = new LinearGradientBrush(SlimeLight, SlimeDark, 0);
        body.Freeze();
        var tip = Frozen(SlimeDark);

        foreach (var s in surfaces)
        {
            var particles = new ParticleSystem(s.Canvas, random);
            _particles.Add(particles);
            particles.Burst(s.Impact, 45, SlimeLight, 800, 0.9, 16, 1400);

            s.Canvas.Children.Add(TopBand(s.Size.Width));

            for (var i = 0; i < 9; i++)
            {
                var spread = i == 0 ? 0 : 140 + random.NextDouble() * 520;
                var angle = random.NextDouble() * Math.PI * 2;
                var at = s.Impact + new Vector(Math.Cos(angle), Math.Sin(angle)) * spread;
                var sprite = new Sprite(assets.Get("slime")) { X = at.X, Y = at.Y, Rotation = random.NextDouble() * 360, ScaleX = 0, ScaleY = 0 };
                sprite.Element.IsHitTestVisible = false;
                s.Canvas.Children.Add(sprite.Element);
                _splats.Add(new Splat { Sprite = sprite, Delay = i * 0.05, Size = i == 0 ? 1.7 : 0.5 + random.NextDouble() * 0.8 });
                sprite.Update(0);
            }

            for (var i = 0; i < 16; i++)
            {
                var width = 10 + random.NextDouble() * 24;
                var drip = new Drip
                {
                    Bottom = s.Size.Height + 100,
                    Body = new Rectangle { Width = width, Fill = body, RadiusX = width / 2, RadiusY = width / 2 },
                    Tip = new Ellipse { Width = width * 1.5, Height = width * 1.6, Fill = tip },
                    X = random.NextDouble() * s.Size.Width,
                    Width = width,
                    MaxLength = 120 + random.NextDouble() * 420,
                    Speed = 40 + random.NextDouble() * 110,
                    Delay = random.NextDouble() * 1.5,
                };
                s.Canvas.Children.Add(drip.Body);
                s.Canvas.Children.Add(drip.Tip);
                _drips.Add(drip);
            }
        }
    }

    /// <summary>A scalloped slime band along the upper screen edge.</summary>
    Path TopBand(double width)
    {
        var figure = new PathFigure { StartPoint = new Point(0, 0), IsClosed = true };
        for (double x = 0; x <= width + 60; x += 60)
            figure.Segments.Add(new QuadraticBezierSegment(new Point(x + 30, 30 + random.NextDouble() * 50), new Point(x + 60, 14), true));
        figure.Segments.Add(new LineSegment(new Point(width + 60, 0), true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return new Path { Data = geometry, Fill = Frozen(SlimeDark), Opacity = 0.92 };
    }

    public void Update(double dt)
    {
        _time += dt;

        foreach (var s in _splats)
        {
            var k = Math.Clamp((_time - s.Delay) / 0.35, 0, 1);
            s.Sprite.ScaleX = s.Sprite.ScaleY = s.Size * Ease.OutBack(k);
            if (k >= 1) s.Sprite.Y += OozeSpeed * dt;
            s.Sprite.Update(dt);
        }

        foreach (var d in _drips)
        {
            if (_time < d.Delay) continue;
            d.Length = Math.Min(d.MaxLength, d.Length + d.Speed * dt);
            d.Body.Height = d.Length;
            Canvas.SetLeft(d.Body, d.X - d.Width / 2);
            Canvas.SetTop(d.Body, 0);

            // At full length a drop breaks off and falls (using the tip); the drip starts regrowing.
            if (d.DropY is double y)
            {
                d.DropVelocity += DropGravity * dt;
                y += d.DropVelocity * dt;
                d.DropY = y > d.Bottom ? null : y;
            }
            else if (d.Length >= d.MaxLength)
            {
                d.DropY = d.Length;
                d.DropVelocity = 0;
                d.Length = d.MaxLength * 0.6;
            }
            PlaceTip(d, new Point(d.X, d.DropY ?? d.Length));
        }

        _nextBubble -= dt;
        if (_nextBubble <= 0 && _time < Duration - FadeSeconds && _splats.Count > 0)
        {
            _nextBubble = 0.12;
            var host = _splats[random.Next(_splats.Count)];
            var at = host.Sprite.Center + new Vector(random.NextDouble() - 0.5, random.NextDouble() - 0.5) * 80 * host.Size;
            var shape = new Ellipse { Stroke = Frozen(Color.FromArgb(220, 220, 255, 180)), StrokeThickness = 2, Fill = Frozen(Color.FromArgb(60, 220, 255, 180)) };
            ((Canvas)VisualTreeHelper.GetParent(host.Sprite.Element)).Children.Add(shape);
            _bubbles.Add(new Bubble { Shape = shape, At = at, Size = 8 + random.NextDouble() * 22 });
        }
        for (var i = _bubbles.Count - 1; i >= 0; i--)
        {
            var b = _bubbles[i];
            b.Age += dt;
            if (b.Age > 0.7)
            {
                ((Canvas)VisualTreeHelper.GetParent(b.Shape)).Children.Remove(b.Shape);
                _bubbles.RemoveAt(i);
                continue;
            }
            var size = b.Size * Ease.OutCubic(b.Age / 0.7);
            b.Shape.Width = b.Shape.Height = size;
            Canvas.SetLeft(b.Shape, b.At.X - size / 2);
            Canvas.SetTop(b.Shape, b.At.Y - size / 2);
        }

        foreach (var p in _particles) p.Update(dt);

        var opacity = Math.Clamp((Duration - _time) / FadeSeconds, 0, 1);
        foreach (var s in surfaces) s.Canvas.Opacity = opacity;
    }

    static void PlaceTip(Drip d, Point at)
    {
        Canvas.SetLeft(d.Tip, at.X - d.Tip.Width / 2);
        Canvas.SetTop(d.Tip, at.Y - d.Tip.Height * 0.6);
    }

    public void Stop()
    {
        foreach (var s in surfaces) s.Canvas.Children.Clear();
        _splats.Clear();
        _drips.Clear();
        _bubbles.Clear();
        _particles.Clear();
    }
}
