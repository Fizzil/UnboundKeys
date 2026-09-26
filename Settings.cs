using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnboundKeys;

// Saves/loads the user's profiles so they survive closing the app. A
// profile is a game: its theme colour, and which of its sub-profiles it was
// last on. Each game holds up to ten sub-profiles (a class, a loadout —
// Fizzil's ask after WoW), and a sub-profile is one complete mapping set:
// the ten words, the six mouse buttons, the on-screen keyboard's keys.
// Every Load*/Save* below takes the game's name and quietly reads or
// writes that game's ACTIVE sub-profile, so the three stores (KeyMap,
// MouseMap, VirtualKeyMap) never need to know the second level exists.
// Stored outside the install folder (in AppData) so it works even if
// UnboundKeys is ever placed somewhere the user can't write to, like
// Program Files.
internal static class Settings
{
    public const string DefaultProfileName = "Default";
    public const string DefaultSubProfileName = "Default";

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

    // One complete set of mappings — what a sub-profile is. Any field
    // absent from an older settings file just stays at its empty default,
    // which every Load* reads as "the app's defaults", so no migration is
    // needed when a new kind of mapping is added.
    public sealed class MappingSet
    {
        public Dictionary<string, ushort> KeyMap { get; set; } = new();

        // Up to RemapStore.MaxExtraKeys extra keys per word, fired alongside
        // its main key as a combo.
        public Dictionary<string, List<ushort>> ExtraKeys { get; set; } = new();
        public Dictionary<string, KeyBehavior> Behaviors { get; set; } = new();

        // Mouse-button mappings — same shape, keyed by MouseCatalog button id
        // (e.g. "middle"). MouseEnabled tracks which buttons actually have a
        // key assigned; an unmapped button passes its click through.
        public Dictionary<string, bool> MouseEnabled { get; set; } = new();
        public Dictionary<string, ushort> MouseKeyMap { get; set; } = new();
        public Dictionary<string, List<ushort>> MouseExtraKeys { get; set; } = new();
        public Dictionary<string, KeyBehavior> MouseBehaviors { get; set; } = new();

        // The on-screen keyboard's remappable keys (see VirtualKeyCatalog):
        // every one always sends something, so no Enabled map here.
        public Dictionary<string, ushort> VirtualKeyMap { get; set; } = new();
        public Dictionary<string, List<ushort>> VirtualExtraKeys { get; set; } = new();
        public Dictionary<string, KeyBehavior> VirtualBehaviors { get; set; } = new();

        // A JSON round trip is the one deep copy this file needs (a new
        // sub-profile starts as a copy of the active one).
        public MappingSet Clone() =>
            JsonSerializer.Deserialize<MappingSet>(JsonSerializer.Serialize(this)) ?? new MappingSet();
    }

    // A game.
    public sealed class ProfileData
    {
        // Which named color palette ("Red"/"Green"/"Blue", see ThemeMode)
        // this profile uses — empty for a profile saved before that existed,
        // in which case the caller's own default ("Red") applies.
        public string ThemeColor { get; set; } = "";

        public string ActiveSubProfile { get; set; } = "";
        public Dictionary<string, MappingSet> SubProfiles { get; set; } = new();

        // Before sub-profiles existed the mappings sat right here. Read once
        // for migration (see Read), then nulled so they are never written
        // again — null is left out of the file entirely.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, ushort>? KeyMap { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<ushort>>? ExtraKeys { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, KeyBehavior>? Behaviors { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, bool>? MouseEnabled { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, ushort>? MouseKeyMap { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<ushort>>? MouseExtraKeys { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, KeyBehavior>? MouseBehaviors { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, ushort>? VirtualKeyMap { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<ushort>>? VirtualExtraKeys { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, KeyBehavior>? VirtualBehaviors { get; set; }

        [JsonIgnore]
        public bool HasLegacyMappings =>
            KeyMap != null || ExtraKeys != null || Behaviors != null
            || MouseEnabled != null || MouseKeyMap != null || MouseExtraKeys != null || MouseBehaviors != null
            || VirtualKeyMap != null || VirtualExtraKeys != null || VirtualBehaviors != null;

