namespace UnboundKeys;

// The mappings the Steering page owns: Push up and Push down (see
// MouseCatalog.Pushes and Steering.cs). Built like MouseMap, with the same
// RemapStore underneath and the same "starts unmapped, Enabled says which
// have a key" rule, but with one real difference: nothing here belongs to a
// profile. Fizzil's setup is to stay in an everyday profile, hop into a
// racing game in the browser and say the word, so the keys must already be
// set; they are saved app-wide (Settings.SaveSteeringMap), never reload on a
// profile switch, and Reset profile leaves them alone. Only Reset app, which
// deletes the settings file, clears them. Which word or button starts
// steering (KeyBehavior.Steering) stays per profile, in that profile's map.
//
// A push has one way of being pressed, so its ids carry no ".double"
// sibling (see Gestures).
public static class SteeringMap
{
    public static readonly string[] Ids = MouseCatalog.Pushes.Select(p => p.Id).ToArray();

    public static readonly Dictionary<string, bool> Enabled = Settings.LoadSteeringEnabled(FreshEnabled());

    // Declared after Ids and Enabled: the constructor arguments and the save
    // delegate's closure read both, and static initializers run in order.
    // The load delegates ignore their profile argument — there is only one
    // set — and are only used by RemapStore.SwitchProfile, which nothing
    // calls here.
    private static readonly RemapStore _store = new(
        Settings.LoadSteeringKeyMap(FreshWords()), FreshWords(),
        Settings.LoadSteeringExtraKeys(FreshExtraKeys()), Settings.LoadSteeringBehaviors(FreshBehaviors()),
        _ => Settings.LoadSteeringKeyMap(FreshWords()),
        _ => Settings.LoadSteeringExtraKeys(FreshExtraKeys()),
        _ => Settings.LoadSteeringBehaviors(FreshBehaviors()),
        (words, extraWords, behaviors) => Settings.SaveSteeringMap(Enabled, words, extraWords, behaviors));

    public static Dictionary<string, ushort> Words => _store.Words;
    public static Dictionary<string, List<ushort>> ExtraWords => _store.ExtraWords;
    public static Dictionary<string, KeyBehavior> Behaviors => _store.Behaviors;

    // The empty starting points handed to Settings.Load*: every push
    // unmapped, key 0 (never a real key; Enabled gates whether it is used),
    // no extras, a plain tap.
    public static Dictionary<string, bool> FreshEnabled()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Ids)
            map[id] = false;
        return map;
    }

    public static Dictionary<string, ushort> FreshWords()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Ids)
            map[id] = 0;
        return map;
    }

    public static Dictionary<string, List<ushort>> FreshExtraKeys() => RemapStore.FreshExtraKeys(Ids);

    public static Dictionary<string, KeyBehavior> FreshBehaviors() => RemapStore.FreshBehaviors(Ids);

    // As MouseMap.Rebind: the first key picked switches the push on, and
    // Enabled is set before the store's save so the save writes it.
    public static void Rebind(string id, ushort vkCode)
    {
        Enabled[id] = true;
        _store.Rebind(id, vkCode);
    }

    public static void AddExtraKey(string id, ushort vkCode) => _store.AddExtraKey(id, vkCode);
    public static void SetExtraKey(string id, int index, ushort vkCode) => _store.SetExtraKey(id, index, vkCode);
    public static void RemoveExtraKey(string id, int index) => _store.RemoveExtraKey(id, index);
    public static List<(ushort Vk, bool Extended)> GetAllKeys(string id) => _store.GetAllKeys(id);

    public static void SetBehavior(string id, KeyBehavior behavior) => _store.SetBehavior(id, behavior);

    // Back to unmapped. Enabled is cleared first so the store's save writes
    // the switched-off state (see MouseMap.ResetToDefault for the bug the
    // other order caused).
    public static void ResetToDefault(string id)
    {
        Enabled[id] = false;
        _store.ResetToDefault(id);
    }

    // Both pushes back to unmapped. Nothing in the dashboard calls this
    // (Reset profile leaves the pushes alone); it is here for completeness.
    public static void ResetAll()
    {
        foreach (var id in Ids)
            Enabled[id] = false;
        _store.ResetAllToDefault();
    }
}
