using System.Runtime.InteropServices;

namespace VoicePress;

// Watches every physical mouse button/wheel event system-wide via a
// low-level mouse hook, and — for whichever of the six remappable buttons
// (see MouseCatalog) currently has a key assigned in MouseMap — swallows the
// real click and fires ButtonPressed instead, so Program.cs can run it
// through KeyExecutor exactly like a recognized voice word.
//
// Left Button is never touched anywhere in this file, deliberately: it isn't
// one of MouseCatalog's six buttons, so it can never be looked up as
// "enabled" and never gets suppressed. That's what keeps a normal click
// always available as an escape hatch, no matter what this class does.
public sealed class MouseInputWatcher : IDisposable
{
    // Fired with the button's MouseCatalog id (e.g. "middle") whenever an
    // enabled button is pressed — never fired for a button with no key
    // assigned, since those aren't intercepted at all.
    public event Action<string>? ButtonPressed;

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

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Kept as a field so the delegate object stays alive for as long as the
    // hook is installed — otherwise the GC could collect it while native
    // code still holds a function pointer into it, crashing the process.
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;

    // While true, every event passes straight through unexamined — matches
    // VoiceEngine's Pause/Resume, so pausing VoicePress (or "press stop")
    // stops mouse remapping too, exactly like it stops voice commands.
    private volatile bool _paused;

    public MouseInputWatcher()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_paused)
        {
            int msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
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

            if (downId != null && MouseMap.Enabled.TryGetValue(downId, out var downEnabled) && downEnabled)
            {
                ButtonPressed?.Invoke(downId);
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
            if (upId != null && MouseMap.Enabled.TryGetValue(upId, out var upEnabled) && upEnabled)
                return (IntPtr)1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
