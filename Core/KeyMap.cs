namespace UnboundKeys;

// Maps the word you say after "press" to the key it sends.
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
    public static readonly Dictionary<string, Key> DefaultWords = BuildDefaultMap();

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
    public static Dictionary<string, Key> Words => _store.Words;

    // Spoken word -> up to RemapStore.MaxExtraKeys additional keys pressed
    // alongside the one in Words, all at once (e.g. binding Ctrl and C alongside the main key
    // makes the word send Ctrl+C as a combo). Empty by default. Loaded from
    // the active profile the same way Words is.
    public static Dictionary<string, List<Key>> ExtraWords => _store.ExtraWords;

    // Spoken word -> how that key gets pressed (tap/repeat/hold, and for how
    // long). Also loaded from the active profile's saved settings.
    public static Dictionary<string, KeyBehavior> Behaviors => _store.Behaviors;

    private static Dictionary<string, Key> BuildDefaultMap()
    {
        var map = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < 9; i++)
            map[RemappableWords[i]] = KeyNames.Digit(i + 1);
        map["ten"] = Key.Digit0;

        return map;
    }

    private static Dictionary<string, Key> FreshDefaultWords() =>
        new(DefaultWords, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, KeyBehavior> FreshDefaultBehaviors() => RemapStore.FreshBehaviors(RemappableWords);

    private static Dictionary<string, List<Key>> FreshDefaultExtraWords() => RemapStore.FreshExtraKeys(RemappableWords);

    private static Dictionary<string, Key> BuildMap() =>
        Settings.LoadKeyMap(ActiveProfile, FreshDefaultWords());

    private static Dictionary<string, List<Key>> BuildExtraWords() =>
        Settings.LoadExtraKeys(ActiveProfile, FreshDefaultExtraWords());

    private static Dictionary<string, KeyBehavior> BuildBehaviors() =>
        Settings.LoadBehaviors(ActiveProfile, FreshDefaultBehaviors());

    // Called by the dashboard when the user picks a new key for a word.
    public static void Rebind(string word, Key key) => _store.Rebind(word, key);

    // Called by the dashboard's "Add Key" — adds one more key that fires
    // alongside the word's main key, as a combo.
    public static void AddExtraKey(string word, Key key) => _store.AddExtraKey(word, key);

    // Called by the dashboard when the user picks a different key for an
    // already-added extra key slot — same idea as Rebind, but for one of
    // the extras instead of the main key.
    public static void SetExtraKey(string word, int index, Key key) => _store.SetExtraKey(word, index, key);

    // Called by the dashboard's "✕" on an extra key row.
    public static void RemoveExtraKey(string word, int index) => _store.RemoveExtraKey(word, index);

    public static List<Key> GetAllKeys(string word) => _store.GetAllKeys(word);

    // What a word presses and how, safe from any thread (see RemapStore.Gate).
    public static bool TryGet(string word, out List<Key> keys, out KeyBehavior behavior) => _store.TryGet(word, out keys, out behavior);

    // Called by the dashboard when the user changes anything about how a
    // word's key is pressed: its Mode, Infinite, the duration, the pause.
    public static void SetBehavior(string word, KeyBehavior behavior) => _store.SetBehavior(word, behavior);

    // Called by the dashboard's card-level reset button: puts a word back to
    // its original key with no repeat/hold/duration set, and no extra keys.
    public static void ResetToDefault(string word) => _store.ResetToDefault(word);

    // The dashboard's Reset All — every word at once.
    public static void ResetAll() => _store.ResetAllToDefault();

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
}
