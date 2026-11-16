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
    public readonly record struct KeySpec(string Label, KeyKind Kind, string? Id, Key Key, double Width, string? ShiftLabel, bool LargeLabel = false);

    private static KeySpec Plain(string label, Key key, double width, string? shiftLabel = null, bool largeLabel = false) =>
        new(label, KeyKind.Plain, null, key, width, shiftLabel, largeLabel);

    private static KeySpec Remap(string id, string label, double width, string? shiftLabel = null) =>
        new(label, KeyKind.Remappable, id, Key.None, width, shiftLabel);

    private static KeySpec StickyFixed(string id, string label, Key key, double width) =>
        new(label, KeyKind.StickyFixed, id, key, width, null);

    private static KeySpec Toggle(string label, double width) =>
        new(label, KeyKind.MiniToggle, null, Key.None, width, null);

    private static KeySpec Fade(double width) =>
        new("Fade", KeyKind.FadeToggle, null, Key.None, width, null);

    private static KeySpec Menu(double width) =>
        new("Menu", KeyKind.Menu, null, Key.None, width, null);

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
    // back (and it tucks the dashboard away again when it is showing, the
    // same as a tray-icon click), and Fade is the one way out of a dimmed
    // screen. Backspace took over the old Mini slot on Row1.
    public static KeySpec FadeKey => Fade(1);
    public static KeySpec MiniKey => Toggle("Mini", 1);
    public static KeySpec MenuKey => Menu(1);

    // Esc, backtick, the number row (remappable), -, =, Backspace (wide —
    // it has Mini's former width as well as its own, so every other key
    // on the row keeps its size).
    private static KeySpec[] Row1() => new[]
    {
        Plain("Esc", Key.Escape, 1.2),
        Plain("`", Key.Backtick, 0.8, "~"),
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
        Plain("-", Key.Minus, 1, "_"),
        Plain("=", Key.EqualSign, 1, "+"),
        Plain("⌫", Key.Backspace, 2.4),
    };

    // Tab, Q–P (remappable), brackets/backslash, Del.
    private static KeySpec[] Row2() => new[]
    {
        Plain("Tab", Key.Tab, 1.5),
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
        Plain("[", Key.OpenBracket, 1, "{"),
        Plain("]", Key.CloseBracket, 1, "}"),
        Plain("\\", Key.Backslash, 1.35, "|"),
        Plain("Del", Key.Delete, 1.65),
    };

    // Caps, A–L (remappable), semicolon/quote, Enter.
    private static KeySpec[] Row3() => new[]
    {
        Plain("Caps", Key.CapsLock, 1.8),
        Remap("va", "a", 1),
        Remap("vs", "s", 1),
        Remap("vd", "d", 1),
        Remap("vf", "f", 1),
        Remap("vg", "g", 1),
        Remap("vh", "h", 1),
        Remap("vj", "j", 1),
        Remap("vk", "k", 1),
        Remap("vl", "l", 1),
        Plain(";", Key.Semicolon, 1, ":", largeLabel: true),
        Plain("'", Key.Quote, 1, "\"", largeLabel: true),
        Plain("Enter", Key.Enter, 2.2),
    };

    // Shift (sticky), Z–M (remappable), comma/period/slash, Shift again —
    // both Shifts share the id "vshift".
    private static KeySpec[] Row4() => new[]
    {
        StickyFixed("vshift", "Shift", Key.Shift, 2.8),
        Remap("vz", "z", 1),
        Remap("vx", "x", 1),
        Remap("vc", "c", 1),
        Remap("vv", "v", 1),
        Remap("vb", "b", 1),
        Remap("vn", "n", 1),
        Remap("vm", "m", 1),
        Plain(",", Key.Comma, 1, "<", largeLabel: true),
        Plain(".", Key.Period, 1, ">"),
        Plain("/", Key.Slash, 1, "?"),
        StickyFixed("vshift", "Shift", Key.Shift, 2.8),
    };

    // Ctrl/Win/Alt (sticky), Space, Alt/Ctrl again, the four arrows.
    private static KeySpec[] Row5() => new[]
    {
        StickyFixed("vctrl", "Ctrl", Key.Ctrl, 1.5),
        StickyFixed("vwin", "Win", Key.Win, 1.5),
        StickyFixed("valt", "Alt", Key.Alt, 1.5),
        Plain("", Key.Space, 5),
        StickyFixed("valt", "Alt", Key.Alt, 1.5),
        StickyFixed("vctrl", "Ctrl", Key.Ctrl, 1.5),
        Plain("←", Key.Left, 1),
        Plain("↓", Key.Down, 1),
        Plain("↑", Key.Up, 1),
        Plain("→", Key.Right, 1),
    };

    // The collapsed strip — none of these are remappable, which is the
    // point of collapsing the letters and digits away. Order is Fizzil's;
    // Fade and Maxi (the last two) match the full layout's own corner.
    public static KeySpec[] MiniRow() => new[]
    {
        Plain("Esc", Key.Escape, 1),
        Plain("Tab", Key.Tab, 1),
        Plain("Caps", Key.CapsLock, 1),
        StickyFixed("vctrl", "Ctrl", Key.Ctrl, 1),
        StickyFixed("vwin", "Win", Key.Win, 1),
        StickyFixed("valt", "Alt", Key.Alt, 1),
        Plain("Space", Key.Space, 1),
        Plain("←", Key.Left, 1),
        Plain("↓", Key.Down, 1),
        Plain("↑", Key.Up, 1),
        Plain("→", Key.Right, 1),
        Plain("Enter", Key.Enter, 1),
        Plain("Del", Key.Delete, 1),
        Plain("⌫", Key.Backspace, 1),
        Menu(1),
        Fade(1),
        Toggle("Maxi", 1),
    };
}
