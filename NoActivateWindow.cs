using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UnboundKeys;

// WPF replacement for NonActivatingForm (WinForms) — a window that never
// takes window activation, so it can't steal focus away from whatever
// it's anchored to (the dashboard, the overlay icon itself, or — once
// OverlayForm is ported — the game itself). Not sealed, same reasoning as
// NonActivatingForm: OverlayForm will build on this too rather than
// duplicating the same handling a second time.
//
// WS_EX_NOACTIVATE has no managed WPF property, so it's set on the raw
// HWND once one exists (SourceInitialized is the first point that's
// true) — the exact approach the Phase 0 spike validated against a real
// foreground game before any real UI was built on top of it.
public class NoActivateWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int GCL_STYLE = -26;
    private const int CS_DROPSHADOW = 0x00020000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;

    private double _windowOpacity = 1.0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetClassLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetClassLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    public NoActivateWindow()
    {
        // Covers the "first time this window is shown" case; WS_EX_NOACTIVATE
        // below covers every activation attempt after that (including a
        // direct click on it) — same split of responsibility as
        // NonActivatingForm's ShowWithoutActivation override plus its
        // CreateParams.ExStyle change.
        ShowActivated = false;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);

        // CS_DROPSHADOW — the same native soft-edge shadow Windows already
        // draws around a ToolTip or context menu, applied here so a
        // popup (the mouse-button remap card, in particular) visibly
        // floats above whatever's behind it, Discord-popout style,
        // without needing a per-pixel-alpha AllowsTransparency window
        // (a heavier, more complex route WPF would otherwise require for
        // a custom-drawn shadow). Square shadow around RemapCard's own
        // rounded corners is a known, accepted trade-off of this
        // approach — it's how Windows' own native tooltips look too.
        int classStyle = GetClassLong(hwnd, GCL_STYLE);
        SetClassLong(hwnd, GCL_STYLE, classStyle | CS_DROPSHADOW);

        if (_windowOpacity < 1.0)
            ApplyWindowOpacity(_windowOpacity);

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    // Fade mode's whole-window dimming. Not Window.Opacity — that only
    // works with AllowsTransparency (a per-pixel layered window, which
    // also loses the native drop shadow above); this is the plain
    // WS_EX_LAYERED alpha that WinForms' Form.Opacity uses, and it works
    // on any window. Safe to call before the handle exists: the value is
    // kept and applied in OnSourceInitialized.
    public void ApplyWindowOpacity(double opacity)
    {
        _windowOpacity = Math.Clamp(opacity, 0.0, 1.0);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(_windowOpacity * 255), LWA_ALPHA);
    }

    // Same fix as NonActivatingForm's WinForms version, same reason:
    // Windows enforces a minimum trackable window size well over 100px on
    // every top-level window by default, silently widening anything
    // smaller back up. A future small popup (a color-swatch picker, say)
    // depends on actually being as small as it asks to be.
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            info.ptMinTrackSize.X = 1;
            info.ptMinTrackSize.Y = 1;
            Marshal.StructureToPtr(info, lParam, true);
        }
        return IntPtr.Zero;
    }
}
