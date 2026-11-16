namespace UnboundKeys;

// The Words/ExtraWords/Behaviors data and Rebind/AddExtraKey/SetExtraKey/
// RemoveExtraKey/GetAllKeys/SetBehavior/ResetToDefault/Save/SwitchProfile
// logic KeyMap, MouseMap, and VirtualKeyMap each used to carry a full,
// separately-maintained copy of. One instance of this backs each of the
// three — they hand it their own dictionaries and their own
// Settings.Load*/Save* calls (as delegates, so this class never needs to
// know which source it's serving), then forward their public methods
// straight through to it. What stays genuinely different per source lives
// in that source's own file instead of here: KeyMap's real DefaultWords
// (every word has one) vs. MouseMap's all-zero one (no button does) vs.
// VirtualKeyMap's real one plus its own IsCustomized query; MouseMap's
// Enabled dict, which nothing else has; and each source's own
// SwitchProfile edges (KeyMap alone releases everything first and updates
// ActiveProfile; MouseMap alone also swaps Enabled).
//
// Same composition idea RemapCardTab.cs's IRemapSource already uses to
// unify the *UI* side of these three (KeyMapSource/MouseMapSource/
// VirtualKeyMapSource, which forward to these very static classes) — this
// is that same pattern one layer deeper, at the data the UI layer reads
// through those adapters.
internal sealed class RemapStore
{
    public Dictionary<string, Key> Words { get; }

    // Never exposed publicly by MouseMap (an all-zero dict — no mouse
    // button has a "natural" key) — only KeyMap/VirtualKeyMap's own
    // public DefaultWords (a real per-id default) point at the same
    // dictionary a caller like ProfilesTab.cs already reads directly.
    public Dictionary<string, Key> DefaultWords { get; }

    public Dictionary<string, List<Key>> ExtraWords { get; }
    public Dictionary<string, KeyBehavior> Behaviors { get; }

    private readonly Func<string, Dictionary<string, Key>> _loadWords;
    private readonly Func<string, Dictionary<string, List<Key>>> _loadExtraWords;
    private readonly Func<string, Dictionary<string, KeyBehavior>> _loadBehaviors;

    // Takes Words/ExtraWords/Behaviors as arguments each time (rather than
    // closing over this store's own properties) so the owning class's
    // save delegate can fold in whatever extra field only it has —
    // MouseMap's closes over its own Enabled dict alongside these three.
    private readonly Action<Dictionary<string, Key>, Dictionary<string, List<Key>>, Dictionary<string, KeyBehavior>> _save;

    // The maps are changed on the UI thread only (the dashboard), but read
    // from others as well: the input hooks' thread deciding whether a
    // button or key is taken over, the microphone's, the timers that tell
    // a single press from a double one. A dictionary read while it is
    // being changed can fail or hang, and a profile switch empties and
    // refills all of them. So: every change to a map, and every read from
    // a thread that is not the UI's, holds this one gate; the dashboard's
    // own reads need not, as nothing changes a map behind its back. Held
    // for the dictionary work alone, never across a save: the hooks'
    // thread must not wait on the disk.
    internal static readonly object Gate = new();

    public RemapStore(
        Dictionary<string, Key> words,
        Dictionary<string, Key> defaultWords,
        Dictionary<string, List<Key>> extraWords,
        Dictionary<string, KeyBehavior> behaviors,
        Func<string, Dictionary<string, Key>> loadWords,
        Func<string, Dictionary<string, List<Key>>> loadExtraWords,
        Func<string, Dictionary<string, KeyBehavior>> loadBehaviors,
        Action<Dictionary<string, Key>, Dictionary<string, List<Key>>, Dictionary<string, KeyBehavior>> save)
    {
        Words = words;
        DefaultWords = defaultWords;
        ExtraWords = extraWords;
        Behaviors = behaviors;
        _loadWords = loadWords;
        _loadExtraWords = loadExtraWords;
        _loadBehaviors = loadBehaviors;
        _save = save;
    }

    // One log line per change to a mapping, saying which, where and to
    // what. Fizzil found a keyboard key on Repeat that nobody had set, more
    // than once; with these (and the shell's "editor opened" line) a stray
    // change has a time and a sequence to be traced by.
    // Nothing is even put together while the logger is off: the line costs
    // a read of the settings file for the sub-profile's name.
    private static void LogChange(string id, string what)
    {
        if (Log.Enabled)
            Log.Info($"mapping {id} ({KeyMap.ActiveProfile} / {Settings.LoadActiveSubProfile(KeyMap.ActiveProfile)}): {what}");
    }

