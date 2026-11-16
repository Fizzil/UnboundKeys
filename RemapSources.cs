namespace UnboundKeys;

// The seam that lets one editor (Wpf/RemapCard) drive KeyMap, MouseMap or
// VirtualKeyMap without knowing which — every place the three differ (a
// word always has a real key; a mouse button starts unmapped) is captured
// in KeyLabelFor and AddKeySeed below, so the card never needs to ask
// "am I a word, a button, or a key."
internal interface IRemapSource
{
    Dictionary<string, Key> Words { get; }
    Dictionary<string, List<Key>> ExtraWords { get; }
    Dictionary<string, KeyBehavior> Behaviors { get; }

    void Rebind(string id, Key key);
    void AddExtraKey(string id, Key key);
    void SetExtraKey(string id, int index, Key key);
    void RemoveExtraKey(string id, int index);
    void SetBehavior(string id, KeyBehavior behavior);
    void ResetToDefault(string id);

    // "Key 1: X" for a word (always has a real key); "Key 1: Not Mapped"
    // for a button that hasn't been assigned one yet.
    string KeyLabelFor(string id);

    // Whether the id sends anything at all: a word always does; a button,
    // or a key's double press, can be Not Mapped.
    bool IsMapped(string id);

    // Add Key's starting value for a new extra key slot — a word just
    // copies its own already-real main key; an unmapped button has
    // nothing sensible to copy, so it falls back to "A".
    Key AddKeySeed(string id);
}

internal sealed class KeyMapSource : IRemapSource
{
    public static readonly KeyMapSource Instance = new();
    private KeyMapSource() { }

    public Dictionary<string, Key> Words => KeyMap.Words;
    public Dictionary<string, List<Key>> ExtraWords => KeyMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => KeyMap.Behaviors;

    public void Rebind(string id, Key key) => KeyMap.Rebind(id, key);
    public void AddExtraKey(string id, Key key) => KeyMap.AddExtraKey(id, key);
    public void SetExtraKey(string id, int index, Key key) => KeyMap.SetExtraKey(id, index, key);
    public void RemoveExtraKey(string id, int index) => KeyMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, KeyBehavior behavior) => KeyMap.SetBehavior(id, behavior);
    public void ResetToDefault(string id) => KeyMap.ResetToDefault(id);

    public string KeyLabelFor(string id) => $"Key 1: {KeyCatalog.DisplayNameFor(KeyMap.Words[id])}";
    public bool IsMapped(string id) => true;
    public Key AddKeySeed(string id) => KeyMap.Words[id];
}

internal sealed class MouseMapSource : IRemapSource
{
    public static readonly MouseMapSource Instance = new();
    private MouseMapSource() { }

    public Dictionary<string, Key> Words => MouseMap.Words;
    public Dictionary<string, List<Key>> ExtraWords => MouseMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => MouseMap.Behaviors;

    public void Rebind(string id, Key key) => MouseMap.Rebind(id, key);
    public void AddExtraKey(string id, Key key) => MouseMap.AddExtraKey(id, key);
    public void SetExtraKey(string id, int index, Key key) => MouseMap.SetExtraKey(id, index, key);
    public void RemoveExtraKey(string id, int index) => MouseMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, KeyBehavior behavior) => MouseMap.SetBehavior(id, behavior);
    public void ResetToDefault(string id) => MouseMap.ResetToDefault(id);

    public string KeyLabelFor(string id) =>
        MouseMap.Enabled[id] ? $"Key 1: {KeyCatalog.DisplayNameFor(MouseMap.Words[id])}" : "Key 1: Not Mapped";
    public bool IsMapped(string id) => MouseMap.Enabled[id];
    public Key AddKeySeed(string id) => MouseMap.Enabled[id] ? MouseMap.Words[id] : Key.A;
}

// The Steering page's pushes: unmapped until a key is picked, as a mouse
// button is, but app-wide (see SteeringMap).
internal sealed class SteeringMapSource : IRemapSource
{
    public static readonly SteeringMapSource Instance = new();
    private SteeringMapSource() { }

    public Dictionary<string, Key> Words => SteeringMap.Words;
    public Dictionary<string, List<Key>> ExtraWords => SteeringMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => SteeringMap.Behaviors;

    public void Rebind(string id, Key key) => SteeringMap.Rebind(id, key);
    public void AddExtraKey(string id, Key key) => SteeringMap.AddExtraKey(id, key);
    public void SetExtraKey(string id, int index, Key key) => SteeringMap.SetExtraKey(id, index, key);
    public void RemoveExtraKey(string id, int index) => SteeringMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, KeyBehavior behavior) => SteeringMap.SetBehavior(id, behavior);
    public void ResetToDefault(string id) => SteeringMap.ResetToDefault(id);

    public string KeyLabelFor(string id) =>
        SteeringMap.Enabled[id] ? $"Key 1: {KeyCatalog.DisplayNameFor(SteeringMap.Words[id])}" : "Key 1: Not Mapped";
    public bool IsMapped(string id) => SteeringMap.Enabled[id];
    public Key AddKeySeed(string id) => SteeringMap.Enabled[id] ? SteeringMap.Words[id] : Key.A;
}

internal sealed class VirtualKeyMapSource : IRemapSource
{
    public static readonly VirtualKeyMapSource Instance = new();
    private VirtualKeyMapSource() { }

    public Dictionary<string, Key> Words => VirtualKeyMap.Words;
    public Dictionary<string, List<Key>> ExtraWords => VirtualKeyMap.ExtraWords;
    public Dictionary<string, KeyBehavior> Behaviors => VirtualKeyMap.Behaviors;

    public void Rebind(string id, Key key) => VirtualKeyMap.Rebind(id, key);
    public void AddExtraKey(string id, Key key) => VirtualKeyMap.AddExtraKey(id, key);
    public void SetExtraKey(string id, int index, Key key) => VirtualKeyMap.SetExtraKey(id, index, key);
    public void RemoveExtraKey(string id, int index) => VirtualKeyMap.RemoveExtraKey(id, index);
    public void SetBehavior(string id, KeyBehavior behavior) => VirtualKeyMap.SetBehavior(id, behavior);
    public void ResetToDefault(string id) => VirtualKeyMap.ResetToDefault(id);

    // A key's own mapping always has a real key; its double press starts
    // "Not Mapped" (0), as a mouse button does (see VirtualKeyMap.IsMapped).
    public string KeyLabelFor(string id) =>
        VirtualKeyMap.IsMapped(id) ? $"Key 1: {KeyCatalog.DisplayNameFor(VirtualKeyMap.Words[id])}" : "Key 1: Not Mapped";
    public bool IsMapped(string id) => VirtualKeyMap.IsMapped(id);
    public Key AddKeySeed(string id) => VirtualKeyMap.IsMapped(id) ? VirtualKeyMap.Words[id] : Key.A;
}
