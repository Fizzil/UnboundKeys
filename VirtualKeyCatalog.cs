namespace UnboundKeys;

// The 36 virtual-keyboard keys that can be remapped from the dashboard —
// the ten number-row digits and the 26 letters. Deliberately just these:
// modifiers (Shift/Ctrl/Alt/Win) are excluded since they're sticky
// toggles rather than ordinary keys (see VirtualKeyboardForm), and
// Tab/Space/the arrows are excluded too, keeping the remappable set to
// plain alphanumeric keys only — the set PhysicalKeyWatcher mirrors onto
// the real keyboard for whichever of these you've actually customized.
// Every other key on VirtualKeyboardForm's on-screen layout (Esc,
// punctuation, Backspace, Del, Caps, Enter, Tab, Space, the arrows,
// Ctrl/Shift/Alt/Win, Menu) is a plain, fixed copy of a real key with no
// entry here at all, since it never needs a Key/Repeat/Hold/Infinite
// card and is never physically mirrored.
public static class VirtualKeyCatalog
{
    public readonly record struct KeyInfo(string Id, string Label, ushort DefaultVk);

    public static readonly KeyInfo[] Keys = BuildKeys();

    private static KeyInfo[] BuildKeys()
    {
        var list = new List<KeyInfo>();

        for (int i = 1; i <= 9; i++)
            list.Add(new KeyInfo($"v{i}", i.ToString(), (ushort)(0x30 + i)));
        list.Add(new KeyInfo("v0", "0", 0x30));

        for (char c = 'A'; c <= 'Z'; c++)
            list.Add(new KeyInfo($"v{char.ToLowerInvariant(c)}", c.ToString(), c));

        return list.ToArray();
    }
}
