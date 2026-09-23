namespace UnboundKeys;

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

    // Which saved profile is currently active — each profile has its own
    // complete key map and set of behaviors. Switching profiles (see
    // SwitchProfile) replaces the contents of Words/Behaviors in place.
    public static string ActiveProfile { get; private set; } = Settings.LoadActiveProfileName();

    // The actual data + Rebind/AddExtraKey/.../SwitchProfile logic — shared
    // with MouseMap/VirtualKeyMap, see RemapStore's own class comment for
    // why. Must be declared after DefaultWords/ActiveProfile above: its
    // constructor arguments (BuildMap() etc.) read both, and static field
    // initializers run in declaration order.
    private static readonly RemapStore _store = new(
        BuildMap(), DefaultWords, BuildExtraWords(), BuildBehaviors(),
        profile => Settings.LoadKeyMap(profile, FreshDefaultWords()),
        profile => Settings.LoadExtraKeys(profile, FreshDefaultExtraWords()),
        profile => Settings.LoadBehaviors(profile, FreshDefaultBehaviors()),
        (words, extraWords, behaviors) => Settings.SaveProfile(ActiveProfile, words, extraWords, behaviors));

    // Spoken word -> key it currently presses. Loaded from the active
    // profile's saved settings (if any) on top of the defaults below.
    public static Dictionary<string, ushort> Words => _store.Words;

    // Spoken word -> up to two additional keys pressed alongside the one in
    // Words, all at once (e.g. binding Ctrl and C alongside the main key
    // makes the word send Ctrl+C as a combo). Empty by default. Loaded from
    // the active profile the same way Words is.
    public static Dictionary<string, List<ushort>> ExtraWords => _store.ExtraWords;

    // Spoken word -> how that key gets pressed (tap/repeat/hold, and for how
    // long). Also loaded from the active profile's saved settings.
    public static Dictionary<string, KeyBehavior> Behaviors => _store.Behaviors;

    private static Dictionary<string, ushort> BuildDefaultMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 9; i++)
            map[RemappableWords[i]] = (ushort)(0x31 + i);
        map["ten"] = 0x30;

        return map;
    }

    private static Dictionary<string, ushort> FreshDefaultWords() =>
        new(DefaultWords, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, KeyBehavior> FreshDefaultBehaviors()
    {
        var map = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in RemappableWords)
            map[word] = new KeyBehavior();
        return map;
    }

    private static Dictionary<string, List<ushort>> FreshDefaultExtraWords()
    {
        var map = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in RemappableWords)
            map[word] = new List<ushort>();
        return map;
    }

    private static Dictionary<string, ushort> BuildMap() =>
        Settings.LoadKeyMap(ActiveProfile, FreshDefaultWords());

    private static Dictionary<string, List<ushort>> BuildExtraWords() =>
        Settings.LoadExtraKeys(ActiveProfile, FreshDefaultExtraWords());

    private static Dictionary<string, KeyBehavior> BuildBehaviors() =>
        Settings.LoadBehaviors(ActiveProfile, FreshDefaultBehaviors());

    // Called by the dashboard when the user picks a new key for a word.
    public static void Rebind(string word, ushort vkCode) => _store.Rebind(word, vkCode);

    // Called by the dashboard's "Add Key" — adds one more key that fires
    // alongside the word's main key, as a combo.
    public static void AddExtraKey(string word, ushort vkCode) => _store.AddExtraKey(word, vkCode);

    // Called by the dashboard when the user picks a different key for an
    // already-added extra key slot — same idea as Rebind, but for one of
    // the extras instead of the main key.
    public static void SetExtraKey(string word, int index, ushort vkCode) => _store.SetExtraKey(word, index, vkCode);

    // Called by the dashboard's "✕" on an extra key row.
    public static void RemoveExtraKey(string word, int index) => _store.RemoveExtraKey(word, index);

    public static List<(ushort Vk, bool Extended)> GetAllKeys(string word) => _store.GetAllKeys(word);

    // Called by the dashboard when the user changes a word's Repeat/Hold/
    // Infinite checkboxes, its duration, or (for a 2+ key word) its
    // per-key repeat intervals.
    public static void SetBehavior(string word, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds) =>
        _store.SetBehavior(word, repeat, hold, durationSeconds, infinite, useCustomRepeatIntervals, repeatKeyIntervalsSeconds);

    // Called by the dashboard's card-level reset button: puts a word back to
    // its original key with no repeat/hold/duration set, and no extra keys.
    public static void ResetToDefault(string word) => _store.ResetToDefault(word);

    // Switches to a different profile: loads its key map and behaviors into
    // the same Words/ExtraWords/Behaviors dictionaries in place (so
    // everything that reads them sees the new profile automatically), and
    // remembers the choice for next launch. Releases anything currently
    // engaged first — an infinite hold from the old profile's word meanings
    // shouldn't carry over into the new profile's context. Only this
    // wrapper (not RemapStore.SwitchProfile itself) calls ReleaseAll —
    // DashboardForm.SwitchToProfile calls this one first, before
    // MouseMap's/VirtualKeyMap's own SwitchProfile, relying on that order
    // to release everything before either of those touch anything.
    public static void SwitchProfile(string profileName)
    {
        KeyExecutor.ReleaseAll();
        _store.SwitchProfile(profileName);
        ActiveProfile = profileName;
        Settings.SetActiveProfile(profileName);
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
