namespace VoicePress;

// Maps the word you say after "press" to a Windows virtual-key code.
public static class KeyMap
{
    // Kept deliberately small — one through ten — since a smaller grammar gives
    // the speech engine fewer similar-sounding options to confuse. All ten are
    // remappable from the dashboard.
    public static readonly string[] RemappableWords =
        { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };

    // The original key for each word (one-nine -> the matching number key, ten
    // -> "0", following the same layout as a game's 1-9-then-0 hotbar) — kept
    // around so the dashboard's full-card reset has something to reset back to.
    public static readonly Dictionary<string, ushort> DefaultWords = BuildDefaultMap();

    // Spoken word -> key it currently presses. Loaded from saved settings (if
    // any) on top of the defaults below.
    public static readonly Dictionary<string, ushort> Words = BuildMap();

    // Spoken word -> how that key gets pressed (tap/repeat/hold, and for how
    // long). Also loaded from saved settings.
    public static readonly Dictionary<string, KeyBehavior> Behaviors = BuildBehaviors();

    private static Dictionary<string, ushort> BuildDefaultMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 9; i++)
            map[RemappableWords[i]] = (ushort)(0x31 + i);
        map["ten"] = 0x30;

        return map;
    }

    private static Dictionary<string, ushort> BuildMap()
    {
        var map = new Dictionary<string, ushort>(DefaultWords, StringComparer.OrdinalIgnoreCase);
        return Settings.LoadKeyMap(map);
    }

    private static Dictionary<string, KeyBehavior> BuildBehaviors()
    {
        var map = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in RemappableWords)
            map[word] = new KeyBehavior();

        return Settings.LoadBehaviors(map);
    }

    // Called by the dashboard when the user picks a new key for a word.
    public static void Rebind(string word, ushort vkCode)
    {
        Words[word] = vkCode;
        Settings.Save(Words, Behaviors);
    }

    // Called by the dashboard when the user changes a word's Repeat/Hold/
    // Infinite checkboxes or its duration.
    public static void SetBehavior(string word, bool repeat, bool hold, double durationSeconds, bool infinite)
    {
        Behaviors[word] = new KeyBehavior
        {
            Repeat = repeat,
            Hold = hold,
            DurationSeconds = durationSeconds,
            Infinite = infinite,
        };
        Settings.Save(Words, Behaviors);
    }

    // Called by the dashboard's card-level reset button: puts a word back to
    // its original key with no repeat/hold/duration set.
    public static void ResetToDefault(string word)
    {
        Words[word] = DefaultWords[word];
        Behaviors[word] = new KeyBehavior();
        Settings.Save(Words, Behaviors);
    }

    // SendInput needs to know which keys are part of the nav cluster / extended
    // set (arrows, insert/delete, page up/down, home/end, the Windows keys) so it
    // can set the right flag — otherwise those keys don't register correctly.
    public static bool IsExtendedKey(ushort vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true, // Page Up, Page Down, End, Home
        0x25 or 0x26 or 0x27 or 0x28 => true, // Left, Up, Right, Down
        0x2D or 0x2E => true,                 // Insert, Delete
        0x5B or 0x5C => true,                 // Left Windows, Right Windows
        _ => false,
    };
}