        // The pre-sub-profiles mappings become the game's one sub-profile,
        // "Default", and the flat fields are dropped.
        public void MigrateLegacyMappings()
        {
            if (SubProfiles.Count == 0 && HasLegacyMappings)
            {
                SubProfiles[DefaultSubProfileName] = new MappingSet
                {
                    KeyMap = KeyMap ?? new(),
                    ExtraKeys = ExtraKeys ?? new(),
                    Behaviors = Behaviors ?? new(),
                    MouseEnabled = MouseEnabled ?? new(),
                    MouseKeyMap = MouseKeyMap ?? new(),
                    MouseExtraKeys = MouseExtraKeys ?? new(),
                    MouseBehaviors = MouseBehaviors ?? new(),
                    VirtualKeyMap = VirtualKeyMap ?? new(),
                    VirtualExtraKeys = VirtualExtraKeys ?? new(),
                    VirtualBehaviors = VirtualBehaviors ?? new(),
                };
                ActiveSubProfile = DefaultSubProfileName;
            }
            KeyMap = null; ExtraKeys = null; Behaviors = null;
            MouseEnabled = null; MouseKeyMap = null; MouseExtraKeys = null; MouseBehaviors = null;
            VirtualKeyMap = null; VirtualExtraKeys = null; VirtualBehaviors = null;
        }
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

        // The editor Game mode switch (see RemapCard): app-wide, so it stays
        // on across every editor once a game is being set up.
        public bool GameMode { get; set; }

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

