using System.Runtime.InteropServices;

namespace UnboundKeys;

// The system-wide low-level-hook machinery PhysicalKeyWatcher (keyboard)
// and MouseInputWatcher (mouse) both need — installing/uninstalling the
// hook, keeping the callback delegate alive for as long as native code
// holds a function pointer to it, and the Pause/Resume gate both watchers
// already wanted identically. Neither watcher's own message-specific
// logic lives here: each owns one of these as a field and hands it its
// own HookCallback, which only ever runs while this class has already
// confirmed nCode >= 0 and the hook isn't paused — exactly the guard both
// watchers used to open their own HookCallback with.
//
// Win32's low-level hook callback shape (int, IntPtr, IntPtr) -> IntPtr is
// identical for WH_KEYBOARD_LL and WH_MOUSE_LL, so one delegate type and
// one P/Invoke surface covers both; what actually differs between the two
// watchers — the native struct each marshals out of lParam, and what they
// do with a keydown/mouseclick once they have it — stays entirely in
// their own files.
internal sealed class LowLevelHook : IDisposable
{
    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // The caller's own callback, only ever invoked by _trampoline once
    // it's already confirmed this event is worth looking at — never
    // called directly from outside this class.
    private HookProc? _userProc;

    // Kept as a field (not a local/lambda) so the delegate object stays
    // alive for as long as the hook is installed — otherwise the GC could
    // collect it while native code still holds a function pointer into
    // it, crashing the process.
    private readonly HookProc _trampoline;
    private IntPtr _hookHandle = IntPtr.Zero;

    // While true, every event passes straight through unexamined without
    // ever reaching the caller's own HookCallback — matches
    // VoiceEngine's/PhysicalKeyWatcher's/MouseInputWatcher's own
    // Pause/Resume, so pausing UnboundKeys stops whatever either watcher
    // does with real input.
    private volatile bool _paused;

    public LowLevelHook()
    {
        _trampoline = Trampoline;
    }

    // hookId is WH_KEYBOARD_LL (13) or WH_MOUSE_LL (14) — the caller's own
    // constant, this class doesn't need to know which. Idempotent, same
    // as both watchers' own Start() used to be, so it's safe to call more
    // than once.
    public void Start(int hookId, HookProc proc)
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        _userProc = proc;
        _hookHandle = SetWindowsHookEx(hookId, _trampoline, GetModuleHandle(null), 0);
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    // Per Win32's own contract for a low-level hook: nCode < 0 means pass
    // it on with no further processing at all, not even a peek — so the
    // caller's HookCallback (which does its own struct marshaling) is
    // never invoked in that case, same as it never used to be examined
    // when either watcher's own "if (nCode >= 0 && !_paused)" was false.
    private IntPtr Trampoline(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_paused)
            return _userProc!(nCode, wParam, lParam);

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // What the caller's own HookCallback calls to pass an event through
    // once it's decided not to suppress it — same CallNextHookEx call
    // either watcher used to make directly against its own _hookHandle.
    internal IntPtr CallNext(int nCode, IntPtr wParam, IntPtr lParam) =>
        CallNextHookEx(_hookHandle, nCode, wParam, lParam);

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
