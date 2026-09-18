namespace VoicePress;

// Same idea as MouseMap, but for the ten remappable physical number-row
// keys (see PhysicalKeyCatalog) instead of the six mouse buttons. Same
// reasoning too: a physical key has no natural default the way a spoken
// word does ("one" presses "1" unless you change it) — every key starts
// unmapped, and Enabled tracks which ones the user has actually assigned
// a key to. An unmapped key's press passes through to Windows completely
// untouched (see PhysicalKeyWatcher); only an enabled one gets intercepted.
public static class PhysicalKeyMap
{
    public static readonly string[] KeyIds = BuildKeyIds();

    // Shares the same active profile as KeyMap/MouseMap — switching
    // profiles swaps every source's mappings together, since a profile
    // represents one complete setup.
    public static string ActiveProfile => KeyMap.ActiveProfile;

    public static readonly Dictionary<string, bool> Enabled = BuildEnabled();
    public static readonly Dictionary<string, ushort> Words = BuildMap();
    public static readonly Dictionary<string, List<ushort>> ExtraWords = BuildExtraWords();
    public static readonly Dictionary<string, KeyBehavior> Behaviors = BuildBehaviors();

    private static string[] BuildKeyIds()
    {
        var ids = new string[PhysicalKeyCatalog.Keys.Length];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = PhysicalKeyCatalog.Keys[i].Id;
        return ids;
    }

    private static Dictionary<string, bool> FreshDefaultEnabled()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KeyIds)
            map[id] = false;
        return map;
    }

    private static Dictionary<string, ushort> FreshDefaultWords()
    {
        // 0 is never a real key we'd send (SendInput needs a nonzero virtual
        // key code), so it doubles as "nothing picked yet" for a key that's
        // unmapped — harmless since Enabled gates whether it's ever actually used.
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KeyIds)
            map[id] = 0;
        return map;
    }

    private static Dictionary<string, List<ushort>> FreshDefaultExtraWords()
    {
        var map = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KeyIds)
            map[id] = new List<ushort>();
        return map;
    }

    private static Dictionary<string, KeyBehavior> FreshDefaultBehaviors()
    {
        var map = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KeyIds)
            map[id] = new KeyBehavior();
        return map;
    }

    private static Dictionary<string, bool> BuildEnabled() =>
        Settings.LoadPhysicalEnabled(KeyMap.ActiveProfile, FreshDefaultEnabled());

    private static Dictionary<string, ushort> BuildMap() =>
        Settings.LoadPhysicalKeyMap(KeyMap.ActiveProfile, FreshDefaultWords());

    private static Dictionary<string, List<ushort>> BuildExtraWords() =>
        Settings.LoadPhysicalExtraKeys(KeyMap.ActiveProfile, FreshDefaultExtraWords());

    private static Dictionary<string, KeyBehavior> BuildBehaviors() =>
        Settings.LoadPhysicalBehaviors(KeyMap.ActiveProfile, FreshDefaultBehaviors());

    // Called by the dashboard the first time a key is picked for a
    // physical key — assigns it and (if this is the first time) switches
    // it on, so PhysicalKeyWatcher starts intercepting it.
    public static void Rebind(string id, ushort vkCode)
    {
        Words[id] = vkCode;
        Enabled[id] = true;
        Save();
    }

    public static void AddExtraKey(string id, ushort vkCode)
    {
        if (ExtraWords[id].Count >= 2)
            return;

        ExtraWords[id].Add(vkCode);
        Save();
    }

    public static void SetExtraKey(string id, int index, ushort vkCode)
    {
        var extras = ExtraWords[id];
        if (index < 0 || index >= extras.Count)
            return;

        extras[index] = vkCode;
        Save();
    }

    public static void RemoveExtraKey(string id, int index)
    {
        var extras = ExtraWords[id];
        if (index < 0 || index >= extras.Count)
            return;

        extras.RemoveAt(index);
        Save();
    }

    public static List<(ushort Vk, bool Extended)> GetAllKeys(string id)
    {
        var keys = new List<(ushort, bool)> { (Words[id], KeyMap.IsExtendedKey(Words[id])) };
        foreach (var vk in ExtraWords[id])
            keys.Add((vk, KeyMap.IsExtendedKey(vk)));
        return keys;
    }

    public static void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds)
    {
        Behaviors[id] = new KeyBehavior
        {
            Repeat = repeat,
            Hold = hold,
            DurationSeconds = durationSeconds,
            Infinite = infinite,
            UseCustomRepeatIntervals = useCustomRepeatIntervals,
            RepeatKeyIntervalsSeconds = new List<double>(repeatKeyIntervalsSeconds),
        };
        Save();
    }

    // Puts a key back to fully unmapped — no key, no extras, default
    // behavior, and switched back off, so its press passes through normally again.
    public static void ResetToDefault(string id)
    {
        Words[id] = 0;
        Enabled[id] = false;
        ExtraWords[id].Clear();
        Behaviors[id] = new KeyBehavior();
        Save();
    }

    private static void Save() => Settings.SavePhysicalProfile(KeyMap.ActiveProfile, Enabled, Words, ExtraWords, Behaviors);

    // Called by DashboardForm alongside KeyMap.SwitchProfile/MouseMap.SwitchProfile
    // — loads the new profile's physical-key mappings into these same
    // dictionaries in place.
    public static void SwitchProfile(string profileName)
    {
        var newEnabled = Settings.LoadPhysicalEnabled(profileName, FreshDefaultEnabled());
        var newWords = Settings.LoadPhysicalKeyMap(profileName, FreshDefaultWords());
        var newExtraWords = Settings.LoadPhysicalExtraKeys(profileName, FreshDefaultExtraWords());
        var newBehaviors = Settings.LoadPhysicalBehaviors(profileName, FreshDefaultBehaviors());

        Enabled.Clear();
        foreach (var (id, enabled) in newEnabled)
            Enabled[id] = enabled;

        Words.Clear();
        foreach (var (id, vk) in newWords)
            Words[id] = vk;

        ExtraWords.Clear();
        foreach (var (id, extras) in newExtraWords)
            ExtraWords[id] = extras;

        Behaviors.Clear();
        foreach (var (id, behavior) in newBehaviors)
            Behaviors[id] = behavior;
    }
}
