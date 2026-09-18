using System.Runtime.InteropServices;

namespace VoicePress;

// Watches every physical keystroke system-wide via a low-level keyboard
// hook, and — for whichever of the ten remappable number-row keys (see
// PhysicalKeyCatalog) currently has a key assigned in PhysicalKeyMap —
// swallows the real press and fires KeyPressed instead, so Program.cs can
// run it through KeyExecutor exactly like a recognized voice word or a
// mapped mouse button. That only ever happens while Physical is the
// active Press source (see PressMode) — while Voice is active instead,
// every number-row key passes through untouched, full stop, regardless of
// what's actually saved for it.
//
// The one exception is the physical Caps Lock key, watched unconditionally
// (see StopRequested) for a rapid triple-tap panic button — but even that
// is never suppressed, only observed, so it's not really a remap either.
//
// Every other key on the keyboard (letters, function keys, the numpad —
// note the numpad's 0-9 are entirely different virtual-key codes from the
// number row, so they were never in scope here) is never touched anywhere
// in this file, deliberately: nothing but the ten number-row keys is ever
// looked up as "enabled", so nothing else can ever be suppressed.
public sealed class PhysicalKeyWatcher : IDisposable
{
    // Fired with the key's PhysicalKeyCatalog id (e.g. "phys1") whenever
    // an enabled key is pressed — never fired for a key with no key
    // assigned, since those aren't intercepted at all.
    public event Action<string>? KeyPressed;

    // Fired on two rapid taps of the physical Caps Lock key — a dedicated
    // panic button, mainly for an able-bodied friend using Physical Press:
    // releases everything the same way saying "press stop" does, works no
    // matter which Press source is active (unlike the number row, which
    // only remaps while Physical is active). Caps Lock specifically
    // (rather than, say, End) because it's a full-size, never-shifted key
    // on every keyboard — no laptop Fn-chording to fight with — and
    // because it's never suppressed here (this is purely a side-effect
    // trigger, not a remap), an even tap count matters: two real toggles
    // land Caps Lock's own on/off state right back where it started,
    // instead of needing a third press just to undo the side effect the
    // gesture itself caused (which is what an odd count, like three,
    // would leave behind).
    public event Action? StopRequested;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    // Sent instead of the plain WM_KEYDOWN/UP pair while Alt is held down
    // at the same time — still a completely ordinary number-row press as
    // far as this watcher cares, so it's treated identically.
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const ushort VK_CAPITAL = 0x14;

    // How close together each of the 2 Caps Lock taps needs to land — reset
    // on every tap (not measured from the first), same idea as the
    // dashboard's own 3-tap Reset All easter egg. A full second rather than
    // that one's tighter 600ms: this is a safety mechanism meant to work
    // reliably under stress, not a fun discoverable extra, so it should
    // err on the side of forgiving.
    private const int PanicTapWindowMs = 1000;
    private int _panicTapCount;
    private DateTime _lastPanicTap = DateTime.MinValue;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookHandle = IntPtr.Zero;

    // While true, every event passes straight through unexamined — matches
    // VoiceEngine's and MouseInputWatcher's Pause/Resume, so pausing
    // VoicePress (or "press stop") stops physical-key remapping too.
    private volatile bool _paused;

    // Which monitored keys are currently physically held down — the
    // remappable number row, and (see StopRequested above) Caps Lock.
    // Windows sends a fresh WM_KEYDOWN for every auto-repeat tick while a
    // key stays pressed (unlike a mouse click, which is a single discrete
    // event) — for the number row, every one of those still needs
    // suppressing, or the physical key would leak through the moment it
    // started repeating; for Caps Lock, nothing needs suppressing, but
    // this still keeps one held-down press from counting as several taps.
    // Either way, only the very first down of a press should actually
    // count as a tap — this is what tells that apart from a repeat.
    private readonly HashSet<ushort> _heldKeys = new();

    private static readonly Dictionary<ushort, string> VkToId = BuildVkToId();

    private static Dictionary<ushort, string> BuildVkToId()
    {
        var map = new Dictionary<ushort, string>();
        foreach (var key in PhysicalKeyCatalog.Keys)
            map[key.PhysicalVk] = key.Id;
        return map;
    }

    public PhysicalKeyWatcher()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
            return;

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_paused)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Ignore only VoicePress's own synthetic key presses (see
            // NativeInput.InjectedByVoicePress) — this hook is system-wide,
            // so without this it would catch its own output and could
            // re-trigger itself. Checking dwExtraInfo specifically, rather
            // than the generic "this was injected" flag, matters for the
            // same reason it did for the mouse hook: legitimate software
            // (accessibility tools, macro keyboards) can also inject real
            // keyboard input, and a blanket check would swallow that too.
            if (data.dwExtraInfo == NativeInput.InjectedByVoicePress)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            ushort vk = (ushort)data.vkCode;

            // Checked before the PressMode gate below, and never
            // suppressed (always falls through to CallNextHookEx) — the
            // panic button works no matter which Press source is active,
            // unlike the number-row remapping just below it.
            if (vk == VK_CAPITAL)
            {
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    if (_heldKeys.Add(vk))
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastPanicTap).TotalMilliseconds > PanicTapWindowMs)
                            _panicTapCount = 0;
                        _panicTapCount++;
                        _lastPanicTap = now;

                        if (_panicTapCount >= 2)
                        {
                            _panicTapCount = 0;
                            StopRequested?.Invoke();
                        }
                    }
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    _heldKeys.Remove(vk);
                }
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            // Voice and Physical are mutually exclusive (see PressMode) —
            // while Voice is the active source, every physical number key
            // passes straight through untouched, exactly as if none were
            // mapped at all, regardless of what's actually saved for them.
            if (PressMode.Active != "physical")
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && VkToId.TryGetValue(vk, out var downId)
                && PhysicalKeyMap.Enabled.TryGetValue(downId, out var downEnabled) && downEnabled)
            {
                if (_heldKeys.Add(vk))
                    KeyPressed?.Invoke(downId);
                return (IntPtr)1;
            }

            // A suppressed key-down also means every one of its auto-repeat
            // downs, and its eventual key-up, need suppressing too —
            // otherwise Windows (and whatever app has focus) sees a key-up
            // with no matching down before it, or the physical key leaks
            // through the moment it starts auto-repeating.
            if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && VkToId.TryGetValue(vk, out var upId)
                && PhysicalKeyMap.Enabled.TryGetValue(upId, out var upEnabled) && upEnabled)
            {
                _heldKeys.Remove(vk);
                return (IntPtr)1;
            }
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