                    // Then the pre-sub-profiles shape: each profile's flat
                    // mappings become its "Default" sub-profile. Every game
                    // ends up with at least one sub-profile and a valid
                    // active one.
                    foreach (var game in saved.Profiles.Values)
                    {
                        game.MigrateLegacyMappings();
                        if (game.SubProfiles.Count == 0)
                            game.SubProfiles[DefaultSubProfileName] = new MappingSet();
                        game.ActiveSubProfile = ActiveSubOf(game);
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

    // The sub-profile a game is on: what it says, if that still exists,
    // otherwise its first one, otherwise "Default".
    private static string ActiveSubOf(ProfileData game)
    {
        if (game.SubProfiles.ContainsKey(game.ActiveSubProfile))
            return game.ActiveSubProfile;
        foreach (var name in game.SubProfiles.Keys)
            return name;
        return DefaultSubProfileName;
    }

    // The mapping set a game's name currently stands for, or null if the
    // game has never been saved.
    private static MappingSet? Resolve(SavedData saved, string profile) =>
        saved.Profiles.TryGetValue(profile, out var game) && game.SubProfiles.TryGetValue(ActiveSubOf(game), out var set)
            ? set
            : null;

    // The game, created with one empty "Default" sub-profile if it has never
    // been saved (an empty set reads as the app's defaults).
    private static ProfileData GameOrNew(SavedData saved, string profile)
    {
        if (!saved.Profiles.TryGetValue(profile, out var game))
        {
            game = new ProfileData();
            saved.Profiles[profile] = game;
        }
        if (game.SubProfiles.Count == 0)
            game.SubProfiles[DefaultSubProfileName] = new MappingSet();
        game.ActiveSubProfile = ActiveSubOf(game);
        return game;
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

    // ---- Sub-profiles -------------------------------------------------

    public static List<string> LoadSubProfileNames(string profile)
    {
        var saved = Read();
        if (saved.Profiles.TryGetValue(profile, out var game) && game.SubProfiles.Count > 0)
            return new List<string>(game.SubProfiles.Keys);
        return new List<string> { DefaultSubProfileName };
    }

    public static string LoadActiveSubProfile(string profile) =>
        Read().Profiles.TryGetValue(profile, out var game) ? ActiveSubOf(game) : DefaultSubProfileName;

    // Points the game at one of its sub-profiles. The caller then re-runs
    // the stores' SwitchProfile with the same game name so they reload.
    public static bool SetActiveSubProfile(string profile, string sub)
    {
        var saved = Read();
        var game = GameOrNew(saved, profile);
        if (!game.SubProfiles.ContainsKey(sub))
            return false;
        game.ActiveSubProfile = sub;
        Write(saved);
        return true;
    }

    // A new sub-profile starts as a copy of the game's active one (Fizzil's
    // choice: change the few keys that differ between classes). Refuses an
    // empty name or one the game already has, case-insensitively.
    public static bool CreateSubProfile(string profile, string name)
    {
        name = name.Trim();
        if (name.Length == 0)
            return false;
        var saved = Read();
        var game = GameOrNew(saved, profile);
        foreach (var existing in game.SubProfiles.Keys)
            if (existing.Equals(name, StringComparison.OrdinalIgnoreCase))
                return false;
        game.SubProfiles[name] = game.SubProfiles[ActiveSubOf(game)].Clone();
        Write(saved);
        return true;
    }

    // Refuses to delete a game's last sub-profile: a game always has one.
    // If the deleted one was active, the game moves to its first remaining
    // sub-profile; the caller then re-runs the stores' SwitchProfile.
    public static bool DeleteSubProfile(string profile, string name)
    {
        var saved = Read();
        if (!saved.Profiles.TryGetValue(profile, out var game) || game.SubProfiles.Count <= 1)
            return false;
        if (!game.SubProfiles.Remove(name))
            return false;
        game.ActiveSubProfile = ActiveSubOf(game);
        Write(saved);
        return true;
    }

    // Renames in place, order preserved (see RenameProfile for why the
    // dictionary is rebuilt). Refuses an unknown old name, an empty new
    // one, or a name the game already uses, case-insensitively.
    public static bool RenameSubProfile(string profile, string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
            return false;
        var saved = Read();
        if (!saved.Profiles.TryGetValue(profile, out var game) || !game.SubProfiles.ContainsKey(oldName))
            return false;
        foreach (var existing in game.SubProfiles.Keys)
            if (existing != oldName && existing.Equals(newName, StringComparison.OrdinalIgnoreCase))
                return false;
        if (oldName == newName)
            return true;

        var renamed = new Dictionary<string, MappingSet>();
        foreach (var (name, set) in game.SubProfiles)
            renamed[name == oldName ? newName : name] = set;
        game.SubProfiles = renamed;
        if (game.ActiveSubProfile == oldName)
            game.ActiveSubProfile = newName;
        Write(saved);
        return true;
    }

    // ---- App-wide bits ------------------------------------------------

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

    public static bool LoadGameMode() => Read().GameMode;

    public static void SaveGameMode(bool on)
    {
        var saved = Read();
        saved.GameMode = on;
        Write(saved);
    }

    // ---- Mappings, through the game's active sub-profile --------------

    // Shared body for every Load* method below: only the MappingSet field
    // being read differs between them (word map vs. mouse map vs. virtual
    // map, and so on) — the "start from defaults, overlay whatever's saved
    // for keys defaults already knows about" logic itself was identical
    // nine times over. selector picks out that one field.
    private static Dictionary<string, T> LoadField<T>(string profile, Dictionary<string, T> defaults, Func<MappingSet, Dictionary<string, T>> selector)
    {
        if (Resolve(Read(), profile) is { } data)
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

    // The theme belongs to the game, not the sub-profile: switching class
    // keeps the game's colour.
    public static string LoadThemeColor(string profile, string defaultColor)
    {
        if (Read().Profiles.TryGetValue(profile, out var game) && !string.IsNullOrEmpty(game.ThemeColor))
            return game.ThemeColor;

        return defaultColor;
    }

    public static void SaveThemeColor(string profile, string color)
    {
        var saved = Read();
        GameOrNew(saved, profile).ThemeColor = color;
        Write(saved);
    }

    // Shared body for every Save* method below: read the game's active
    // sub-profile, apply a patch that touches only the fields that one
    // caller owns, write the whole thing back — so saving a voice-word
    // change never clobbers whatever mouse-button mappings are already on
    // disk for that sub-profile (and vice versa).
    private static void SaveFields(string profile, Action<MappingSet> patch)
    {
        var saved = Read();
        var game = GameOrNew(saved, profile);
        patch(game.SubProfiles[game.ActiveSubProfile]);
        Write(saved);
    }

    public static void SaveProfile(string profile, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors) =>
        SaveFields(profile, d => { d.KeyMap = keyMap; d.ExtraKeys = extraKeys; d.Behaviors = behaviors; });

    public static void SaveMouseProfile(string profile, Dictionary<string, bool> enabled, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors) =>
        SaveFields(profile, d => { d.MouseEnabled = enabled; d.MouseKeyMap = keyMap; d.MouseExtraKeys = extraKeys; d.MouseBehaviors = behaviors; });

    public static void SaveVirtualProfile(string profile, Dictionary<string, ushort> keyMap, Dictionary<string, List<ushort>> extraKeys, Dictionary<string, KeyBehavior> behaviors) =>
        SaveFields(profile, d => { d.VirtualKeyMap = keyMap; d.VirtualExtraKeys = extraKeys; d.VirtualBehaviors = behaviors; });

    // Creates a game with the given defaults as its one "Default"
    // sub-profile if it doesn't already exist — a no-op if it does, so this
    // is safe to call speculatively.
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
            ActiveSubProfile = DefaultSubProfileName,
            SubProfiles =
            {
                [DefaultSubProfileName] = new MappingSet
                {
                    KeyMap = keyMap,
                    ExtraKeys = extraKeys,
                    Behaviors = behaviors,
                    MouseEnabled = mouseEnabled,
                    MouseKeyMap = mouseKeyMap,
                    MouseExtraKeys = mouseExtraKeys,
                    MouseBehaviors = mouseBehaviors,
                },
            },
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

    // Renames a game in place. Profiles is rebuilt rather than
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