    private static string Describe(KeyBehavior b)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        string mode = b.Repeat ? (b.Rotation ? "Rotation" : "Repeat") : b.Hold ? "Hold" : "Tap";
        string time = b.Infinite ? ", Infinite" : b.DurationSeconds > 0 ? $", {b.DurationSeconds.ToString("0.0", invariant)} s" : "";
        string pause = b.Priority ? $", pause {b.PrioritySeconds.ToString("0.0", invariant)} s" : "";
        return mode + time + pause;
    }

    public void Rebind(string id, Key key)
    {
        lock (Gate)
            Words[id] = key;
        Save();
        LogChange(id, $"key 1 = {KeyCatalog.DisplayNameFor(key)}");
    }

    // How many keys can fire alongside an id's main key — six keys in all
    // (Fizzil's ask; it was two extras). The one number both the store
    // and the editor card check.
    public const int MaxExtraKeys = 5;

    // Capped at MaxExtraKeys; a no-op past that, so it's safe to call
    // speculatively without checking the count first.
    public void AddExtraKey(string id, Key key)
    {
        lock (Gate)
        {
            if (ExtraWords[id].Count >= MaxExtraKeys)
                return;
            ExtraWords[id].Add(key);
        }
        Save();
        LogChange(id, $"key {ExtraWords[id].Count + 1} added = {KeyCatalog.DisplayNameFor(key)}");
    }

    public void SetExtraKey(string id, int index, Key key)
    {
        lock (Gate)
        {
            var extras = ExtraWords[id];
            if (index < 0 || index >= extras.Count)
                return;
            extras[index] = key;
        }
        Save();
        LogChange(id, $"key {index + 2} = {KeyCatalog.DisplayNameFor(key)}");
    }

    public void RemoveExtraKey(string id, int index)
    {
        lock (Gate)
        {
            var extras = ExtraWords[id];
            if (index < 0 || index >= extras.Count)
                return;
            extras.RemoveAt(index);
        }
        Save();
        LogChange(id, $"key {index + 2} removed");
    }

    // The full set of keys an id should press — its main key followed by
    // any extras. Used everywhere an id is actually executed, instead of
    // just looking up Words[id] alone. A list of its own: what is being
    // pressed must not change under the press when the mapping is edited.
    public List<Key> GetAllKeys(string id)
    {
        lock (Gate)
        {
            var keys = new List<Key> { Words[id] };
            keys.AddRange(ExtraWords[id]);
            return keys;
        }
    }

    // What an id presses and how, for whoever is about to run it, from any
    // thread. False for an id the map does not hold at this instant (there
    // is none such, but a thread that finds one drops the press and does
    // not crash).
    public bool TryGet(string id, out List<Key> keys, out KeyBehavior behavior)
    {
        lock (Gate)
        {
            if (Words.TryGetValue(id, out var key) && ExtraWords.TryGetValue(id, out var extras) && Behaviors.TryGetValue(id, out var how))
            {
                keys = new List<Key> { key };
                keys.AddRange(extras);
                behavior = how;
                return true;
            }
        }
        keys = new List<Key>();
        behavior = new KeyBehavior();
        return false;
    }

    // Takes the whole KeyBehavior rather than one argument per field, so a
    // new field is added in KeyBehavior.cs and the editor that sets it, and
    // nowhere in between.
    public void SetBehavior(string id, KeyBehavior behavior)
    {
        lock (Gate)
            Behaviors[id] = behavior;
        Save();
        LogChange(id, Describe(behavior));
    }

    // The empty starting points every source hands to Settings.Load*: no
    // extra keys and a plain tap for each of its ids.
    public static Dictionary<string, List<Key>> FreshExtraKeys(IEnumerable<string> ids)
    {
        var map = new Dictionary<string, List<Key>>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            map[id] = new List<Key>();
        return map;
    }

    public static Dictionary<string, KeyBehavior> FreshBehaviors(IEnumerable<string> ids)
    {
        var map = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
            map[id] = new KeyBehavior();
        return map;
    }

    // Puts an id back to its own DefaultWords entry, no extras, default
    // behavior — for MouseMap this lands on 0 (its DefaultWords is
    // all-zero), which combined with MouseMap.ResetToDefault also
    // switching Enabled back off is exactly "fully unmapped."
    public void ResetToDefault(string id)
    {
        lock (Gate)
        {
            Words[id] = DefaultWords[id];
            ExtraWords[id].Clear();
            Behaviors[id] = new KeyBehavior();
        }
        Save();
        LogChange(id, "reset to default");
    }

    // Every id at once, one save at the end instead of one per id.
    public void ResetAllToDefault()
    {
        lock (Gate)
        {
            foreach (var id in new List<string>(Words.Keys))
            {
                Words[id] = DefaultWords[id];
                ExtraWords[id].Clear();
                Behaviors[id] = new KeyBehavior();
            }
        }
        Save();
        if (Log.Enabled)
            Log.Info($"mappings reset to default: {Words.Count} of them ({KeyMap.ActiveProfile} / {Settings.LoadActiveSubProfile(KeyMap.ActiveProfile)})");
    }

    public void Save() => _save(Words, ExtraWords, Behaviors);

    // Loads a different profile's mappings into these same three
    // dictionaries in place, so everything holding a reference to Words/
    // ExtraWords/Behaviors (read once, at construction, same as the
    // dictionaries themselves always worked) sees the new profile
    // automatically without needing to re-fetch anything.
    public void SwitchProfile(string profileName)
    {
        var newWords = _loadWords(profileName);
        var newExtraWords = _loadExtraWords(profileName);
        var newBehaviors = _loadBehaviors(profileName);

        // Read from the file above, swapped in here in one go.
        lock (Gate)
        {
            Words.Clear();
            foreach (var (id, key) in newWords)
                Words[id] = key;

            ExtraWords.Clear();
            foreach (var (id, extras) in newExtraWords)
                ExtraWords[id] = extras;

            Behaviors.Clear();
            foreach (var (id, behavior) in newBehaviors)
                Behaviors[id] = behavior;
        }
    }
}
