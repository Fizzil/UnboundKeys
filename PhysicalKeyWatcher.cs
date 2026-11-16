namespace UnboundKeys;

// Decides what each real key event means (the events come from the system
// through IInputHooks, see InputHooks.cs), for two unrelated jobs that
// happen to share the one stream of keys:
//
// 1. The rapid double-tap of the physical Caps Lock key (see
//    StopRequested): releases everything (voice, mouse, and the virtual
//    keyboard sticky modifiers) the same way saying "press stop" does,
//    and shows or hides the dashboard like the on-screen Menu key, for
//    anyone with a real keyboard. Never suppressed, purely observed.
//
// 2. Mirroring the virtual on-screen keyboard's remappable digits/letters
//    (see VirtualKeyCatalog) onto the real keyboard — but ONLY for a key
//    that's actually been customized there, or given a double press (see
//    VirtualKeyMap.IsIntercepted).
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
    // all. Program.cs hands it to KeyPresses, which tells a single press
    // from a double one, and runs the mapping through KeyExecutor exactly
    // like a recognized voice word, a mapped mouse button, or a click on
    // the virtual keyboard itself.
    public event Action<string>? VirtualKeyPressed;

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

    // Whether Caps Lock is currently physically held down — the keyboard
    // sends a fresh key-down for every auto-repeat tick while a key stays
    // pressed, so only the very first down of a press should count as a
    // tap; this is what tells that apart from a repeat.
    private bool _capsLockHeld;

    // Digit/letter keys currently physically held down AND being mirrored
    // (i.e. customized — see the class comment). Same auto-repeat problem
    // as Caps Lock above, but for a different reason: without this, a
    // held-down key's repeated key-downs would each re-fire
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

    // One real key event; true to swallow it. Never called for the app's
    // own key presses (IInputHooks leaves those out), so it cannot catch
    // its own output.
    internal bool Handle(KeyEvent e)
    {
        ushort vk = e.Key;

        // Never swallowed: this is purely observed, not remapped.
        if (vk == VK_CAPITAL)
        {
            if (e.Down)
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
            else
            {
                _capsLockHeld = false;
            }

            return false;
        }

        if (e.Down && VkToId.TryGetValue(vk, out var downId) && VirtualKeyMap.IsIntercepted(downId))
        {
            if (_heldMirroredKeys.Add(vk))
                VirtualKeyPressed?.Invoke(downId);
            return true;
        }

        // A swallowed key-down also means its matching key-up needs
        // swallowing too — otherwise the program in front sees a key-up
        // with no matching down before it.
        if (!e.Down && _heldMirroredKeys.Remove(vk))
            return true;

        return false;
    }

    public void Dispose() { }
}
