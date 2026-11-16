namespace UnboundKeys;

// The full list of keys offered in the dashboard's "choose a key" menu, grouped
// the way an on-screen keyboard would group them.
public static class KeyCatalog
{
    public readonly record struct Entry(string DisplayName, Key Key);

    // Field order matters here: these must be declared above Groups, since
    // static field initializers run top-to-bottom and Groups reads them.
    private static readonly Entry[] Navigation =
    {
        new("Up Arrow", Key.Up),
        new("Down Arrow", Key.Down),
        new("Left Arrow", Key.Left),
        new("Right Arrow", Key.Right),
        new("Home", Key.Home),
        new("End", Key.End),
        new("Page Up", Key.PageUp),
        new("Page Down", Key.PageDown),
        new("Insert", Key.Insert),
        new("Delete", Key.Delete),
    };

    private static readonly Entry[] Modifiers =
    {
        new("Shift", Key.Shift),
        new("Ctrl", Key.Ctrl),
        new("Alt", Key.Alt),
        new("Tab", Key.Tab),
        new("Caps Lock", Key.CapsLock),
        new("Windows", Key.Win),
    };

    // A "key" a word/button sends can be a mouse click instead of a
    // keyboard press: the five buttons are keys like any other (see Key),
    // and the system's key sender is where a click goes out as a click.
    // Wheel Up/Down aren't offered here: a wheel tick is an instantaneous
    // scroll, not a press you can hold down, so Hold wouldn't mean anything
    // for it.
    private static readonly Entry[] MouseButtons =
    {
        new("Left Click", Key.LeftClick),
        new("Right Click", Key.RightClick),
        new("Middle Click", Key.MiddleClick),
        new("Mouse Button 4", Key.Mouse4),
        new("Mouse Button 5", Key.Mouse5),
    };

    // Whether a "key" is one of those mouse buttons.
    public static bool IsMouseButton(Key key) =>
        key is Key.LeftClick or Key.RightClick or Key.MiddleClick or Key.Mouse4 or Key.Mouse5;

    // Named but not listed: see DisplayNameFor.
    private static readonly Entry[] Punctuation =
    {
        new("- (minus)", Key.Minus),
        new("= (equals)", Key.EqualSign),
        new(", (comma)", Key.Comma),
        new(". (period)", Key.Period),
        new("/ (slash)", Key.Slash),
        new("; (semicolon)", Key.Semicolon),
        new("' (quote)", Key.Quote),
        new("[ (open bracket)", Key.OpenBracket),
        new("] (close bracket)", Key.CloseBracket),
        new("\\ (backslash)", Key.Backslash),
        new("` (backtick)", Key.Backtick),
    };

    // The virtual pad's pedals (see PadKeys): pressing one holds the pedal
    // on the pad Steering makes, steering on or off (a held pedal plugs the
    // pad in on its own, see Steering.SetPadPedal).
    private static readonly Entry[] Controller =
    {
        new("Throttle", PadKeys.Throttle),
        new("Brake", PadKeys.Brake),
    };

    private static readonly Entry[] Other =
    {
        new("Space", Key.Space),
        new("Enter", Key.Enter),
        new("Backspace", Key.Backspace),
        new("Escape", Key.Escape),
    };

    public const string ControllerCategory = "Controller";

    public static readonly (string Category, Entry[] Keys)[] Groups =
    {
        ("Letters", Letters()),
        ("Numbers", Numbers()),
        ("Function Keys", FunctionKeys()),
        ("Navigation", Navigation),
        ("Modifiers", Modifiers),
        ("Mouse", MouseButtons),
        (ControllerCategory, Controller),
        ("Other", Other),
    };

    public static string DisplayNameFor(Key key)
    {
        foreach (var (_, keys) in Groups)
            foreach (var entry in keys)
                if (entry.Key == key)
                    return entry.DisplayName;

        // Punctuation is not offered in the picker (Fizzil: the symbols out
        // altogether) but keeps its names, so a mapping saved with one still
        // reads "; (semicolon)" rather than a code.
        foreach (var entry in Punctuation)
            if (entry.Key == key)
                return entry.DisplayName;

        // A key the app has no name for (a number in an old file): shown by
        // its number, as it always was.
        return $"Key 0x{(ushort)key:X2}";
    }

    private static Entry[] Letters()
    {
        var list = new List<Entry>();
        for (char c = 'A'; c <= 'Z'; c++)
            list.Add(new Entry(c.ToString(), KeyNames.Letter(c)));
        return list.ToArray();
    }

    private static Entry[] Numbers()
    {
        var list = new List<Entry>();
        for (int i = 0; i <= 9; i++)
            list.Add(new Entry(i.ToString(), KeyNames.Digit(i)));
        return list.ToArray();
    }

    private static Entry[] FunctionKeys()
    {
        var list = new List<Entry>();
        for (int i = 1; i <= 12; i++)
            list.Add(new Entry($"F{i}", KeyNames.Function(i)));
        return list.ToArray();
    }
}
