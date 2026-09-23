namespace UnboundKeys;

// Shift/Ctrl/Alt/Win on the virtual keyboard behave as sticky toggles
// rather than the tap-or-hold behavior every other remappable key uses: a
// mouse can't hold one button down while clicking a second one, so there's
// no way to "hold Shift while clicking A" otherwise. One click holds the
// real key down (via a raw NativeInput.KeyDown — this deliberately
// bypasses KeyExecutor's tap/repeat/hold/infinite machinery entirely,
// since none of that means anything for "stay down until the next key");
// the next virtual key click — sticky or not — releases every currently-
// active one at once, so combos like Ctrl+Shift+Esc work by clicking each
// modifier in turn, then the key. Clicking an already-active modifier
// again cancels it with nothing sent.
public static class StickyModifiers
{
    private static readonly Dictionary<string, List<(ushort Vk, bool Extended)>> _active = new();
    private static readonly object _lock = new();

    // Lets VirtualKeyboardForm's sticky buttons refresh their own
    // highlight, including when something else (the panic button) releases
    // them out from under it.
    public static event Action? Changed;

    public static bool IsActive(string id)
    {
        lock (_lock)
            return _active.ContainsKey(id);
    }

    // Called by a sticky modifier's own button.
    public static void Toggle(string id, List<(ushort Vk, bool Extended)> keys)
    {
        lock (_lock)
        {
            if (_active.Remove(id, out var existingKeys))
            {
                foreach (var key in existingKeys)
                    NativeInput.KeyUp(key.Vk, key.Extended);
            }
            else
            {
                foreach (var key in keys)
                    NativeInput.KeyDown(key.Vk, key.Extended);
                _active[id] = keys;
            }
        }
        Changed?.Invoke();
    }

    // Called right after any other virtual key fires — releases every
    // active modifier at once, so a combo is exactly one shot: click the
    // modifier(s), click the key, everything lets go together. A no-op if
    // nothing's currently active.
    public static void ConsumeForKeyPress()
    {
        List<List<(ushort Vk, bool Extended)>> toRelease;
        lock (_lock)
        {
            if (_active.Count == 0)
                return;
            toRelease = new List<List<(ushort, bool)>>(_active.Values);
            _active.Clear();
        }

        foreach (var keys in toRelease)
            foreach (var key in keys)
                NativeInput.KeyUp(key.Vk, key.Extended);
        Changed?.Invoke();
    }

    // Same idea as KeyExecutor.ReleaseAll — called by it, so "press stop",
    // the triple-Caps-Lock panic button, and app exit also let go of any
    // sticky modifier the on-screen keyboard left held down.
    public static void ReleaseAll() => ConsumeForKeyPress();
}
