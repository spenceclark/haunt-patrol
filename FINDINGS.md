# Desktop Haunting PoC — Findings

**Date:** 2026-10-17
**Test machine:** Windows 11 (build 26300), two 2560×1440 monitors side by side at 100% scaling (`DISPLAY1` primary at 0,0; `DISPLAY2` at 2560,0)
**Code:** `DesktopHaunting/` (.NET 10, WPF, no packages)

The rest of this document separates what was **verified** on this machine from what was only **designed** and not yet tested.

---

## 1. Working approach

### Window model

Each monitor gets one full-screen WPF window per effect:

| | Encounter overlay | Penalty overlay |
|---|---|---|
| XAML | `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `ShowActivated=False`, `Topmost=True`, `Focusable=False` | same |
| Extended styles added in `SourceInitialized` | `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` | `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`, `WS_EX_TRANSPARENT`, `WS_EX_LAYERED` |
| `WM_MOUSEACTIVATE` hook | returns `MA_NOACTIVATE` | same (harmless) |

- `AllowsTransparency=True` makes WPF create a **per-pixel-alpha layered window** (`WS_EX_LAYERED` + `UpdateLayeredWindow`). Windows hit-tests that kind of window by pixel alpha, so alpha-0 pixels are click-through at the OS level. Everything else depends on this.
- `WS_EX_TOOLWINDOW` keeps the windows out of Alt+Tab. `ShowInTaskbar=False` keeps them off the taskbar.
- `WS_EX_NOACTIVATE`, `ShowActivated=False` and `MA_NOACTIVATE` together keep the overlay from becoming the foreground window when it is shown or clicked.

All of this lives in `Platform/NativeMethods.cs` (`MakeOverlay`, `PlaceOver`).

### Win32 APIs used

| API | Purpose |
|---|---|
| `GetWindowLongPtr` / `SetWindowLongPtr(GWL_EXSTYLE)` | add the extended styles |
| `HwndSource.AddHook` | handle `WM_MOUSEACTIVATE` |
| `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)` | place the window in physical pixels |
| `GetWindowRect` | check the window's actual position, for diagnostics |
| `GetForegroundWindow` | log activation behaviour |
| `MonitorFromPoint` + `GetDpiForMonitor(MDT_EFFECTIVE_DPI)` | get each monitor's DPI |
| `GetThreadDpiAwarenessContext` / `AreDpiAwarenessContextsEqual` | confirm at startup that PerMonitorV2 took effect |

Monitors are enumerated with `System.Windows.Forms.Screen.AllScreens`.

### DPI handling

- `app.manifest` declares `<dpiAwareness>PerMonitorV2, PerMonitor</dpiAwareness>` plus `<dpiAware>true/PM</dpiAware>` as a fallback. At startup the log shows `DPI awareness: PerMonitorV2` (**verified**).
- With PerMonitorV2, `Screen.Bounds` and `WorkingArea` are in physical pixels.
- Windows are placed with `SetWindowPos` in physical pixels: once in `SourceInitialized` (before the window is visible), again right after `Show()`, and again on every `DpiChanged`. The repeat is needed because moving a window to a monitor with a different DPI triggers `WM_DPICHANGED`, and WPF then applies Windows' suggested rectangle, which can differ from ours. We do **not** mark `WM_DPICHANGED` as handled, because WPF needs it to rescale.
- Target placement is computed in physical pixels (box = `ceil(90 × scale)`, 20 px gap, 40 px margin inside the working area). Positions are converted to DIP canvas coordinates using `VisualTreeHelper.GetDpi(window)` **after** the window has been positioned, not the cached monitor DPI.
- Every window logs its actual `GetWindowRect` and appends `EXPECTED [...]` if it doesn't match the monitor bounds. If the window DPI differs from the monitor DPI, a separate line is logged.

### Overlay lifecycle

- `HauntingController` owns the state machine (`Stopped / Idle / Encounter / Penalty`) and three `DispatcherTimer`s: a one-shot schedule timer, a 100 ms countdown timer and a 10 s penalty timer. Elapsed time comes from a `Stopwatch` inside `EncounterSession`.
- `OverlayManager` creates, positions, shows and closes the windows. Overlays only render and report clicks. Game state is in `EncounterSession`.
- Monitors are re-enumerated for each encounter and again for the penalty.
- Success and timeout cannot both fire: `EncounterSession.IsFinished` is set exactly once, and `TryTimeOut()` returns true at most once.
- Every timer tick and click handler goes through `Guard()`, which calls `Abort()` on any exception. `App.DispatcherUnhandledException` also calls `Abort()`. `Abort` closes all overlays and stops all timers.
- `ShutdownMode=OnMainWindowClose`, and `MainWindow.Closed` calls `Controller.Shutdown()`.

---

## 2. Failed approaches

**Nothing failed at the windowing level.** The single per-pixel-alpha window per monitor worked on the first build, so the spec's fallback (five small interactive windows plus separate click-through visuals) was not needed. Some pitfalls were avoided by design without being reproduced (see §3 and §5).

These are the things that actually broke during development:

| What | Symptom | Cause / fix |
|---|---|---|
| Placement fallback on a cramped screen | DEBUG self-check failed: `gap 4/5` (two targets closer than 20 px) | After 500 random attempts, the fallback scanned a coarse grid with steps of box size + gap. Earlier random placements could block every grid cell, and the last-resort placement then overlapped. **Fix:** scan every 8 px. Overlap now happens only if there is genuinely no room. |
| `UseWindowsForms` + PerMonitorV2 in the manifest | Build warning `WFO0003` ("configure via `Application.SetHighDpiMode`") | That warning is for WinForms `Application.Run` apps. This is a WPF app, so the manifest is the right place. Suppressed with `<NoWarn>WFO0003</NoWarn>`. |
| `UseWindowsForms` + `ImplicitUsings` | (anticipated) WPF/WinForms type clashes (`Application`, `Point`, `MessageBox` …) | Removed the implicit `System.Windows.Forms` and `System.Drawing` usings in the csproj and fully qualified `Screen` / `Rectangle` where they are used. |
| Test harness: reading the event log through UI Automation | The first soak run reported "1 encounter" over 420 s | The WPF `ListBox` virtualizes its rows, so UIA only exposes visible rows. This is a test-tool issue, not an app bug. Fixed by collecting log lines as they scroll past. |

---

## 3. Focus behaviour

**Verified:** clicking targets did not change the foreground application.

In a live run where all five targets were clicked by hand:

```text
21:40:25  Foreground HWND before encounter: 0x001B0B50
21:40:29  Foreground HWND after first click: 0x001B0B50
21:40:36  Encounter won with 5.0s remaining
21:40:36  Foreground HWND after completion: 0x001B0B50
```

The handle stayed the same throughout. In a second run (timeout, no clicks) the foreground handle changed from `0x00070608` to `0x00030A54`. That handle matched neither overlay nor penalty window, so the user switched apps during the run; it says nothing either way about the overlay.

Measures that keep focus where it is (all in place; the combination is what was verified, not each piece on its own):

- Show without `Activate()`, with `ShowActivated=False` (WPF then uses `SW_SHOWNA`).
- `WS_EX_NOACTIVATE` on the window, and `MA_NOACTIVATE` from `WM_MOUSEACTIVATE` so the click is still delivered.
- Targets are `Border` + `TextBlock` with `MouseLeftButtonDown`, **not `Button`**. `ButtonBase` calls `Focus()` on mouse down, WPF turns that into `SetFocus(hwnd)`, and that would activate the overlay despite `WS_EX_NOACTIVATE`.
- There are no `Focus()` / `Activate()` / `SetForegroundWindow()` calls anywhere in the overlay code.

Not explicitly tested: typing into the original application immediately after a win. The foreground handle being unchanged suggests it works.

---

## 4. Multi-monitor quirks

- **Coordinate systems:** three coordinate spaces are involved.
  1. Physical screen pixels: `Screen.*`, `SetWindowPos`, `GetWindowRect`, target bounds.
  2. Monitor-relative physical pixels: target position minus `monitor.Bounds` origin.
  3. WPF DIPs inside each window: monitor-relative pixels ÷ that window's DPI scale.

  Keep everything physical until the last step, and convert per window, never globally.
- **DPI awareness is a prerequisite.** Without PerMonitorV2, `Screen.Bounds` on a scaled secondary monitor is virtualized, and every position would be wrong in ways that look random. That is why the startup log line exists.
- **Moving a window across monitors with different DPIs:** a window created on the primary monitor and then moved to another-DPI monitor gets `WM_DPICHANGED` with a suggested rectangle that WPF applies. The fix is to call `SetWindowPos` again (see §1). **This is designed but untested**, because both test monitors are at 96 DPI.
- **Negative origins:** placement maths uses signed rectangles throughout, and the DEBUG self-check covers a monitor at `(-1920, -300)` at 96 DPI next to a 3840×2160 primary at 192 DPI (300 randomized runs). **Not tested on real hardware.**
- **Reserved band on the primary monitor:** the top `100 DIP × scale` of the primary monitor is excluded from placement so targets never sit under the countdown label.
- **Spreading targets:** targets are dealt round-robin across monitors in shuffled order, so with two monitors the split is always 3/2. Verified in logs: targets appeared on both monitors in every run.
- **Display changes during an event:** `SystemEvents.DisplaySettingsChanged` arrives on another thread. It is marshalled with `Dispatcher.BeginInvoke`, then cancels any active encounter or penalty and re-enumerates monitors. Note that this fires on *any* display change, including a scaling change, not just a monitor being unplugged. **Not tested.**

Window positions verified on both monitors in every run, for example:

```text
encounter window on #0 hwnd=0x008407E2 rect=[0,0 2560x1440]
encounter window on #1 hwnd=0x00451438 rect=[2560,0 2560x1440]
```

---

## 5. Input handling

Interactive targets and click-through effects coexist because they live in **different windows with different hit-test rules**:

- **Encounter overlay:** per-pixel alpha decides. Transparent pixels pass clicks through to whatever is underneath. Opaque target pixels receive the click. Once a target is removed, its pixels become alpha 0 and are click-through again. `WS_EX_TRANSPARENT` is deliberately **not** set, because it would make the targets unclickable.
- **Penalty overlay:** `WS_EX_TRANSPARENT` makes the whole window invisible to hit-testing, including the opaque red pixels. WPF's `IsHitTestVisible=False` is also set, but it only affects WPF's internal hit-testing and is **not** what makes the window click-through at the OS level.

**Verified with `WindowFromPoint` probes** from a separate process during a live encounter and penalty:

```text
empty area, monitor 0   (10,700)    -> 0x00020706  not overlay  PASS
empty area, monitor 1   (2570,700)  -> 0x00030A54  not overlay  PASS
target 1..5 centres                 -> overlay HWND             PASS (5/5)
X crossing, monitor 0   (1280,720)  -> 0x00020706  not penalty  PASS
X crossing, monitor 1   (3840,720)  -> 0x00030A54  not penalty  PASS
```

`WindowFromPoint` uses the same hit-testing that routes mouse clicks and, on Windows 10+, mouse-wheel messages ("scroll inactive windows"). So wheel scrolling over transparent areas should work as well. **Wheel scrolling was not tested directly.**

**Known exception:** the countdown label ("HAUNTING! 12.4s remaining") is visible pixels inside the primary encounter window, so it eats clicks in that small strip at the top of the primary monitor. It is marked with a `ponytail:` comment in `EncounterOverlayWindow.xaml`.

No global keyboard or mouse hooks are installed, and gameplay needs no keyboard input.

---

## 6. Recommended architecture

**Keep these:**

1. **One per-pixel-alpha WPF window per monitor for interactive content** (`AllowsTransparency=True`). This is verified to give OS-level click-through on transparent areas while targets stay clickable. No need for the five-small-windows fallback.
2. **A separate `WS_EX_TRANSPARENT` window per monitor for passive effects.** Never put a passive effect in the interactive window, or it will eat clicks.
3. **The non-activation set:** `ShowActivated=False`, `WS_EX_NOACTIVATE`, `MA_NOACTIVATE`, `WS_EX_TOOLWINDOW`, no `Focus()` / `Activate()`, no `Button`-derived controls on overlays.
4. **Physical-pixel placement via `SetWindowPos`, repeated after `Show()` and on `DpiChanged`**, and conversion to DIPs per window using that window's actual DPI.
5. **A single controller on the UI dispatcher** owning state and `DispatcherTimer`s, plus a pure session object for game rules. A `Stopwatch` for elapsed time. Overlays stay dumb: they render and raise click events.
6. **Re-enumerate monitors for every event**, and tear down on `DisplaySettingsChanged`.
7. **Try/catch around every tick and click, and in `DispatcherUnhandledException`, that closes all overlays.** A stuck topmost window is the worst failure mode.
8. **Logging the diagnostics shown here:** DPI awareness at startup, per-monitor DPI, expected vs actual window rectangles, and foreground HWND at key points. These are what made the results above checkable.

**Avoid:**

- WPF `Button` (or anything that calls `Focus()`) on overlay windows.
- `WS_EX_TRANSPARENT` on interactive windows.
- Relying on `IsHitTestVisible=False` for OS-level click-through.
- Opaque or nearly-transparent (alpha 1) full-screen backgrounds. Any non-zero alpha blocks input.
- Setting WPF `Left/Top/Width/Height` for overlay placement across mixed-DPI monitors. Use physical `SetWindowPos`.
- A tray icon, global hooks or `SetForegroundWindow`. None of them are needed.

---

## 7. Outstanding risks

| Risk | Status / what to check |
|---|---|
| **Mixed DPI scaling** (e.g. 100% + 150%) | Untested; both test monitors are 96 DPI. Watch for `window DPI scale … differs from monitor scale` or `EXPECTED [...]` in the log, and check that targets are visually 90 DIP and fully on-screen. |
| **Negative monitor origins on real hardware** | Only covered by the self-check. Arrange a monitor to the left of or above the primary and re-run. |
| **Unplugging a monitor mid-event** | Handler written, not exercised. Also confirm `Screen.AllScreens` returns fresh data straight after the event. |
| **Mouse wheel over transparent areas** | Inferred from `WindowFromPoint`, not tested directly. Test with "Scroll inactive windows" both on and off. |
| **Countdown label eats clicks** | Known and small. If it matters, move the label into its own `WS_EX_TRANSPARENT` window. |
| **Rendering cost of large layered windows** | WPF layered windows push the whole surface through `UpdateLayeredWindow` in software. The primary overlay redraws at 10 Hz for the countdown. Not measured; check CPU usage on 4K or 3+ monitor setups. |
| **Z-order against other topmost windows** | Other `Topmost` windows (Task Manager, video players, some toolbars) may cover targets. Full-screen exclusive games will hide overlays entirely. Not tested. |
| **Elevated (admin) foreground apps** | Clicking through to an elevated window works through normal hit-testing, but interactions with UIPI have not been tested. |
| **Touch and pen input** | Not tested. `WM_POINTER` activation may behave differently from `WM_MOUSEACTIVATE`. |
| **Every display change cancels the event** | Changing scaling or resolution mid-encounter cancels it, which may be more than intended. |
| **Long-running stability** | The 20-encounter soak passed (20 triggered, 20 timeouts, 19 penalties dismissed + 1 cancelled by Stop, 0 errors, never more than one overlay set at a time, nothing left after Stop, Exit ended the process). Successful-click cycles were only exercised by hand, not in a soak, and there was no memory or handle-count check. |
