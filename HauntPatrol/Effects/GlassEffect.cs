using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HauntPatrol.Audio;
using HauntPatrol.Rendering;
using static HauntPatrol.Rendering.ParticleSystem;

namespace HauntPatrol.Effects;

/// <summary>
/// Cracked screen: a flash at the impact point, jagged radial fractures that race outward, ring
/// cracks joining them, glass fragments falling away, then a fade. Purely visual and click-through.
/// </summary>
public sealed class GlassEffect(IReadOnlyList<PenaltySurface> surfaces, AudioService audio, Random random) : IPenaltyEffect
{
    public double Duration => 10;
    public IReadOnlyList<PenaltySurface> Surfaces => surfaces;

    const double GrowSeconds = 0.35, FadeSeconds = 2, ShardGravity = 1400;
    static readonly int[] RingRadii = [55, 130, 240, 390];

    sealed record Crack(Polyline Shadow, Polyline Line, Point[] Points, double Delay);

    sealed class Shard
    {
        public required Polygon Shape;
        public required RotateTransform Spin;
        public Point Position;
        public Vector Velocity;
        public double SpinSpeed;
    }

    readonly List<Crack> _cracks = [];
    readonly List<Shard> _shards = [];
    readonly List<ParticleSystem> _particles = [];
    double _time;

    public void Start()
    {
        audio.Play("glass");
        foreach (var s in surfaces)
        {
            var particles = new ParticleSystem(s.Canvas, random);
            _particles.Add(particles);
            particles.Ring(s.Impact, Colors.White, 140, 0.35, 10);
            particles.Burst(s.Impact, 24, Color.FromArgb(230, 220, 240, 255), 700, 0.6, 5, 1200);

            BuildCracks(s);
            for (var i = 0; i < 26; i++) AddShard(s);
        }
    }

    void BuildCracks(PenaltySurface s)
    {
        var reach = Math.Max(s.Size.Width, s.Size.Height) * 0.7;
        const int rays = 18;
        var rayPoints = new List<List<Point>>();

        for (var i = 0; i < rays; i++)
        {
            var angle = 2 * Math.PI * i / rays + (random.NextDouble() - 0.5) * 0.3;
            var length = reach * (0.3 + random.NextDouble() * 0.7);
            var p = s.Impact;
            var points = new List<Point> { p };
            for (double d = 0; d < length;)
            {
                var step = 22 + random.NextDouble() * 48;
                angle += (random.NextDouble() - 0.5) * 0.45;
                p += new Vector(Math.Cos(angle), Math.Sin(angle)) * step;
                d += step;
                points.Add(p);
            }
            rayPoints.Add(points);
            AddCrack(s.Canvas, points, 0);
        }

        // Concentric cracks between neighbouring rays.
        for (var ring = 0; ring < RingRadii.Length; ring++)
        {
            for (var i = 0; i < rays; i++)
            {
                if (random.NextDouble() > 0.75) continue;
                var a = PointAtRadius(rayPoints[i], s.Impact, RingRadii[ring]);
                var b = PointAtRadius(rayPoints[(i + 1) % rays], s.Impact, RingRadii[ring]);
                if (a is not Point pa || b is not Point pb) continue;
                var mid = Ease.Lerp(pa, pb, 0.5) + new Vector(random.NextDouble() - 0.5, random.NextDouble() - 0.5) * 18;
                AddCrack(s.Canvas, [pa, mid, pb], 0.12 + ring * 0.06);
            }
        }
    }

    static Point? PointAtRadius(List<Point> ray, Point origin, double radius)
    {
        foreach (var p in ray)
            if ((p - origin).Length >= radius) return p;
        return null;
    }

    void AddCrack(Canvas canvas, IList<Point> points, double delay)
    {
        var shadow = new Polyline { Stroke = Frozen(Color.FromArgb(120, 0, 0, 0)), StrokeThickness = 3.5, StrokeLineJoin = PenLineJoin.Round };
        var line = new Polyline { Stroke = Frozen(Color.FromArgb(235, 255, 255, 255)), StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
        canvas.Children.Add(shadow);
        canvas.Children.Add(line);
        _cracks.Add(new Crack(shadow, line, [.. points], delay));
    }

    void AddShard(PenaltySurface s)
    {
        var size = 6 + random.NextDouble() * 16;
        var spin = new RotateTransform();
        var shape = new Polygon
        {
            Points = [new(0, 0), new(size, size * 0.3), new(size * 0.4, size)],
            Fill = Frozen(Color.FromArgb(150, 225, 240, 255)),
            Stroke = Frozen(Color.FromArgb(220, 255, 255, 255)),
            StrokeThickness = 1,
            RenderTransform = spin,
        };
        s.Canvas.Children.Add(shape);
        var angle = random.NextDouble() * Math.PI * 2;
        _shards.Add(new Shard
        {
            Shape = shape,
            Spin = spin,
            Position = s.Impact + new Vector(Math.Cos(angle), Math.Sin(angle)) * random.NextDouble() * 50,
            Velocity = new Vector(Math.Cos(angle) * (60 + random.NextDouble() * 260), -80 - random.NextDouble() * 240),
            SpinSpeed = (random.NextDouble() - 0.5) * 720,
        });
    }

    public void Update(double dt)
    {
        _time += dt;

        foreach (var c in _cracks)
        {
            var k = Math.Clamp((_time - c.Delay) / GrowSeconds, 0, 1);
            var count = (int)Math.Ceiling(c.Points.Length * k);
            if (c.Line.Points.Count == count) continue;
            var visible = new PointCollection(c.Points.Take(count));
            visible.Freeze();
            c.Line.Points = c.Shadow.Points = visible;
        }

        for (var i = _shards.Count - 1; i >= 0; i--)
        {
            var s = _shards[i];
            s.Velocity.Y += ShardGravity * dt;
            s.Position += s.Velocity * dt;
            s.Spin.Angle += s.SpinSpeed * dt;
            Canvas.SetLeft(s.Shape, s.Position.X);
            Canvas.SetTop(s.Shape, s.Position.Y);
            if (s.Position.Y > 5000) _shards.RemoveAt(i); // long gone below the screen
        }

        foreach (var p in _particles) p.Update(dt);

        var opacity = Math.Clamp((Duration - _time) / FadeSeconds, 0, 1);
        foreach (var s in surfaces) s.Canvas.Opacity = opacity;
    }

    public void Stop()
    {
        foreach (var s in surfaces) s.Canvas.Children.Clear();
        _cracks.Clear();
        _shards.Clear();
        _particles.Clear();
    }
}
