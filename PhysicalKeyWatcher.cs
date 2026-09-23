using System.Runtime.InteropServices;

namespace UnboundKeys;

// Watches the physical keyboard system-wide via a low-level hook (see
// LowLevelHook — the P/Invoke/lifecycle machinery lives there; this class
// owns only the actual keyboard-specific logic), for two unrelated jobs
// that happen to share one hook:
//
// 1. The physical Caps Lock key's rapid double-tap panic button (see
//    StopRequested) — releases everything (voice, mouse, and the virtual
//    keyboard's sticky modifiers) the same way saying "press stop" does,
//    for anyone with a real keyboard. Never suppressed — purely observed.
//
// 2. Mirroring the virtual on-screen keyboard's remappable digits/letters
//    (see VirtualKeyCatalog) onto the real keyboard — but ONLY for a key
//    that's actually been customized there (see VirtualKeyMap.IsCustomized).
//    An untouched letter/digit passes through completely untouched, so
//    normal typing elsewhere is never affected; only the specific keys
//    you've remapped on the on-screen keyboard get intercepted and
//    rerouted through that same remap when pressed for real. Physical
//    Press (remapping the whole number row unconditionally) was removed
//    once the virtual on-screen keyboard made it redundant — this is
//    deliberately narrower than that was.
public sealed class PhysicalKeyWatcher : IDisposable
{
    // Fired on two rapid taps of the physical Caps Lock key. Caps Lock
    // specifically (rather than, say, End) because it's a full-size,
    // never-shifted key on every keyboard — no laptop Fn-chording to
    // fight with — and because it's never suppressed here (this is purely
    // a side-effect trigger, not a remap), an even tap count matters: two
    // real toggles land Caps Lock's own on/off state right back where it
    // started, instead of needing a third press just to undo the side
    // effect the gesture itself caused (which is what an odd count, like
    // three, would leave behind).
    public event Action? StopRequested;

    // Fired with the VirtualKeyCatalog id (e.g. "vr") of a customized
    // digit/letter when its real physical key is pressed — never fired
    // for one still at its own default, since those aren't intercepted at
    // all. Program.cs runs this through KeyExecutor exactly like a
    // recognized voice word, a mapped mouse button, or a click on the
    // virtual keyboard itself.
    public event Action<string>? VirtualKeyPressed;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    // Sent instead of the plain WM_KEYDOWN/UP pair while Alt is held down
    // at the same time — still an ordinary press as far as this watcher
    // cares, so it's treated identically.
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const ushort VK_CAPITAL = 0x14;

    // How close together each of the 2 Caps Lock taps needs to land —
    // reset on every tap (not measured from the first), same idea as the
    // dashboard's own 3-tap Reset All easter egg. A full second rather
    // than that one's tighter 600ms: this is a safety mechanism meant to
    // work reliably under stress, not a fun discoverable extra, so it
    // should err on the side of forgiving.
    private const int PanicTapWindowMs = 1000;
    private int _panicTapCount;
    private DateTime _lastPanicTap = DateTime.MinValue;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly LowLevelHook _hook = new();

    // Whether Caps Lock is currently physically held down — Windows sends
    // a fresh WM_KEYDOWN for every auto-repeat tick while a key stays
    // pressed, so only the very first down of a press should count as a
    // tap; this is what tells that apart from a repeat.
    private bool _capsLockHeld;

    // Digit/letter keys currently physically held down AND being mirrored
    // (i.e. customized — see the class comment). Same auto-repeat problem
    // as Caps Lock above, but for a different reason: without this, a
    // held-down key's repeated WM_KEYDOWNs would each re-fire
    // VirtualKeyPressed, which for something like an Infinite Repeat would
    // just toggle it on and off every ~30ms instead of behaving like one
    // press. Also doubles as "which keys' matching key-up still needs
    // suppressing" — a suppressed down with a leaked-through up would look
    // to Windows (and whatever app has focus) like a spurious keystroke.
    private readonly HashSet<ushort> _heldMirroredKeys = new();

    private static readonly Dictionary<ushort, string> VkToId = BuildVkToId();

    private static Dictionary<ushort, string> BuildVkToId()
    {
        var map = new Dictionary<ushort, string>();
        foreach (var key in VirtualKeyCatalog.Keys)
            map[key.DefaultVk] = key.Id;
        return map;
    }

    public void Start() => _hook.Start(WH_KEYBOARD_LL, HookCallback);
    public void Pause() => _hook.Pause();
    public void Resume() => _hook.Resume();

    // Only ever invoked once LowLevelHook has already confirmed nCode >= 0
    // and the watcher isn't paused — no need to check either here.
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Ignore only UnboundKeys's own synthetic key presses (see
        // NativeInput.InjectedByUnboundKeys) — this hook is system-wide,
        // so without this it would catch its own output. Checking
        // dwExtraInfo specifically, rather than the generic "this was
        // injected" flag, matters because legitimate software
        // (accessibility tools, macro keyboards) can also inject real
        // keyboard input, and a blanket check would swallow that too.
        if (data.dwExtraInfo == NativeInput.InjectedByUnboundKeys)
            return _hook.CallNext(nCode, wParam, lParam);

        int msg = wParam.ToInt32();
        ushort vk = (ushort)data.vkCode;

        // Never suppressed — always falls through to CallNext — this is
        // purely observed, not remapped.
        if (vk == VK_CAPITAL)
        {
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                if (!_capsLockHeld)
                {
                    _capsLockHeld = true;
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
                _capsLockHeld = false;
            }

            return _hook.CallNext(nCode, wParam, lParam);
        }

        if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && VkToId.TryGetValue(vk, out var downId)
            && VirtualKeyMap.IsCustomized(downId))
        {
            if (_heldMirroredKeys.Add(vk))
                VirtualKeyPressed?.Invoke(downId);
            return (IntPtr)1;
        }

        // A suppressed key-down also means its matching key-up needs
        // suppressing too — otherwise Windows (and whatever app has
        // focus) sees a key-up with no matching down before it.
        if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _heldMirroredKeys.Remove(vk))
            return (IntPtr)1;

        return _hook.CallNext(nCode, wParam, lParam);
    }

    public void Dispose() => _hook.Dispose();
}
