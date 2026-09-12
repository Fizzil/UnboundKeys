namespace VoicePress;

// The full list of keys offered in the dashboard's "choose a key" menu, grouped
// the way an on-screen keyboard would group them.
public static class KeyCatalog
{
    public readonly record struct Entry(string DisplayName, ushort VkCode);

    // Field order matters here: these must be declared above Groups, since
    // static field initializers run top-to-bottom and Groups reads them.
    private static readonly Entry[] Navigation =
    {
        new("Up Arrow", 0x26),
        new("Down Arrow", 0x28),
        new("Left Arrow", 0x25),
        new("Right Arrow", 0x27),
        new("Home", 0x24),
        new("End", 0x23),
        new("Page Up", 0x21),
        new("Page Down", 0x22),
        new("Insert", 0x2D),
        new("Delete", 0x2E),
    };

    private static readonly Entry[] Modifiers =
    {
        new("Shift", 0x10),
        new("Ctrl", 0x11),
        new("Alt", 0x12),
        new("Tab", 0x09),
        new("Caps Lock", 0x14),
        new("Windows", 0x5B),
    };

    private static readonly Entry[] Punctuation =
    {
        new("- (minus)", 0xBD),
        new("= (equals)", 0xBB),
        new(", (comma)", 0xBC),
        new(". (period)", 0xBE),
        new("/ (slash)", 0xBF),
        new("; (semicolon)", 0xBA),
        new("' (quote)", 0xDE),
        new("[ (open bracket)", 0xDB),
        new("] (close bracket)", 0xDD),
        new("\\ (backslash)", 0xDC),
        new("` (backtick)", 0xC0),
    };

    private static readonly Entry[] Other =
    {
        new("Space", 0x20),
        new("Enter", 0x0D),
        new("Backspace", 0x08),
        new("Escape", 0x1B),
    };

    public static readonly (string Category, Entry[] Keys)[] Groups =
    {
        ("Letters", Letters()),
        ("Numbers", Numbers()),
        ("Function Keys", FunctionKeys()),
        ("Navigation", Navigation),
        ("Modifiers", Modifiers),
        ("Punctuation", Punctuation),
        ("Other", Other),
    };

    public static string DisplayNameFor(ushort vk)
    {
        foreach (var (_, keys) in Groups)
            foreach (var entry in keys)
                if (entry.VkCode == vk)
                    return entry.DisplayName;

        return $"Key 0x{vk:X2}";
    }

    private static Entry[] Letters()
    {
        var list = new List<Entry>();
        for (char c = 'A'; c <= 'Z'; c++)
            list.Add(new Entry(c.ToString(), c));
        return list.ToArray();
    }

    private static Entry[] Numbers()
    {
        var list = new List<Entry>();
        for (int i = 0; i <= 9; i++)
            list.Add(new Entry(i.ToString(), (ushort)(0x30 + i)));
        return list.ToArray();
    }

    private static Entry[] FunctionKeys()
    {
        var list = new List<Entry>();
        for (int i = 1; i <= 12; i++)
            list.Add(new Entry($"F{i}", (ushort)(0x70 + i - 1)));
        return list.ToArray();
    }
}
