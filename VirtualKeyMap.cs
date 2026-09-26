namespace UnboundKeys;

// The virtual keyboard's 34 remappable keys (see VirtualKeyCatalog) — same
// "always mapped to something real" shape as KeyMap, not MouseMap's
// starts-unmapped/Enabled-gated one: every key needs to send a real key
// from the moment the keyboard first opens, so it works as a plain
// on-screen keyboard with zero setup, same reasoning as a spoken word
// always pressing its matching number key by default.
public static class VirtualKeyMap
{
    public static readonly string[] KeyIds = BuildKeyIds();

    public static readonly Dictionary<string, ushort> DefaultWords = BuildDefaultMap();

    // Shares the same active profile as KeyMap/MouseMap — switching
    // profiles swaps every source's mappings together.
    public static string ActiveProfile => KeyMap.ActiveProfile;

    // See RemapStore's own class comment. Must be declared after KeyIds/
    // DefaultWords above: its constructor arguments (BuildMap() etc.) read
    // both, and static field initializers run in declaration order.
    private static readonly RemapStore _store = new(
        BuildMap(), DefaultWords, BuildExtraWords(), BuildBehaviors(),
        profile => Settings.LoadVirtualKeyMap(profile, FreshDefaultWords()),
        profile => Settings.LoadVirtualExtraKeys(profile, FreshDefaultExtraWords()),
        profile => Settings.LoadVirtualBehaviors(profile, FreshDefaultBehaviors()),
        (words, extraWords, behaviors) => Settings.SaveVirtualProfile(ActiveProfile, words, extraWords, behaviors));

    public static Dictionary<string, ushort> Words => _store.Words;
    public static Dictionary<string, List<ushort>> ExtraWords => _store.ExtraWords;
    public static Dictionary<string, KeyBehavior> Behaviors => _store.Behaviors;

    private static string[] BuildKeyIds()
    {
        var ids = new string[VirtualKeyCatalog.Keys.Length];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = VirtualKeyCatalog.Keys[i].Id;
        return ids;
    }

    private static Dictionary<string, ushort> BuildDefaultMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in VirtualKeyCatalog.Keys)
            map[key.Id] = key.DefaultVk;
        return map;
    }

    private static Dictionary<string, ushort> FreshDefaultWords() =>
        new(DefaultWords, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, KeyBehavior> FreshDefaultBehaviors()
    {
        var map = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KeyIds)
            map[id] = new KeyBehavior();
        return map;
    }

    private static Dictionary<string, List<ushort>> FreshDefaultExtraWords()
    {
        var map = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in KeyIds)
            map[id] = new List<ushort>();
        return map;
    }

    private static Dictionary<string, ushort> BuildMap() =>
        Settings.LoadVirtualKeyMap(KeyMap.ActiveProfile, FreshDefaultWords());

    private static Dictionary<string, List<ushort>> BuildExtraWords() =>
        Settings.LoadVirtualExtraKeys(KeyMap.ActiveProfile, FreshDefaultExtraWords());

    private static Dictionary<string, KeyBehavior> BuildBehaviors() =>
        Settings.LoadVirtualBehaviors(KeyMap.ActiveProfile, FreshDefaultBehaviors());

    public static void Rebind(string id, ushort vkCode) => _store.Rebind(id, vkCode);
    public static void AddExtraKey(string id, ushort vkCode) => _store.AddExtraKey(id, vkCode);
    public static void SetExtraKey(string id, int index, ushort vkCode) => _store.SetExtraKey(id, index, vkCode);
    public static void RemoveExtraKey(string id, int index) => _store.RemoveExtraKey(id, index);
    public static List<(ushort Vk, bool Extended)> GetAllKeys(string id) => _store.GetAllKeys(id);

    // Whether this key has been changed from doing nothing special —
    // remapped to a different key, given extra combo keys, or given a
    // non-default Repeat/Hold/Infinite behavior. PhysicalKeyWatcher uses
    // this to decide whether to mirror a real keypress of this key at
    // all: an untouched key passes through completely untouched (no hook
    // overhead, no risk to normal typing), and only a customized one gets
    // intercepted and rerouted through the same remap the on-screen
    // keyboard itself uses.
    public static bool IsCustomized(string id) =>
        Words[id] != DefaultWords[id] ||
        ExtraWords[id].Count > 0 ||
        Behaviors[id] is not { Repeat: false, Hold: false, Infinite: false, Priority: false };

    public static void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds, double repeatGapSeconds, bool priority, double prioritySeconds) =>
        _store.SetBehavior(id, repeat, hold, durationSeconds, infinite, useCustomRepeatIntervals, repeatKeyIntervalsSeconds, repeatGapSeconds, priority, prioritySeconds);

    // Puts a key back to its own natural key, no extras, default behavior.
    public static void ResetToDefault(string id) => _store.ResetToDefault(id);

    // The dashboard's Reset All — every key at once.
    public static void ResetAll() => _store.ResetAllToDefault();

    // Called by DashboardForm alongside KeyMap.SwitchProfile/MouseMap.SwitchProfile
    // — loads the new profile's virtual-keyboard mappings into these same
    // dictionaries in place. No ReleaseAll call here:
    // KeyMap.SwitchProfile (always called first) already releases everything
    // system-wide, sticky modifiers included (see KeyExecutor.ReleaseAll).
    public static void SwitchProfile(string profileName) => _store.SwitchProfile(profileName);
}
