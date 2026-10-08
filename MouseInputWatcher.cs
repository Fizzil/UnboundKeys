using System.Runtime.InteropServices;

namespace UnboundKeys;

// Watches every physical mouse button/wheel event system-wide via a
// low-level mouse hook (see LowLevelHook — the P/Invoke/lifecycle
// machinery lives there; this class owns only the actual mouse-specific
// logic), and — for whichever of the six remappable buttons (see
// MouseCatalog) currently has a key assigned in MouseMap, for any way of
// pressing it — swallows the real click and hands the down or up to
// MouseGestures, which decides whether that was a single, a double or a
// long press and fires ButtonPressed with that mapping's id, so
// Program.cs can run it through KeyExecutor exactly like a recognized
// voice word.
//
// Left Button is never touched anywhere in this file, deliberately: it isn't
// one of MouseCatalog's six buttons, so it can never be looked up as
// "enabled" and never gets suppressed. That's what keeps a normal click
// always available as an escape hatch, no matter what this class does.
public sealed class MouseInputWatcher : IDisposable
{
    // Fired with the mapping's id (e.g. "middle", or "middle.double" for a
    // double press; see MouseCatalog.IdFor) once a press is decided —
    // never for a way of pressing that has no key assigned.
    public event Action<string>? ButtonPressed;

    private readonly MouseGestures _gestures;

    public MouseInputWatcher()
    {
        _gestures = new MouseGestures(
            id => MouseMap.Enabled.TryGetValue(id, out bool on) && on,
            id => ButtonPressed?.Invoke(id));
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int XBUTTON1 = 0x0001;
    private const int XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly LowLevelHook _hook = new();

    public void Start() => _hook.Start(WH_MOUSE_LL, HookCallback);
    public void Pause() => _hook.Pause();
    public void Resume() => _hook.Resume();

    // Only ever invoked once LowLevelHook has already confirmed nCode >= 0
    // and the watcher isn't paused — no need to check either here.
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

        // Ignore only UnboundKeys's own synthetic clicks (see
        // NativeInput.InjectedByUnboundKeys), not every software-
        // injected event in general — this hook is system-wide, so
        // without this it would catch its own mouse-click output (a
        // word/button mapped to click another *enabled* button could
        // otherwise re-trigger itself, at worst in an infinite loop).
        // Checking dwExtraInfo specifically, rather than the generic
        // LLMHF_INJECTED flag, matters because many gaming mice relay
        // real physical input through vendor driver software that
        // itself injects via this same path — a blanket "ignore
        // anything injected" check would swallow that real input too,
        // not just UnboundKeys's own.
        if (data.dwExtraInfo == NativeInput.InjectedByUnboundKeys)
            return _hook.CallNext(nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        short highWord = (short)(data.mouseData >> 16);

        string? downId = msg switch
        {
            WM_RBUTTONDOWN => "right",
            WM_MBUTTONDOWN => "middle",
            WM_XBUTTONDOWN when highWord == XBUTTON1 => "x1",
            WM_XBUTTONDOWN when highWord == XBUTTON2 => "x2",
            WM_MOUSEWHEEL when highWord > 0 => "wheelup",
            WM_MOUSEWHEEL when highWord < 0 => "wheeldown",
            _ => null,
        };

        // A button with a key on any way of pressing it is taken over
        // whole: its ordinary click can no longer pass through, because
        // which way of pressing this is cannot be known until it is over.
        if (downId != null && MouseMap.AnyGestureEnabled(downId))
        {
            if (MouseCatalog.IsWheel(downId))
                _gestures.Notch(downId);
            else
                _gestures.Down(downId);
            return (IntPtr)1;
        }

        // A suppressed button-down also means its matching button-up
        // needs suppressing — otherwise Windows (and whatever app has
        // focus) sees a button-up with no down before it, which some
        // apps read as a spurious click of their own. Wheel events have
        // no separate up message, so there's nothing to match here for
        // wheelup/wheeldown.
        string? upId = msg switch
        {
            WM_RBUTTONUP => "right",
            WM_MBUTTONUP => "middle",
            WM_XBUTTONUP when highWord == XBUTTON1 => "x1",
            WM_XBUTTONUP when highWord == XBUTTON2 => "x2",
            _ => null,
        };
        if (upId != null && MouseMap.AnyGestureEnabled(upId))
        {
            _gestures.Up(upId);
            return (IntPtr)1;
        }

        return _hook.CallNext(nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _gestures.Dispose();
        _hook.Dispose();
    }
}
