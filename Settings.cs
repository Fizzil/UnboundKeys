using System.IO;
using System.Text.Json;

namespace UnboundKeys;

// Saves/loads the user's profiles (each its own key map + behaviors) so they
// survive closing the app. Stored outside the install folder (in AppData) so
// it works even if UnboundKeys is ever placed somewhere the user can't write
// to, like Program Files.
internal static class Settings
{
    public const string DefaultProfileName = "Default";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnboundKeys", "settings.json");

    // One-time migration for anyone upgrading from the app's old name
    // (VoicePress, renamed before this release — see the class comment):
    // AppData\VoicePress\settings.json exists but AppData\UnboundKeys\
    // doesn't yet, so every profile/keymap would otherwise look wiped out
    // the first time someone runs the renamed build. Copies rather than
    // moves — the old file is left in place, untouched, rather than
    // deleted, in case anything here goes wrong. A no-op forever after the
    // first successful run (the new file exists by then), so this is safe
    // to leave in indefinitely rather than needing to be pulled out later.
    private static void MigrateFromOldNameIfNeeded()
    {
        if (File.Exists(FilePath))
            return;

        var oldPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoicePress", "settings.json");
        if (!File.Exists(oldPath))
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.Copy(oldPath, FilePath);
        }
        catch
        {
            // If this fails, Read() just falls back to defaults, same as
            // any other unreadable-settings-file case.
        }
    }

    public sealed class ProfileData
    {
        public Dictionary<string, ushort> KeyMap { get; set; } = new();

        // Up to RemapStore.MaxExtraKeys extra keys per word, fired alongside
        // its main key as a combo. Absent entirely from settings files saved before this
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

        // Which named color palette ("Red"/"Green"/"Blue", see ThemeMode)
        // this profile uses — empty/absent for a profile saved before this
        // feature existed, in which case the caller's own default ("Red")
        // applies instead.
        public string ThemeColor { get; set; } = "";

        // Virtual-keyboard mappings (the 34 remappable on-screen keys — see
        // VirtualKeyCatalog) — same shape as KeyMap above, not Mouse's
        // Enabled-gated shape, since every virtual key always sends
        // something (its own key, unless remapped). Absent from settings
        // files saved before this existed — no migration needed, every key
        // just starts at its own natural default.
        public Dictionary<string, ushort> VirtualKeyMap { get; set; } = new();
        public Dictionary<string, List<ushort>> VirtualExtraKeys { get; set; } = new();
        public Dictionary<string, KeyBehavior> VirtualBehaviors { get; set; } = new();
    }

    private sealed class SavedData
    {
        public string ActiveProfile { get; set; } = DefaultProfileName;
        public Dictionary<string, ProfileData> Profiles { get; set; } = new();

        // Where the on-screen keyboard was last left (in WPF's
        // device-independent pixels), and whether it was collapsed to its
        // strip — app-wide, not per profile: a spot on this screen isn't
        // part of a mapping set. Null until the keyboard has been moved.
        public double? KeyboardLeft { get; set; }
        public double? KeyboardTop { get; set; }
        public bool KeyboardMini { get; set; }

        // How big the on-screen keyboard is drawn (1.0 = full size).
        // Defaults to about the footprint of Windows' own on-screen
        // keyboard, the one it replaces (Fizzil's ask).
        public double KeyboardScale { get; set; } = 0.65;

        // Where the dashboard window was last left — null until it has
        // been moved, in which case it opens centered.
        public double? DashboardLeft { get; set; }
        public double? DashboardTop { get; set; }

        // Settings > Startup: a scheduled task starts the app at sign-in
        // (see StartupTask), and whether that automatic start comes up
        // with the voice keys paused.
        public bool StartWithWindows { get; set; }
        public bool AutoStartPaused { get; set; }

        // Pre-profiles shape — only ever read, for one-time migration.
        public Dictionary<string, ushort>? KeyMap { get; set; }
        public Dictionary<string, KeyBehavior>? Behaviors { get; set; }
    }

    private static SavedData Read()
    {
        MigrateFromOldNameIfNeeded();

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

    public static (double? Left, double? Top, bool Mini) LoadKeyboardPlacement()
    {
        var saved = Read();
        return (saved.KeyboardLeft, saved.KeyboardTop, saved.KeyboardMini);
    }

    public static void SaveKeyboardPlacement(double left, double top, bool mini)
    {
        var saved = Read();
        saved.KeyboardLeft = left;
        saved.KeyboardTop = top;
        saved.KeyboardMini = mini;
        Write(saved);
    }

    public static (double? Left, double? Top) LoadDashboardPlacement()
    {
        var saved = Read();
        return (saved.DashboardLeft, saved.DashboardTop);
    }

    public static void SaveDashboardPlacement(double left, double top)
    {
        var saved = Read();
        saved.DashboardLeft = left;
        saved.DashboardTop = top;
        Write(saved);
    }

    public static double LoadKeyboardScale()
    {
        double scale = Read().KeyboardScale;
        return scale > 0 ? scale : 0.65;
    }

    public static void SaveKeyboardScale(double scale)
    {
        var saved = Read();
        saved.KeyboardScale = scale;
        Write(saved);
    }

    public static bool LoadStartWithWindows() => Read().StartWithWindows;
    public static bool LoadAutoStartPaused() => Read().AutoStartPaused;

    public static void SaveStartup(bool startWithWindows, bool autoStartPaused)
    {
        var saved = Read();
        saved.StartWithWindows = startWithWindows;
        saved.AutoStartPaused = autoStartPaused;
        Write(saved);
    }

    // Shared body for every Load* method below: only the ProfileData field
    // being read differs between them (word map vs. mouse map vs. virtual
    // map, and so on) — the "start from defaults, overlay whatever's saved
    // for keys defaults already knows about" logic itself was identical
    // nine times over. selector picks out that one field.
    private static Dictionary<string, T> LoadField<T>(string profile, Dictionary<string, T> defaults, Func<ProfileData, Dictionary<string, T>> selector)
    {
        if (Read().Profiles.TryGetValue(profile, out var data))
            foreach (var (id, value) in selector(data))
                if (defaults.ContainsKey(id))
                    defaults[id] = value;

        return defaults;
    }

    public static Dictionary<string, ushort> LoadKeyMap(string profile, Dictionary<string, ushort> defaults) =>
        LoadField(profile, defaults, d => d.KeyMap);

    public static Dictionary<string, List<ushort>> LoadExtraKeys(string profile, Dictionary<string, List<ushort>> defaults) =>
        LoadField(profile, defaults, d => d.ExtraKeys);

    public static Dictionary<string, KeyBehavior> LoadBehaviors(string profile, Dictionary<string, KeyBehavior> defaults) =>
        LoadField(profile, defaults, d => d.Behaviors);

    public static Dictionary<string, bool> LoadMouseEnabled(string profile, Dictionary<string, bool> defaults) =>
        LoadField(profile, defaults, d => d.MouseEnabled);

    public static Dictionary<string, ushort> LoadMouseKeyMap(string profile, Dictionary<string, ushort> defaults) =>
        LoadField(profile, defaults, d => d.MouseKeyMap);

    public static Dictionary<string, List<ushort>> LoadMouseExtraKeys(string profile, Dictionary<string, List<ushort>> defaults) =>
        LoadField(profile, defaults, d => d.MouseExtraKeys);

    public static Dictionary<string, KeyBehavior> LoadMouseBehaviors(string profile, Dictionary<string, KeyBehavior> defaults) =>
        LoadField(profile, defaults, d => d.MouseBehaviors);

    public static Dictionary<string, ushort> LoadVirtualKeyMap(string profile, Dictionary<string, ushort> defaults) =>
        LoadField(profile, defaults, d => d.VirtualKeyMap);

    public static Dictionary<string, List<ushort>> LoadVirtualExtraKeys(string profile, Dictionary<string, List<ushort>> defaults) =>
        LoadField(profile, defaults, d => d.VirtualExtraKeys);

    public static Dictionary<string, KeyBehavior> LoadVirtualBehaviors(string profile, Dictionary<string, KeyBehavior> defaults) =>
        LoadField(profile, defaults, d => d.VirtualBehaviors);

    public static string LoadThemeColor(string profile, string defaultColor)
    {
        if (Read().Profiles.TryGetValue(profile, out var data) && !string.IsNullOrEmpty(data.ThemeColor))
            return data.ThemeColor;

        return defaultColor;
    }

    public static void SaveThemeColor(string profile, string color) =>
        SaveFields(profile, d => d.ThemeColor = color);

    // Shared body for every Save* method below (CreateProfileIfMissing is
    // its own shape — a fresh ProfileData built once, only when the
    // profile doesn't exist yet — so it stays separate): read the
    // profile's current saved data, apply a patch that touches only the
    // fields that one caller owns, write the whole thing back — so saving
    // a voice-word change never clobbers whatever mouse-button mappings
    // are already on disk for that profile (and vice versa).
    private static void SaveFields(string profile, Action<ProfileData> patch)
    {
        var saved = Read();
        var data = saved.Profiles.TryGetValue(profile, out var existing) ? existing : new ProfileData();
        patch(data);
        saved.Profiles[profile] = data;
        Write(saved);
    }

    public static void SaveProfile(string profile, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors) =>
        SaveFields(profile, d => { d.KeyMap = keyMap; d.ExtraKeys = extraKeys; d.Behaviors = behaviors; });

    public static void SaveMouseProfile(string profile, Dictionary<string, bool> enabled, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors) =>
        SaveFields(profile, d => { d.MouseEnabled = enabled; d.MouseKeyMap = keyMap; d.MouseExtraKeys = extraKeys; d.MouseBehaviors = behaviors; });

    public static void SaveVirtualProfile(string profile, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors) =>
        SaveFields(profile, d => { d.VirtualKeyMap = keyMap; d.VirtualExtraKeys = extraKeys; d.VirtualBehaviors = behaviors; });

    // Creates a profile with the given defaults if it doesn't already exist —
    // a no-op if it does, so this is safe to call speculatively.
    public static void CreateProfileIfMissing(
        string profile,
        Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors,
        Dictionary<string, bool> mouseEnabled, Dictionary<string, ushort> mouseKeyMap, Dictionary<string, List<ushort>> mouseExtraKeys, Dictionary<string, KeyBehavior> mouseBehaviors)
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

    // Renames a profile in place. Profiles is rebuilt rather than
    // remove-then-add: System.Text.Json writes (and reads back) a
    // dictionary in insertion order, so the naive way would drop the
    // renamed profile to the bottom of the list. Refuses "Default", an
    // unknown old name, or a new name another profile already uses
    // (case-insensitively — the map itself is case-sensitive). If the
    // renamed profile is the active one, the caller must still run the
    // normal profile switch afterwards: KeyMap.ActiveProfile is what every
    // save is keyed by, and only SwitchProfile updates it.
    public static bool RenameProfile(string oldName, string newName)
    {
        newName = newName.Trim();
        if (oldName == DefaultProfileName || newName.Length == 0)
            return false;

        var saved = Read();
        if (!saved.Profiles.ContainsKey(oldName))
            return false;
        foreach (var existing in saved.Profiles.Keys)
            if (existing != oldName && existing.Equals(newName, StringComparison.OrdinalIgnoreCase))
                return false;
        if (oldName == newName)
            return true;

        var renamed = new Dictionary<string, ProfileData>();
        foreach (var (name, data) in saved.Profiles)
            renamed[name == oldName ? newName : name] = data;
        saved.Profiles = renamed;
        if (saved.ActiveProfile == oldName)
            saved.ActiveProfile = newName;
        Write(saved);
        return true;
    }
}
