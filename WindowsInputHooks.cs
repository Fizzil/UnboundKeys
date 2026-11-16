using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnboundKeys;

// Windows' side of IInputHooks (see InputHooks.cs): a low-level mouse hook
// and a low-level keyboard hook (LowLevelHook holds the machinery), on a
// thread of their own.
//
// Their own thread, because Windows calls a low-level hook on the thread
// that installed it and waits for the answer before the event goes
// anywhere. While that was the dashboard's thread, anything slow there (a
// settings save, a page being built) held up every mouse move and key in
// the whole system, and a hook that answers too slowly is quietly removed
// by Windows, with nothing to say so (the 2026-11-07 review). This thread
// does nothing but answer the hooks.
//
// And a watchdog, because a removed hook means mapped buttons silently
// stop working, and with steering on the band stops hearing the pointer.
// Once a second, on the same thread:
// - an answer that took long enough for Windows to have given up on the
//   hook puts both hooks back;
// - so does the system having seen input for a while that neither hook
//   was shown (both gone, whatever the reason).
// Putting a hook back is cheap and harmless when it was still there.
internal sealed class WindowsInputHooks : IInputHooks
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    // Sent instead of the plain pair while Alt is held: still an ordinary
    // press and release as far as the app cares.
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int XBUTTON1 = 0x0001;
    private const int XBUTTON2 = 0x0002;

    private const uint WM_TIMER = 0x0113;
    private const uint WM_QUIT = 0x0012;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint elapseMs, IntPtr timerProc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool KillTimer(IntPtr hWnd, UIntPtr id);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    // How often the watchdog looks.
    private const uint WatchdogMs = 1000;

    // Windows gives a low-level hook 300 ms to answer by default (the
    // LowLevelHooksTimeout setting) and removes one that does not. An
    // answer slower than this is close enough to have cost the hook.
    internal const int SlowAnswerMs = 200;

    // Input the system saw this long after the last event either hook was
    // shown means the hooks are not being called.
    internal const int UnseenInputMs = 3000;

    // The second check can also be set off by input that is never shown to
    // a hook at all (the sign-in screen), so it puts the hooks back no
    // more often than this.
    internal const int UnseenRetryMs = 10000;

    private readonly LowLevelHook _mouseHook = new();
    private readonly LowLevelHook _keyHook = new();
    private readonly LowLevelHook.HookProc _mouseProc;
    private readonly LowLevelHook.HookProc _keyProc;
    private Func<MouseEvent, bool> _onMouse = _ => false;
    private Func<KeyEvent, bool> _onKey = _ => false;

    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _running = new();

    // Written and read on the hooks' thread only (the callbacks and the
    // watchdog both run there), except Reinstalls, which the harness reads.
    private int _lastSeenTick = Environment.TickCount;
    private int _slowestAnswerMs;
    private int _lastUnseenRetryTick = Environment.TickCount;
    private bool _faultLogged;
    private int _reinstalls;

    // How many times the watchdog has put the hooks back.
    internal int Reinstalls => Volatile.Read(ref _reinstalls);

    public WindowsInputHooks()
    {
        _mouseProc = MouseProc;
        _keyProc = KeyProc;
    }

    // Returns once both hooks are in (or Windows has refused them, which
    // goes in the log).
    public void Start(Func<MouseEvent, bool> onMouse, Func<KeyEvent, bool> onKey)
    {
        if (_thread != null)
            return;
        _onMouse = onMouse;
        _onKey = onKey;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "UnboundKeys input hooks",
            // The whole system's input waits on this thread's answers.
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        _running.Wait();
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        // A thread has no message queue until it first asks for a message;
        // the hooks and the timer need one.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        Install();
        var timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, WatchdogMs, IntPtr.Zero);
        _running.Set();

        // The hooks are called from inside GetMessage, on this thread.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_TIMER)
                Watchdog();
        }

        KillTimer(IntPtr.Zero, timer);
        _mouseHook.Dispose();
        _keyHook.Dispose();
    }

    private void Install()
    {
        _mouseHook.Start(WH_MOUSE_LL, _mouseProc);
        _keyHook.Start(WH_KEYBOARD_LL, _keyProc);
        if (!_mouseHook.IsInstalled || !_keyHook.IsInstalled)
            Log.Error($"input hooks: Windows refused one (mouse {_mouseHook.IsInstalled}, keyboard {_keyHook.IsInstalled})");
        _lastSeenTick = Environment.TickCount;
        _slowestAnswerMs = 0;
    }

    // fault: a slow answer is something wrong in the app and always goes
    // in the log; the other reason can be nothing at all (see
    // UnseenRetryMs) and is only noted while "Log what I do" is on.
    private void Reinstall(string why, bool fault)
    {
        // Out, then in: never two of the same hook at once, which would
        // hand one press to the app twice.
        _mouseHook.Dispose();
        _keyHook.Dispose();
        Install();
        Interlocked.Increment(ref _reinstalls);
        if (fault)
            Log.Error($"input hooks put back: {why}");
        else
            Log.Info($"input hooks put back: {why}");
    }

    private void Watchdog()
    {
        if (_slowestAnswerMs >= SlowAnswerMs)
        {
            Reinstall($"an answer took {_slowestAnswerMs} ms, long enough for Windows to drop a hook", fault: true);
            return;
        }

        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return;
        int now = Environment.TickCount;
        int unseenMs = unchecked((int)info.dwTime - _lastSeenTick);
        if (unseenMs >= UnseenInputMs && unchecked(now - _lastUnseenRetryTick) >= UnseenRetryMs)
        {
            _lastUnseenRetryTick = now;
            Reinstall($"the system saw input {unseenMs} ms after the last event the hooks were shown", fault: false);
        }
    }

    // Each callback: time it, never let an error out (an exception here
    // would take the app down from inside Windows' call), and pass the
    // event on unless the handler swallowed it. The app's own output,
    // marked by NativeInput, is passed on unexamined: only that, not every
    // injected event, since some mice and keyboards deliver real input by
    // injection too.
    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        long started = Stopwatch.GetTimestamp();
        bool swallow = false;
        try
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != NativeInput.InjectedByUnboundKeys)
            {
                var e = ToMouseEvent(wParam.ToInt32(), data);
                if (e != null)
                    swallow = _onMouse(e.Value);
            }
        }
        catch (Exception ex)
        {
            Fault("mouse", ex);
        }
        Answered(started);
        return swallow ? (IntPtr)1 : _mouseHook.CallNext(nCode, wParam, lParam);
    }

    private IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        long started = Stopwatch.GetTimestamp();
        bool swallow = false;
        try
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (data.dwExtraInfo != NativeInput.InjectedByUnboundKeys)
            {
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (down || up)
                    swallow = _onKey(new KeyEvent(WindowsKeys.FromVk(data.vkCode), down));
            }
        }
        catch (Exception ex)
        {
            Fault("keyboard", ex);
        }
        Answered(started);
        return swallow ? (IntPtr)1 : _keyHook.CallNext(nCode, wParam, lParam);
    }

    private static MouseEvent? ToMouseEvent(int msg, MSLLHOOKSTRUCT data)
    {
        if (msg == WM_MOUSEMOVE)
            return new MouseEvent(MouseEventKind.Move, null, data.pt.x, data.pt.y);

        short highWord = (short)(data.mouseData >> 16);
        (MouseEventKind Kind, string? Button) what = msg switch
        {
            WM_RBUTTONDOWN => (MouseEventKind.Down, "right"),
            WM_RBUTTONUP => (MouseEventKind.Up, "right"),
            WM_MBUTTONDOWN => (MouseEventKind.Down, "middle"),
            WM_MBUTTONUP => (MouseEventKind.Up, "middle"),
            WM_XBUTTONDOWN when highWord == XBUTTON1 => (MouseEventKind.Down, "x1"),
            WM_XBUTTONDOWN when highWord == XBUTTON2 => (MouseEventKind.Down, "x2"),
            WM_XBUTTONUP when highWord == XBUTTON1 => (MouseEventKind.Up, "x1"),
            WM_XBUTTONUP when highWord == XBUTTON2 => (MouseEventKind.Up, "x2"),
            WM_MOUSEWHEEL when highWord > 0 => (MouseEventKind.Down, "wheelup"),
            WM_MOUSEWHEEL when highWord < 0 => (MouseEventKind.Down, "wheeldown"),
            // The left button and anything else: not the app's to touch.
            _ => (MouseEventKind.Move, null),
        };
        return what.Button == null ? null : new MouseEvent(what.Kind, what.Button, data.pt.x, data.pt.y);
    }

    private void Answered(long started)
    {
        _lastSeenTick = Environment.TickCount;
        int ms = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (ms > _slowestAnswerMs)
            _slowestAnswerMs = ms;
    }

    // The first error is logged; a fault that repeats on every mouse move
    // must not fill the log.
    private void Fault(string which, Exception ex)
    {
        if (_faultLogged)
            return;
        _faultLogged = true;
        Log.Error($"input hooks: the {which} handler failed (later failures are not logged)", ex);
    }

    public void Dispose()
    {
        var thread = _thread;
        if (thread == null)
            return;
        _thread = null;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(2000);
    }
}
