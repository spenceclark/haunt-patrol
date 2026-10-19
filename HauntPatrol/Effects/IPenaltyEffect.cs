using System.Windows;
using System.Windows.Controls;

namespace HauntPatrol.Effects;

/// <summary>One participating monitor: a passive (click-through) canvas, its DIP size and the impact point.</summary>
public sealed record PenaltySurface(Canvas Canvas, Size Size, Point Impact);

/// <summary>A temporary, purely visual, click-through desktop effect. Independent of any encounter.</summary>
public interface IPenaltyEffect
{
    double Duration { get; }
    IReadOnlyList<PenaltySurface> Surfaces { get; }
    void Start();
    void Update(double deltaTime);
    void Stop();
}
