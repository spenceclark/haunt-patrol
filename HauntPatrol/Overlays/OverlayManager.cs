using HauntPatrol.Platform;
using static HauntPatrol.Platform.NativeMethods;

namespace HauntPatrol.Overlays;

/// <summary>Creates, positions and closes every overlay window. Encounters never touch windows.</summary>
public sealed class OverlayManager(Action<string> log)
{
    readonly List<OverlayWindow> _open = [];

    public int Count => _open.Count;

    public OverlayWindow Open(MonitorInfo monitor, bool passive)
    {
        var w = new OverlayWindow(monitor, passive);
        _open.Add(w);
        w.ShowOverlay();

        GetWindowRect(w.Handle, out var r);
        var b = monitor.Bounds;
        var actual = $"[{r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}]";
        var expected = $"[{b.X},{b.Y} {b.Width}x{b.Height}]";
        log($"{(passive ? "passive" : "interactive")} overlay on #{monitor.Index} hwnd=0x{w.Handle:X8} rect={actual} scale={w.Scale:0.##}"
            + (actual == expected ? "" : $" EXPECTED {expected}"));
        return w;
    }

    public void CloseAll()
    {
        foreach (var w in _open.ToArray())
        {
            try { w.Close(); }
            catch (Exception ex) { log($"close failed: {ex.Message}"); }
        }
        _open.Clear();
    }
}
