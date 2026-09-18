namespace VoicePress;

// The ten physical number-row keys Physical Press can remap — the
// keyboard equivalent of VPress's ten spoken words. Ids are deliberately
// distinct from every voice word ("one".."ten") and mouse button id
// ("right", "middle", ...), since KeyExecutor tracks engaged/infinite
// state for every source in one shared string-keyed dictionary.
//
// Deliberately just the top-row 1-9/0, not the numpad equivalents — those
// are different virtual-key codes entirely (0x60-0x69), so they're
// already untouched by this, same as every other key on the keyboard.
public static class PhysicalKeyCatalog
{
    public readonly record struct KeyInfo(string Id, string Label, string ShortLabel, ushort PhysicalVk);

    public static readonly KeyInfo[] Keys =
    {
        new("phys1", "Physical Key 1", "1", 0x31),
        new("phys2", "Physical Key 2", "2", 0x32),
        new("phys3", "Physical Key 3", "3", 0x33),
        new("phys4", "Physical Key 4", "4", 0x34),
        new("phys5", "Physical Key 5", "5", 0x35),
        new("phys6", "Physical Key 6", "6", 0x36),
        new("phys7", "Physical Key 7", "7", 0x37),
        new("phys8", "Physical Key 8", "8", 0x38),
        new("phys9", "Physical Key 9", "9", 0x39),
        new("phys0", "Physical Key 0", "0", 0x30),
    };
}
