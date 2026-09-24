namespace UnboundKeys;

// The seam that lets one editor (Wpf/RemapCard) drive KeyMap, MouseMap or
// VirtualKeyMap without knowing which — every place the three differ (a
// word always has a real key; a mouse button starts unmapped) is captured
// in KeyLabelFor and AddKeySeed below, so the card never needs to ask
// "am I a word, a button, or a key."
internal interface IRemapSource
{
    Dictionary<string, ushort> Words { get; }
    Dictionary<string, List<ushort>> ExtraWords { get; }
    Dictionary<string, KeyBehavior> Behaviors { get; }

    void Rebind(string id, ushort vkCode);
    void AddExtraKey(string id, ushort vkCode);
    void SetExtraKey(string id, int index, ushort vkCode);
    void RemoveExtraKey(string id, int index);
    void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds);
    void ResetToDefault(string id);

    // "Key 1: X" for a word (always has a real key); "Key 1: Not Mapped"
    // for a button that hasn't been assigned one yet.
    string KeyLabelFor(string id);

    // Add Key's starting value for a new extra key slot — a word just
    // copies its own already-real main key; an unmapped button has
    // nothing sensible to copy, so it falls back to "A".
    ushort AddKeySeed(string id);
}

internal sealed class KeyMapSource : IRemapSource
{
    public static readonly KeyMapSource Instance = new();
    private KeyMapSource() { }

    public Dictionary<string, ushort> Words => KeyMap.Words;
    public Dictionary<string, List<ushort>> ExtraWords => KeyMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => KeyMap.Behaviors;

    public void Rebind(string id, ushort vkCode) => KeyMap.Rebind(id, vkCode);
    public void AddExtraKey(string id, ushort vkCode) => KeyMap.AddExtraKey(id, vkCode);
    public void SetExtraKey(string id, int index, ushort vkCode) => KeyMap.SetExtraKey(id, index, vkCode);
    public void RemoveExtraKey(string id, int index) => KeyMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds) =>
        KeyMap.SetBehavior(id, repeat, hold, durationSeconds, infinite, useCustomRepeatIntervals, repeatKeyIntervalsSeconds);
    public void ResetToDefault(string id) => KeyMap.ResetToDefault(id);

    public string KeyLabelFor(string id) => $"Key 1: {KeyCatalog.DisplayNameFor(KeyMap.Words[id])}";
    public ushort AddKeySeed(string id) => KeyMap.Words[id];
}

internal sealed class MouseMapSource : IRemapSource
{
    public static readonly MouseMapSource Instance = new();
    private MouseMapSource() { }

    public Dictionary<string, ushort> Words => MouseMap.Words;
    public Dictionary<string, List<ushort>> ExtraWords => MouseMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => MouseMap.Behaviors;

    public void Rebind(string id, ushort vkCode) => MouseMap.Rebind(id, vkCode);
    public void AddExtraKey(string id, ushort vkCode) => MouseMap.AddExtraKey(id, vkCode);
    public void SetExtraKey(string id, int index, ushort vkCode) => MouseMap.SetExtraKey(id, index, vkCode);
    public void RemoveExtraKey(string id, int index) => MouseMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds) =>
        MouseMap.SetBehavior(id, repeat, hold, durationSeconds, infinite, useCustomRepeatIntervals, repeatKeyIntervalsSeconds);
    public void ResetToDefault(string id) => MouseMap.ResetToDefault(id);

    public string KeyLabelFor(string id) =>
        MouseMap.Enabled[id] ? $"Key 1: {KeyCatalog.DisplayNameFor(MouseMap.Words[id])}" : "Key 1: Not Mapped";
    public ushort AddKeySeed(string id) => MouseMap.Enabled[id] ? MouseMap.Words[id] : (ushort)0x41;
}

internal sealed class VirtualKeyMapSource : IRemapSource
{
    public static readonly VirtualKeyMapSource Instance = new();
    private VirtualKeyMapSource() { }

    public Dictionary<string, ushort> Words => VirtualKeyMap.Words;
    public Dictionary<string, List<ushort>> ExtraWords => VirtualKeyMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => VirtualKeyMap.Behaviors;

    public void Rebind(string id, ushort vkCode) => VirtualKeyMap.Rebind(id, vkCode);
    public void AddExtraKey(string id, ushort vkCode) => VirtualKeyMap.AddExtraKey(id, vkCode);
    public void SetExtraKey(string id, int index, ushort vkCode) => VirtualKeyMap.SetExtraKey(id, index, vkCode);
    public void RemoveExtraKey(string id, int index) => VirtualKeyMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, bool repeat, bool hold, double durationSeconds, bool infinite, bool useCustomRepeatIntervals, List<double> repeatKeyIntervalsSeconds) =>
        VirtualKeyMap.SetBehavior(id, repeat, hold, durationSeconds, infinite, useCustomRepeatIntervals, repeatKeyIntervalsSeconds);
    public void ResetToDefault(string id) => VirtualKeyMap.ResetToDefault(id);

    public string KeyLabelFor(string id) => $"Key 1: {KeyCatalog.DisplayNameFor(VirtualKeyMap.Words[id])}";
    public ushort AddKeySeed(string id) => VirtualKeyMap.Words[id];
}
