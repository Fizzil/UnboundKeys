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
    public Dictionary<string, ushort> Words { get; }

    // Never exposed publicly by MouseMap (an all-zero dict — no mouse
    // button has a "natural" key) — only KeyMap/VirtualKeyMap's own
    // public DefaultWords (a real per-id default) point at the same
    // dictionary a caller like ProfilesTab.cs already reads directly.
    public Dictionary<string, ushort> DefaultWords { get; }

    public Dictionary<string, List<ushort>> ExtraWords { get; }
    public Dictionary<string, KeyBehavior> Behaviors { get; }

    private readonly Func<string, Dictionary<string, ushort>> _loadWords;
    private readonly Func<string, Dictionary<string, List<ushort>>> _loadExtraWords;
    private readonly Func<string, Dictionary<string, KeyBehavior>> _loadBehaviors;

    // Takes Words/ExtraWords/Behaviors as arguments each time (rather than
    // closing over this store's own properties) so the owning class's
    // save delegate can fold in whatever extra field only it has —
    // MouseMap's closes over its own Enabled dict alongside these three.
    private readonly Action<Dictionary<string, ushort>, Dictionary<string, List<ushort>>, Dictionary<string, KeyBehavior>> _save;

    public RemapStore(
        Dictionary<string, ushort> words,
        Dictionary<string, ushort> defaultWords,
        Dictionary<string, List<ushort>> extraWords,
        Dictionary<string, KeyBehavior> behaviors,
        Func<string, Dictionary<string, ushort>> loadWords,
        Func<string, Dictionary<string, List<ushort>>> loadExtraWords,
        Func<string, Dictionary<string, KeyBehavior>> loadBehaviors,
        Action<Dictionary<string, ushort>, Dictionary<string, List<ushort>>, Dictionary<string, KeyBehavior>> save)
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

    public void Rebind(string id, ushort vkCode)
    {
        Words[id] = vkCode;
        Save();
    }

    // How many keys can fire alongside an id's main key — six keys in all
    // (Fizzil's ask; it was two extras). The one number both the store
    // and the editor card check.
    public const int MaxExtraKeys = 5;

    // Capped at MaxExtraKeys; a no-op past that, so it's safe to call
    // speculatively without checking the count first.
    public void AddExtraKey(string id, ushort vkCode)
    {
        if (ExtraWords[id].Count >= MaxExtraKeys)
            return;

        ExtraWords[id].Add(vkCode);
        Save();
    }

    public void SetExtraKey(string id, int index, ushort vkCode)
    {
        var extras = ExtraWords[id];
        if (index < 0 || index >= extras.Count)
            return;

        extras[index] = vkCode;
        Save();
    }

    public void RemoveExtraKey(string id, int index)
    {
        var extras = ExtraWords[id];
        if (index < 0 || index >= extras.Count)
            return;

        extras.RemoveAt(index);
        Save();
    }

    // The full set of keys an id should press — its main key followed by
    // any extras — each paired with whether SendInput needs to treat it
    // as an extended key. Used everywhere an id is actually executed,
    // instead of just looking up Words[id] alone.
    public List<(ushort Vk, bool Extended)> GetAllKeys(string id)
    {
        var keys = new List<(ushort, bool)> { (Words[id], KeyMap.IsExtendedKey(Words[id])) };
        foreach (var vk in ExtraWords[id])
            keys.Add((vk, KeyMap.IsExtendedKey(vk)));
        return keys;
    }

    public void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds)
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

    // Puts an id back to its own DefaultWords entry, no extras, default
    // behavior — for MouseMap this lands on 0 (its DefaultWords is
    // all-zero), which combined with MouseMap.ResetToDefault also
    // switching Enabled back off is exactly "fully unmapped."
    public void ResetToDefault(string id)
    {
        Words[id] = DefaultWords[id];
        ExtraWords[id].Clear();
        Behaviors[id] = new KeyBehavior();
        Save();
    }

    // Every id at once, one save at the end instead of one per id.
    public void ResetAllToDefault()
    {
        foreach (var id in new List<string>(Words.Keys))
        {
            Words[id] = DefaultWords[id];
            ExtraWords[id].Clear();
            Behaviors[id] = new KeyBehavior();
        }
        Save();
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
