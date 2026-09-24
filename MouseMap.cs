namespace UnboundKeys;

// Same idea as KeyMap, but for the six remappable mouse buttons (see
// MouseCatalog) instead of the ten spoken words. The one real difference: a
// word always has some key ("one" presses "1" unless you change it), but a
// mouse button has no natural default — every button starts unmapped, and
// Enabled tracks which ones the user has actually assigned a key to. An
// unmapped button's click passes through to Windows completely untouched
// (see MouseInputWatcher); only an enabled one gets intercepted.
public static class MouseMap
{
    public static readonly string[] ButtonIds = BuildButtonIds();

    // Shares the same active profile as KeyMap — switching profiles swaps
    // both the ten words' and the six buttons' mappings together, since a
    // profile represents one complete setup (e.g. "the isle").
    public static string ActiveProfile => KeyMap.ActiveProfile;

    public static readonly Dictionary<string, bool> Enabled = BuildEnabled();

    // See RemapStore's own class comment. Must be declared after ButtonIds/
    // Enabled above: its constructor arguments (BuildMap() etc., and the
    // save delegate's own closure over Enabled) read both, and static
    // field initializers run in declaration order. DefaultWords is never
    // exposed publicly here (unlike KeyMap/VirtualKeyMap) — an all-zero
    // dict, built fresh just for this constructor call, since no mouse
    // button has a "natural" default the way a word or virtual key does.
    private static readonly RemapStore _store = new(
        BuildMap(), FreshDefaultWords(), BuildExtraWords(), BuildBehaviors(),
        profile => Settings.LoadMouseKeyMap(profile, FreshDefaultWords()),
        profile => Settings.LoadMouseExtraKeys(profile, FreshDefaultExtraWords()),
        profile => Settings.LoadMouseBehaviors(profile, FreshDefaultBehaviors()),
        (words, extraWords, behaviors) => Settings.SaveMouseProfile(ActiveProfile, Enabled, words, extraWords, behaviors));

    public static Dictionary<string, ushort> Words => _store.Words;
    public static Dictionary<string, List<ushort>> ExtraWords => _store.ExtraWords;
    public static Dictionary<string, KeyBehavior> Behaviors => _store.Behaviors;

    private static string[] BuildButtonIds()
    {
        var ids = new string[MouseCatalog.Buttons.Length];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = MouseCatalog.Buttons[i].Id;
        return ids;
    }

    private static Dictionary<string, bool> FreshDefaultEnabled()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ButtonIds)
            map[id] = false;
        return map;
    }

    private static Dictionary<string, ushort> FreshDefaultWords()
    {
        // 0 is never a real key we'd send (SendInput needs a nonzero virtual
        // key code), so it doubles as "nothing picked yet" for a button
        // that's unmapped — harmless since Enabled gates whether it's ever
        // actually used.
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ButtonIds)
            map[id] = 0;
        return map;
    }

    private static Dictionary<string, List<ushort>> FreshDefaultExtraWords()
    {
        var map = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ButtonIds)
            map[id] = new List<ushort>();
        return map;
    }

    private static Dictionary<string, KeyBehavior> FreshDefaultBehaviors()
    {
        var map = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ButtonIds)
            map[id] = new KeyBehavior();
        return map;
    }

    private static Dictionary<string, bool> BuildEnabled() =>
        Settings.LoadMouseEnabled(KeyMap.ActiveProfile, FreshDefaultEnabled());

    private static Dictionary<string, ushort> BuildMap() =>
        Settings.LoadMouseKeyMap(KeyMap.ActiveProfile, FreshDefaultWords());

    private static Dictionary<string, List<ushort>> BuildExtraWords() =>
        Settings.LoadMouseExtraKeys(KeyMap.ActiveProfile, FreshDefaultExtraWords());

    private static Dictionary<string, KeyBehavior> BuildBehaviors() =>
        Settings.LoadMouseBehaviors(KeyMap.ActiveProfile, FreshDefaultBehaviors());

    // Called by the dashboard the first time a key is picked for a button —
    // assigns the key and (if this is the first key ever picked for it)
    // switches it on, so MouseInputWatcher starts intercepting it. Enabled
    // is set before the store's own Rebind (which saves) runs, so the save
    // it triggers picks up the new Enabled value too.
    public static void Rebind(string id, ushort vkCode)
    {
        Enabled[id] = true;
        _store.Rebind(id, vkCode);
    }

    public static void AddExtraKey(string id, ushort vkCode) => _store.AddExtraKey(id, vkCode);
    public static void SetExtraKey(string id, int index, ushort vkCode) => _store.SetExtraKey(id, index, vkCode);
    public static void RemoveExtraKey(string id, int index) => _store.RemoveExtraKey(id, index);
    public static List<(ushort Vk, bool Extended)> GetAllKeys(string id) => _store.GetAllKeys(id);

    public static void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds) =>
        _store.SetBehavior(id, repeat, hold, durationSeconds, infinite, useCustomRepeatIntervals, repeatKeyIntervalsSeconds);

    // Puts a button back to fully unmapped — no key, no extras, default
    // behavior, and (unlike a word's ResetToDefault) switched back off, so
    // its click passes through normally again.
    public static void ResetToDefault(string id)
    {
        _store.ResetToDefault(id);
        Enabled[id] = false;
    }

    // The dashboard's Reset All — every button back to unmapped. Enabled
    // is cleared first so the store's own save (which closes over it)
    // writes the switched-off state too.
    public static void ResetAll()
    {
        foreach (var id in ButtonIds)
            Enabled[id] = false;
        _store.ResetAllToDefault();
    }

    // Called by DashboardForm alongside KeyMap.SwitchProfile — loads the new
    // profile's mouse mappings into these same dictionaries in place.
    public static void SwitchProfile(string profileName)
    {
        var newEnabled = Settings.LoadMouseEnabled(profileName, FreshDefaultEnabled());

        Enabled.Clear();
        foreach (var (id, enabled) in newEnabled)
            Enabled[id] = enabled;

        _store.SwitchProfile(profileName);
    }
}
