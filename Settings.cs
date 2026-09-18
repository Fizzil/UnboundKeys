using System.Text.Json;

namespace VoicePress;

// Saves/loads the user's profiles (each its own key map + behaviors) so they
// survive closing the app. Stored outside the install folder (in AppData) so
// it works even if VoicePress is ever placed somewhere the user can't write
// to, like Program Files.
internal static class Settings
{
    public const string DefaultProfileName = "Default";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoicePress", "settings.json");

    public sealed class ProfileData
    {
        public Dictionary<string, ushort> KeyMap { get; set; } = new();

        // Up to two extra keys per word, fired alongside its main key as a
        // combo. Absent entirely from settings files saved before this
        // existed — deserializing just leaves this at its empty default, so
        // older profiles need no migration step.
        public Dictionary<string, List<ushort>> ExtraKeys { get; set; } = new();

        public Dictionary<string, KeyBehavior> Behaviors { get; set; } = new();

        // Mouse-button mappings — same shape as the three above, just keyed
        // by MouseCatalog button id (e.g. "middle") instead of a spoken word.
        // MouseEnabled tracks which buttons actually have a key assigned; an
        // unmapped button passes its click through untouched. Absent from
        // settings files saved before this existed, same as ExtraKeys above
        // — no migration needed, every button just starts unmapped.
        public Dictionary<string, bool> MouseEnabled { get; set; } = new();
        public Dictionary<string, ushort> MouseKeyMap { get; set; } = new();
        public Dictionary<string, List<ushort>> MouseExtraKeys { get; set; } = new();
        public Dictionary<string, KeyBehavior> MouseBehaviors { get; set; } = new();

        // Physical-keyboard mappings (the number-row keys Physical Press
        // remaps) — same shape as the mouse fields above, just keyed by
        // PhysicalKeyCatalog id (e.g. "phys1") instead. Absent from
        // settings files saved before this existed, same reasoning as
        // MouseEnabled above — no migration needed.
        public Dictionary<string, bool> PhysicalEnabled { get; set; } = new();
        public Dictionary<string, ushort> PhysicalKeyMap { get; set; } = new();
        public Dictionary<string, List<ushort>> PhysicalExtraKeys { get; set; } = new();
        public Dictionary<string, KeyBehavior> PhysicalBehaviors { get; set; } = new();

        // Which of "voice"/"physical" is this profile's active Press
        // source (see PressMode) — empty/absent for a profile saved
        // before this feature existed, in which case the caller's own
        // default ("voice") applies instead.
        public string ActivePressMode { get; set; } = "";
    }

    private sealed class SavedData
    {
        public string ActiveProfile { get; set; } = DefaultProfileName;
        public Dictionary<string, ProfileData> Profiles { get; set; } = new();

        // Pre-profiles shape — only ever read, for one-time migration.
        public Dictionary<string, ushort>? KeyMap { get; set; }
        public Dictionary<string, KeyBehavior>? Behaviors { get; set; }
    }

    private static SavedData Read()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var saved = JsonSerializer.Deserialize<SavedData>(json);
                if (saved != null)
                {
                    // The app used to store one flat key map/behavior set with
                    // no concept of profiles. If that's what's on disk and no
                    // profiles exist yet, keep it as the first ("Default")
                    // profile instead of silently discarding it.
                    if (saved.Profiles.Count == 0 && (saved.KeyMap != null || saved.Behaviors != null))
                    {
                        saved.Profiles[DefaultProfileName] = new ProfileData
                        {
                            KeyMap = saved.KeyMap ?? new(),
                            Behaviors = saved.Behaviors ?? new(),
                        };
                        saved.ActiveProfile = DefaultProfileName;
                    }
                    return saved;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to the defaults.
        }

