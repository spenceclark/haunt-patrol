using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HauntPatrol.Platform;
using static HauntPatrol.Platform.NativeMethods;

namespace HauntPatrol.Overlays;

/// <summary>
/// A borderless, topmost, non-activating, per-pixel-alpha window covering one monitor.
/// Interactive: Windows hit-tests by pixel alpha, so transparent areas click through and visible sprites get the mouse.
/// Passive: WS_EX_TRANSPARENT makes the whole window click-through (effects, particles, HUD).
/// No Focus()/Activate() and no Button-derived content, or the overlay would steal focus.
/// </summary>
public sealed class OverlayWindow : Window
{
    public MonitorInfo Monitor { get; }
    public bool IsPassive { get; }
    public Canvas Canvas { get; } = new() { ClipToBounds = true };
    public IntPtr Handle { get; private set; }

    /// <summary>DIP coordinates on <see cref="Canvas"/>. Move/Up only fire during a drag that began on a visible object.</summary>
    public event Action<Point>? PointerDown, PointerMove, PointerUp;
    bool _dragging;

    public OverlayWindow(MonitorInfo monitor, bool passive)
    {
        Monitor = monitor;
        IsPassive = passive;
        Title = passive ? "Haunt Patrol effects" : "Haunt Patrol encounter";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = Canvas;

        if (passive)
            Canvas.IsHitTestVisible = false;
        else
            HookPointer();

        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            var ex = (long)GetWindowLongPtr(Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            if (passive) ex |= WS_EX_TRANSPARENT;
            SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(Handle)!.AddHook(WndProc);
            Place();
        };
        // WPF applies Windows' suggested rect on a DPI change; put ours back (not marked handled, WPF must rescale).
        DpiChanged += (_, _) => Place();
    }

    public void ShowOverlay()
    {
        Show();
        Place();
    }

    public double Scale => VisualTreeHelper.GetDpi(this).DpiScaleX;

    /// <summary>Monitor size in this window's DIPs, using the window's actual DPI after placement.</summary>
    public Size SizeInDips => new(Monitor.Bounds.Width / Scale, Monitor.Bounds.Height / Scale);

    void Place()
    {
        var b = Monitor.Bounds;
        SetWindowPos(Handle, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE);
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOUSEACTIVATE) return IntPtr.Zero;
        handled = true;
        return new IntPtr(MA_NOACTIVATE); // click is still delivered, window is not activated
    }

    // Drag input: press lands on a visible sprite (alpha > 0), then mouse capture keeps
    // moves/up flowing while the cursor crosses transparent pixels or leaves the monitor.
    void HookPointer()
    {
        Canvas.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _dragging = Canvas.CaptureMouse();
            PointerDown?.Invoke(e.GetPosition(Canvas));
        };
        Canvas.MouseMove += (_, e) =>
        {
            if (_dragging) PointerMove?.Invoke(e.GetPosition(Canvas));
        };
        Canvas.MouseLeftButtonUp += (_, e) => EndDrag(e.GetPosition(Canvas));
        Canvas.LostMouseCapture += (_, _) => EndDrag(Mouse.GetPosition(Canvas));
    }

    void EndDrag(Point p)
    {
        if (!_dragging) return;
        _dragging = false;
        if (Canvas.IsMouseCaptured) Canvas.ReleaseMouseCapture();
        PointerUp?.Invoke(p);
    }
}
