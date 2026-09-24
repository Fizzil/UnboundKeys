namespace UnboundKeys;

// The on-screen keyboard's layout — every key, its kind, and its width in
// units of a standard key — shared by the keyboard window itself
// (Wpf/VirtualKeyboardWindow) and the dashboard's Keyboard page, which
// draws the same rows as a map of what can be remapped. One definition,
// so the map always matches the keyboard. The widths and their reasoning
// are VirtualKeyboardForm.cs's (WinForms): Fizzil tuned them by eye.
public static class KeyboardLayout
{
    public enum KeyKind { Plain, Remappable, StickyFixed, MiniToggle, FadeToggle, Menu }

    // ShiftLabel is the symbol shown while Shift is sticky-active (1 → !),
    // cosmetic only; LargeLabel bumps the font for punctuation glyphs that
    // read as too small at the normal size.
    public readonly record struct KeySpec(string Label, KeyKind Kind, string? Id, ushort Vk, bool Extended, double Width, string? ShiftLabel, bool LargeLabel = false);

    private static KeySpec Plain(string label, ushort vk, bool extended, double width, string? shiftLabel = null, bool largeLabel = false) =>
        new(label, KeyKind.Plain, null, vk, extended, width, shiftLabel, largeLabel);

    private static KeySpec Remap(string id, string label, double width, string? shiftLabel = null) =>
        new(label, KeyKind.Remappable, id, 0, false, width, shiftLabel);

    private static KeySpec StickyFixed(string id, string label, ushort vk, bool extended, double width) =>
        new(label, KeyKind.StickyFixed, id, vk, extended, width, null);

    private static KeySpec Toggle(string label, double width) =>
        new(label, KeyKind.MiniToggle, null, 0, false, width, null);

    private static KeySpec Fade(double width) =>
        new("Fade", KeyKind.FadeToggle, null, 0, false, width, null);

    private static KeySpec Menu(double width) =>
        new("Menu", KeyKind.Menu, null, 0, false, width, null);

    public static KeySpec[][] FullRows() => new[] { Row1(), Row2(), Row3(), Row4(), Row5() };

    // "Menu", "Fade" and "Mini" are not on any row: they sit in the top-
    // right corner of the word-suggestion strip above Row1, one seventeenth
    // of the width each, the exact cells "Menu", "Fade" and "Maxi" (the
    // last three keys of MiniRow) occupy in the collapsed strip, so
    // toggling between the two layouts never moves any of them out from
    // under the mouse (Fizzil asked for this). They live on the keyboard
    // as well as the dashboard because the keyboard is what is on screen
    // mid-game: with the dashboard hidden, Listening off and the taskbar
    // out of reach, Menu is the one mouse-only way to bring the dashboard
    // back, and Fade the one way out of a dimmed screen. Backspace took
    // over the old Mini slot on Row1.
    public static KeySpec FadeKey => Fade(1);
    public static KeySpec MiniKey => Toggle("Mini", 1);
    public static KeySpec MenuKey => Menu(1);

    // Esc, backtick, the number row (remappable), -, =, Backspace (wide —
    // it has Mini's former width as well as its own, so every other key
    // on the row keeps its size).
    private static KeySpec[] Row1() => new[]
    {
        Plain("Esc", 0x1B, false, 1.2),
        Plain("`", 0xC0, false, 0.8, "~"),
        Remap("v1", "1", 1, "!"),
        Remap("v2", "2", 1, "@"),
        Remap("v3", "3", 1, "#"),
        Remap("v4", "4", 1, "$"),
        Remap("v5", "5", 1, "%"),
        Remap("v6", "6", 1, "^"),
        Remap("v7", "7", 1, "&"),
        Remap("v8", "8", 1, "*"),
        Remap("v9", "9", 1, "("),
        Remap("v0", "0", 1, ")"),
        Plain("-", 0xBD, false, 1, "_"),
        Plain("=", 0xBB, false, 1, "+"),
        Plain("⌫", 0x08, false, 2.4),
    };

