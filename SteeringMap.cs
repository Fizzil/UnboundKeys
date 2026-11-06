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
// The race button lives here too (Fizzil: "a default one-button solution:
// drive a car with your mouse"): the id "race" is a mapping that starts as
// Throttle with Hold, Infinite and Steering, and RaceTrigger is the mouse
// button or Keyboard-page key that runs it. Both are app-wide, so the race
// button works from any profile and wins over whatever that button means
// there (see MouseInputWatcher, Program.cs and KeyPresses).
//
// A push has one way of being pressed, so its ids carry no ".double"
// sibling (see Gestures).
public static class SteeringMap
{
    public const string RaceId = "race";

    public static readonly string[] Ids = MouseCatalog.Pushes.Select(p => p.Id).Append(RaceId).ToArray();

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

    // The starting points handed to Settings.Load*: every push unmapped,
    // key 0 (never a real key; Enabled gates whether it is used), no
    // extras, a plain tap. The race mapping is the exception: it is always
    // "mapped" (whether it runs is up to RaceTrigger) and starts as the
    // throttle, held until pressed again, with steering.
    public static Dictionary<string, bool> FreshEnabled()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Ids)
            map[id] = id == RaceId;
        return map;
    }

    public static Dictionary<string, ushort> FreshWords()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in Ids)
            map[id] = id == RaceId ? PadKeys.Throttle : (ushort)0;
        return map;
    }

    public static Dictionary<string, List<ushort>> FreshExtraKeys() => RemapStore.FreshExtraKeys(Ids);

    public static Dictionary<string, KeyBehavior> FreshBehaviors()
    {
        var map = RemapStore.FreshBehaviors(Ids);
        map[RaceId] = new KeyBehavior { Hold = true, Infinite = true, Steering = true };
        return map;
    }

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

    // Back to unmapped, or for the race mapping back to its defaults
    // (Throttle, Hold, Infinite, Steering). Enabled is set first so the
    // store's save writes the final state (see MouseMap.ResetToDefault for
    // the bug the other order caused).
    public static void ResetToDefault(string id)
    {
        Enabled[id] = id == RaceId;
        _store.ResetToDefault(id);
        if (id == RaceId)
        {
            // The store resets a behaviour to a plain tap; the race
            // mapping's default is not one.
            Behaviors[id] = FreshBehaviors()[id];
            _store.Save();
        }
    }

    // Everything back to its start: both pushes unmapped, the race mapping
    // at its defaults. Nothing in the dashboard calls this (Reset profile
    // leaves these alone); it is here for completeness.
    public static void ResetAll()
    {
        foreach (var id in Ids)
            Enabled[id] = id == RaceId;
        _store.ResetAllToDefault();
        Behaviors[RaceId] = FreshBehaviors()[RaceId];
        _store.Save();
    }

    // The race button: the id of a mouse button (MouseCatalog.Buttons) or a
    // Keyboard-page key (VirtualKeyCatalog.Keys), or empty for none.
    public static string RaceTrigger
    {
        get => _raceTrigger;
        set
        {
            _raceTrigger = value ?? "";
            Settings.SaveRaceTrigger(_raceTrigger);
        }
    }

    private static string _raceTrigger = Settings.LoadRaceTrigger();

    // Whether this id is the race button, for the hook and the key presses
    // to ask before they run a profile's mapping.
    public static bool IsRaceTrigger(string id) =>
        _raceTrigger.Length > 0 && string.Equals(_raceTrigger, id, StringComparison.OrdinalIgnoreCase);

    // What the key picker's choice means as a race button: a mouse button's
    // virtual-key code is that button, a letter or digit key is its
    // Keyboard-page key, and anything else is no race button (null). Left
    // Button is never one (it always opens the dashboard) and the wheel
    // does not press; function keys are not Keyboard-page keys.
    public static string? TriggerForVk(ushort vk)
    {
        string? button = vk switch
        {
            0x02 => "right",
            0x04 => "middle",
            0x05 => "x1",
            0x06 => "x2",
            _ => null,
        };
        if (button != null)
            return button;
        foreach (var key in VirtualKeyCatalog.Keys)
            if (key.DefaultVk == vk)
                return key.Id;
        return null;
    }

    // The trigger as the user knows it: "Middle Button", "A key", "Not set".
    public static string TriggerLabel()
    {
        if (_raceTrigger.Length == 0)
            return "Not set";
        foreach (var button in MouseCatalog.Buttons)
            if (button.Id == _raceTrigger)
                return button.Label;
        foreach (var key in VirtualKeyCatalog.Keys)
            if (key.Id == _raceTrigger)
                return key.Label + " key";
        return _raceTrigger;
    }

    // The virtual-key code behind the trigger, for the picker to mark it.
    public static ushort TriggerVk()
    {
        switch (_raceTrigger)
        {
            case "right": return 0x02;
            case "middle": return 0x04;
            case "x1": return 0x05;
            case "x2": return 0x06;
        }
        foreach (var key in VirtualKeyCatalog.Keys)
            if (key.Id == _raceTrigger)
                return key.DefaultVk;
        return 0;
    }
}
