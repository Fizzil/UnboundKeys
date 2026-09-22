using System.Runtime.InteropServices;

namespace VoicePress;

// A window that never takes window activation, so it can't steal focus
// away from whatever it's anchored to (the dashboard, the overlay icon
// itself, or — for OverlayForm, which inherits this — the game). Not
// sealed: OverlayForm is built on this too, rather than duplicating the
// exact same WS_EX_NOACTIVATE/WM_GETMINMAXINFO handling a second time.
public class NonActivatingForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;

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

    // Windows enforces a minimum trackable window size (well over 100px
    // wide) on every top-level window by default, regardless of how small
    // ClientSize is set to — below that floor, the OS silently widens the
    // real window back up. Same fix as OverlayForm's own (see its
    // comments) for the identical problem: intercepting WM_GETMINMAXINFO
    // and reporting a tiny minimum lets a window built on this class
    // actually be as small as it asks to be — which ThemeColorPopup's
    // 60px-wide swatches specifically depend on, or they silently stretch
    // exactly like OverlayForm's own icon once did.
    protected override void WndProc(ref Message m)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (m.Msg == WM_GETMINMAXINFO)
        {
            var info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO))!;
            info.ptMinTrackSize.X = 1;
            info.ptMinTrackSize.Y = 1;
            Marshal.StructureToPtr(info, m.LParam, true);
        }
        base.WndProc(ref m);
    }

    public NonActivatingForm()
    {
        // A Form built entirely in code (no Designer-generated
        // InitializeComponent) still defaults to AutoScaleMode.Font —
        // without a real design-time font baseline to compare against,
        // that can silently stretch every pixel size this class (and
        // ThemeColorPopup, which sizes its swatches to exactly match the
        // overlay icon) explicitly sets, for reasons that have nothing to
        // do with the monitor's actual DPI. Every size here is already a
        // deliberate, explicit pixel value, so auto-scaling only works
        // against that.
        AutoScaleMode = AutoScaleMode.None;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
}