    // Tab, Q–P (remappable), brackets/backslash, Del.
    private static KeySpec[] Row2() => new[]
    {
        Plain("Tab", 0x09, false, 1.5),
        Remap("vq", "q", 1),
        Remap("vw", "w", 1),
        Remap("ve", "e", 1),
        Remap("vr", "r", 1),
        Remap("vt", "t", 1),
        Remap("vy", "y", 1),
        Remap("vu", "u", 1),
        Remap("vi", "i", 1),
        Remap("vo", "o", 1),
        Remap("vp", "p", 1),
        Plain("[", 0xDB, false, 1, "{"),
        Plain("]", 0xDD, false, 1, "}"),
        Plain("\\", 0xDC, false, 1.35, "|"),
        Plain("Del", 0x2E, true, 1.65),
    };

    // Caps, A–L (remappable), semicolon/quote, Enter.
    private static KeySpec[] Row3() => new[]
    {
        Plain("Caps", 0x14, false, 1.8),
        Remap("va", "a", 1),
        Remap("vs", "s", 1),
        Remap("vd", "d", 1),
        Remap("vf", "f", 1),
        Remap("vg", "g", 1),
        Remap("vh", "h", 1),
        Remap("vj", "j", 1),
        Remap("vk", "k", 1),
        Remap("vl", "l", 1),
        Plain(";", 0xBA, false, 1, ":", largeLabel: true),
        Plain("'", 0xDE, false, 1, "\"", largeLabel: true),
        Plain("Enter", 0x0D, false, 2.2),
    };

    // Shift (sticky), Z–M (remappable), comma/period/slash, Shift again —
    // both Shifts share the id "vshift".
    private static KeySpec[] Row4() => new[]
    {
        StickyFixed("vshift", "Shift", 0x10, false, 2.8),
        Remap("vz", "z", 1),
        Remap("vx", "x", 1),
        Remap("vc", "c", 1),
        Remap("vv", "v", 1),
        Remap("vb", "b", 1),
        Remap("vn", "n", 1),
        Remap("vm", "m", 1),
        Plain(",", 0xBC, false, 1, "<", largeLabel: true),
        Plain(".", 0xBE, false, 1, ">"),
        Plain("/", 0xBF, false, 1, "?"),
        StickyFixed("vshift", "Shift", 0x10, false, 2.8),
    };

    // Ctrl/Win/Alt (sticky), Space, Alt/Ctrl again, the four arrows.
    private static KeySpec[] Row5() => new[]
    {
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1.5),
        StickyFixed("vwin", "Win", 0x5B, true, 1.5),
        StickyFixed("valt", "Alt", 0x12, false, 1.5),
        Plain("", 0x20, false, 5),
        StickyFixed("valt", "Alt", 0x12, false, 1.5),
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1.5),
        Plain("←", 0x25, true, 1),
        Plain("↓", 0x28, true, 1),
        Plain("↑", 0x26, true, 1),
        Plain("→", 0x27, true, 1),
    };

    // The collapsed strip — none of these are remappable, which is the
    // point of collapsing the letters and digits away. Order is Fizzil's;
    // Fade and Maxi (the last two) match the full layout's own corner.
    public static KeySpec[] MiniRow() => new[]
    {
        Plain("Esc", 0x1B, false, 1),
        Plain("Tab", 0x09, false, 1),
        Plain("Caps", 0x14, false, 1),
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1),
        StickyFixed("vwin", "Win", 0x5B, true, 1),
        StickyFixed("valt", "Alt", 0x12, false, 1),
        Plain("Space", 0x20, false, 1),
        Plain("←", 0x25, true, 1),
        Plain("↓", 0x28, true, 1),
        Plain("↑", 0x26, true, 1),
        Plain("→", 0x27, true, 1),
        Plain("Enter", 0x0D, false, 1),
        Plain("Del", 0x2E, true, 1),
        Plain("⌫", 0x08, false, 1),
        Menu(1),
        Fade(1),
        Toggle("Maxi", 1),
    };
}
