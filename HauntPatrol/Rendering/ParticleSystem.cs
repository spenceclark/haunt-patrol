using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HauntPatrol.Rendering;

/// <summary>Short-lived Ellipse particles and expanding rings on a (passive) canvas.</summary>
public sealed class ParticleSystem(Canvas canvas, Random random)
{
    sealed class Particle
    {
        public required Ellipse Shape;
        public Point Position;
        public Vector Velocity;
        public double Age, Life, Gravity, Size, RingRadius;
    }

    readonly List<Particle> _live = [];

    public void Burst(Point at, int count, Color color, double speed, double life, double size = 8, double gravity = 900)
    {
        var brush = Frozen(color);
        for (var i = 0; i < count; i++)
        {
            var angle = random.NextDouble() * Math.PI * 2;
            var v = new Vector(Math.Cos(angle), Math.Sin(angle)) * speed * (0.35 + random.NextDouble() * 0.65);
            var s = size * (0.5 + random.NextDouble());
            Add(new Particle
            {
                Shape = new Ellipse { Width = s, Height = s, Fill = brush },
                Position = at,
                Velocity = v + new Vector(0, -speed * 0.3),
                Life = life * (0.6 + random.NextDouble() * 0.4),
                Gravity = gravity,
                Size = s,
            });
        }
    }

    public void Ring(Point at, Color color, double radius, double life, double thickness = 6) =>
        Add(new Particle
        {
            Shape = new Ellipse { Stroke = Frozen(color), StrokeThickness = thickness },
            Position = at,
            Life = life,
            RingRadius = radius,
        });

    void Add(Particle p)
    {
        p.Shape.IsHitTestVisible = false;
        canvas.Children.Add(p.Shape);
        _live.Add(p);
        Place(p);
    }

    public void Update(double dt)
    {
        for (var i = _live.Count - 1; i >= 0; i--)
        {
            var p = _live[i];
            p.Age += dt;
            if (p.Age >= p.Life)
            {
                canvas.Children.Remove(p.Shape);
                _live.RemoveAt(i);
                continue;
            }
            p.Velocity.Y += p.Gravity * dt;
            p.Position += p.Velocity * dt;
            Place(p);
        }
    }

    void Place(Particle p)
    {
        var t = p.Age / p.Life;
        if (p.RingRadius > 0)
            p.Size = p.Shape.Width = p.Shape.Height = 2 * p.RingRadius * Ease.OutCubic(t) + 1;
        Canvas.SetLeft(p.Shape, p.Position.X - p.Size / 2);
        Canvas.SetTop(p.Shape, p.Position.Y - p.Size / 2);
        p.Shape.Opacity = 1 - t * t;
    }

    public void Clear()
    {
        foreach (var p in _live) canvas.Children.Remove(p.Shape);
        _live.Clear();
    }

    public static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
