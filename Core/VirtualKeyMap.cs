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

    public static readonly Dictionary<string, Key> DefaultWords = BuildDefaultMap();

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

    public static Dictionary<string, Key> Words => _store.Words;
    public static Dictionary<string, List<Key>> ExtraWords => _store.ExtraWords;
    public static Dictionary<string, KeyBehavior> Behaviors => _store.Behaviors;

    // Every mapping id: each key's own ("va") and its double press's
    // ("va.double"; see Gestures).
    private static string[] BuildKeyIds() => Gestures.AllIds(VirtualKeyCatalog.Keys.Select(k => k.Id));

    // A key's own default is its real key; its double press's is 0, "Not
    // Mapped", as a mouse button's is (see IsMapped).
    private static Dictionary<string, Key> BuildDefaultMap()
    {
        var map = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in VirtualKeyCatalog.Keys)
        {
            map[key.Id] = key.DefaultKey;
            map[Gestures.IdFor(key.Id, Gesture.Double)] = Key.None;
        }
        return map;
    }

    private static Dictionary<string, Key> FreshDefaultWords() =>
        new(DefaultWords, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, KeyBehavior> FreshDefaultBehaviors() => RemapStore.FreshBehaviors(KeyIds);

    private static Dictionary<string, List<Key>> FreshDefaultExtraWords() => RemapStore.FreshExtraKeys(KeyIds);

    private static Dictionary<string, Key> BuildMap() =>
        Settings.LoadVirtualKeyMap(KeyMap.ActiveProfile, FreshDefaultWords());

    private static Dictionary<string, List<Key>> BuildExtraWords() =>
        Settings.LoadVirtualExtraKeys(KeyMap.ActiveProfile, FreshDefaultExtraWords());

    private static Dictionary<string, KeyBehavior> BuildBehaviors() =>
        Settings.LoadVirtualBehaviors(KeyMap.ActiveProfile, FreshDefaultBehaviors());

    public static void Rebind(string id, Key key) => _store.Rebind(id, key);
    public static void AddExtraKey(string id, Key key) => _store.AddExtraKey(id, key);
    public static void SetExtraKey(string id, int index, Key key) => _store.SetExtraKey(id, index, key);
    public static void RemoveExtraKey(string id, int index) => _store.RemoveExtraKey(id, index);
    public static List<Key> GetAllKeys(string id) => _store.GetAllKeys(id);

    // Whether this key has been changed from doing nothing special —
    // remapped to a different key, given extra combo keys, or given a
    // non-default Repeat/Hold/Infinite behavior. PhysicalKeyWatcher uses
    // this to decide whether to mirror a real keypress of this key at
    // all: an untouched key passes through completely untouched (no hook
    // overhead, no risk to normal typing), and only a customized one gets
    // intercepted and rerouted through the same remap the on-screen
    // keyboard itself uses.
    //
    // Asked on the input hooks' thread, hence the gate (see
    // RemapStore.Gate); an id the maps do not hold at this instant is
    // simply not customized.
    public static bool IsCustomized(string id)
    {
        lock (RemapStore.Gate)
        {
            return Words.TryGetValue(id, out var key) && DefaultWords.TryGetValue(id, out var natural)
                && ExtraWords.TryGetValue(id, out var extras) && Behaviors.TryGetValue(id, out var behavior)
                && (key != natural || extras.Count > 0 || !behavior.IsPlainTap());
        }
    }

    // Whether an id sends anything: a key's own mapping always does; its
    // double press's only once a key is picked for it (0 is "Not Mapped").
    // What PressGestures asks before it fires an id.
    public static bool IsMapped(string id)
    {
        lock (RemapStore.Gate)
            return Words.TryGetValue(id, out Key key) && key != Key.None;
    }

    // What an id presses and how, safe from any thread.
    public static bool TryGet(string id, out List<Key> keys, out KeyBehavior behavior) => _store.TryGet(id, out keys, out behavior);

    // Whether a key is taken over on the real keyboard (PhysicalKeyWatcher)
    // and lit on the Keyboard page: its own mapping is customized, or its
    // double press's is.
    public static bool IsIntercepted(string keyId)
    {
        lock (RemapStore.Gate)
            return IsCustomized(keyId) || IsCustomized(Gestures.IdFor(keyId, Gesture.Double));
    }

    public static void SetBehavior(string id, KeyBehavior behavior) => _store.SetBehavior(id, behavior);

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