        return new SavedData();
    }

    private static void Write(SavedData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            data.KeyMap = null;
            data.Behaviors = null;
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
            // If saving fails (e.g. disk full), the app keeps working — it
            // just won't remember the change next time it starts.
        }
    }

    // Every known profile name, "Default" always included even if it hasn't
    // been saved to yet.
    public static List<string> LoadProfileNames()
    {
        var saved = Read();
        var names = new List<string>(saved.Profiles.Keys);
        if (!names.Contains(DefaultProfileName))
            names.Insert(0, DefaultProfileName);
        return names;
    }

    public static string LoadActiveProfileName()
    {
        var saved = Read();
        return string.IsNullOrEmpty(saved.ActiveProfile) ? DefaultProfileName : saved.ActiveProfile;
    }

    public static void SetActiveProfile(string profile)
    {
        var saved = Read();
        saved.ActiveProfile = profile;
        Write(saved);
    }

    public static Dictionary<string, ushort> LoadKeyMap(string profile, Dictionary<string, ushort> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (word, vk) in data.KeyMap)
                if (defaults.ContainsKey(word))
                    defaults[word] = vk;

        return defaults;
    }

    public static Dictionary<string, List<ushort>> LoadExtraKeys(string profile, Dictionary<string, List<ushort>> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (word, extras) in data.ExtraKeys)
                if (defaults.ContainsKey(word))
                    defaults[word] = extras;

        return defaults;
    }

    public static Dictionary<string, KeyBehavior> LoadBehaviors(string profile, Dictionary<string, KeyBehavior> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (word, behavior) in data.Behaviors)
                if (defaults.ContainsKey(word))
                    defaults[word] = behavior;

        return defaults;
    }

    public static Dictionary<string, bool> LoadMouseEnabled(string profile, Dictionary<string, bool> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, enabled) in data.MouseEnabled)
                if (defaults.ContainsKey(id))
                    defaults[id] = enabled;

        return defaults;
    }

    public static Dictionary<string, ushort> LoadMouseKeyMap(string profile, Dictionary<string, ushort> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, vk) in data.MouseKeyMap)
                if (defaults.ContainsKey(id))
                    defaults[id] = vk;

        return defaults;
    }

    public static Dictionary<string, List<ushort>> LoadMouseExtraKeys(string profile, Dictionary<string, List<ushort>> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, extras) in data.MouseExtraKeys)
                if (defaults.ContainsKey(id))
                    defaults[id] = extras;

        return defaults;
    }

    public static Dictionary<string, KeyBehavior> LoadMouseBehaviors(string profile, Dictionary<string, KeyBehavior> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, behavior) in data.MouseBehaviors)
                if (defaults.ContainsKey(id))
                    defaults[id] = behavior;

        return defaults;
    }

    public static Dictionary<string, bool> LoadPhysicalEnabled(string profile, Dictionary<string, bool> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, enabled) in data.PhysicalEnabled)
                if (defaults.ContainsKey(id))
                    defaults[id] = enabled;

        return defaults;
    }

    public static Dictionary<string, ushort> LoadPhysicalKeyMap(string profile, Dictionary<string, ushort> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, vk) in data.PhysicalKeyMap)
                if (defaults.ContainsKey(id))
                    defaults[id] = vk;

        return defaults;
    }

    public static Dictionary<string, List<ushort>> LoadPhysicalExtraKeys(string profile, Dictionary<string, List<ushort>> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, extras) in data.PhysicalExtraKeys)
                if (defaults.ContainsKey(id))
                    defaults[id] = extras;

        return defaults;
    }

    public static Dictionary<string, KeyBehavior> LoadPhysicalBehaviors(string profile, Dictionary<string, KeyBehavior> defaults)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, behavior) in data.PhysicalBehaviors)
                if (defaults.ContainsKey(id))
                    defaults[id] = behavior;

        return defaults;
    }

    public static string LoadActivePressMode(string profile, string defaultMode)
    {
        if (Read().Profiles.TryGetValue(profile, out var data) && !string.IsNullOrEmpty(data.ActivePressMode))
            return data.ActivePressMode;

        return defaultMode;
    }

    public static void SaveActivePressMode(string profile, string mode)
    {
        var saved = Read();
        var data = saved.Profiles.TryGetValue(profile, out var existing) ? existing : new ProfileData();
        data.ActivePressMode = mode;
        saved.Profiles[profile] = data;
        Write(saved);
    }

    // Each Save*/Create* method below reads the profile's current saved data,
    // patches only the fields it owns, and writes the whole thing back — so
    // saving a voice-word change never clobbers whatever mouse-button
    // mappings are already on disk for that profile (and vice versa).
    public static void SaveProfile(string profile, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors)
    {
        var saved = Read();
        var data = saved.Profiles.TryGetValue(profile, out var existing) ? existing : new ProfileData();
        data.KeyMap = keyMap;
        data.ExtraKeys = extraKeys;
        data.Behaviors = behaviors;
        saved.Profiles[profile] = data;
        Write(saved);
    }

    public static void SaveMouseProfile(string profile, Dictionary<string, bool> enabled, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors)
    {
        var saved = Read();
        var data = saved.Profiles.TryGetValue(profile, out var existing) ? existing : new ProfileData();
        data.MouseEnabled = enabled;
        data.MouseKeyMap = keyMap;
        data.MouseExtraKeys = extraKeys;
        data.MouseBehaviors = behaviors;
        saved.Profiles[profile] = data;
        Write(saved);
    }

    public static void SavePhysicalProfile(string profile, Dictionary<string, bool> enabled, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors)
    {
        var saved = Read();
        var data = saved.Profiles.TryGetValue(profile, out var existing) ? existing : new ProfileData();
        data.PhysicalEnabled = enabled;
        data.PhysicalKeyMap = keyMap;
        data.PhysicalExtraKeys = extraKeys;
        data.PhysicalBehaviors = behaviors;
        saved.Profiles[profile] = data;
        Write(saved);
    }

    // Creates a profile with the given defaults if it doesn't already exist —
    // a no-op if it does, so this is safe to call speculatively.
    public static void CreateProfileIfMissing(
        string profile,
        Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors,
        Dictionary<string, bool> mouseEnabled, Dictionary<string, ushort> mouseKeyMap, Dictionary<string, List<ushort>> mouseExtraKeys, Dictionary<string, KeyBehavior> mouseBehaviors,
        Dictionary<string, bool> physicalEnabled, Dictionary<string, ushort> physicalKeyMap, Dictionary<string, List<ushort>> physicalExtraKeys, Dictionary<string, KeyBehavior> physicalBehaviors)
    {
        var saved = Read();
        if (saved.Profiles.ContainsKey(profile))
            return;

        saved.Profiles[profile] = new ProfileData
        {
            KeyMap = keyMap,
            ExtraKeys = extraKeys,
            Behaviors = behaviors,
            MouseEnabled = mouseEnabled,
            MouseKeyMap = mouseKeyMap,
            MouseExtraKeys = mouseExtraKeys,
            MouseBehaviors = mouseBehaviors,
            PhysicalEnabled = physicalEnabled,
            PhysicalKeyMap = physicalKeyMap,
            PhysicalExtraKeys = physicalExtraKeys,
            PhysicalBehaviors = physicalBehaviors,
        };
        Write(saved);
    }

    public static void DeleteProfile(string profile)
    {
        var saved = Read();
        saved.Profiles.Remove(profile);
        if (saved.ActiveProfile == profile)
            saved.ActiveProfile = DefaultProfileName;
        Write(saved);
    }
}
