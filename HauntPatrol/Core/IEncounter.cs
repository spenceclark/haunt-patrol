using System.Windows;

namespace HauntPatrol.Core;

public enum EncounterResult { Victory, Failure, Cancelled }

/// <summary>
/// A mini-game. It draws through the context and reports its outcome with
/// <see cref="EncounterContext.Resolve"/> (once, at the decisive moment) then
/// <see cref="EncounterContext.Complete"/> (after its outro). It never touches overlay windows.
/// Positions are DIPs on the encounter monitor.
/// </summary>
public interface IEncounter
{
    string Title { get; }
    double Duration { get; }
    void Initialize(EncounterContext context);
    void Start();
    void Update(double deltaTime);
    void OnPointerDown(Point position);
    void OnPointerMove(Point position);
    void OnPointerUp(Point position);
    void End();
}
