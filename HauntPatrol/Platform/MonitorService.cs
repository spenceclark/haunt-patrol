using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace HauntPatrol.Platform;

/// <summary>A monitor in physical pixels (valid because the process is PerMonitorV2).</summary>
public sealed record MonitorInfo(int Index, Rectangle Bounds, bool IsPrimary);

public static class MonitorService
{
    /// <summary>Re-enumerated for every encounter and penalty, never cached.</summary>
    public static IReadOnlyList<MonitorInfo> Enumerate(bool allMonitors) =>
        Screen.AllScreens
            .Select((s, i) => new MonitorInfo(i, s.Bounds, s.Primary))
            .Where(m => allMonitors || m.IsPrimary)
            .ToList();
}
