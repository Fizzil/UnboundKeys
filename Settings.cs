using System.Text.Json;

namespace VoicePress;

// Saves/loads the user's key assignments and behaviors so they survive
// closing the app. Stored outside the install folder (in AppData) so it works
// even if VoicePress is ever placed somewhere the user can't write to, like
// Program Files.
internal static class Settings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoicePress", "settings.json");

    private sealed class SavedData
    {
        public Dictionary<string, ushort> KeyMap { get; set; } = new();
        public Dictionary<string, KeyBehavior> Behaviors { get; set; } = new();
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
                    return saved;
            }
        }
        catch
        {
            // Corrupt or unreadable settings file — fall back to the defaults.
        }

        return new SavedData();
    }

    public static Dictionary<string, ushort> LoadKeyMap(Dictionary<string, ushort> defaults)
    {
        foreach (var (word, vk) in Read().KeyMap)
            if (defaults.ContainsKey(word))
                defaults[word] = vk;

        return defaults;
    }

    public static Dictionary<string, KeyBehavior> LoadBehaviors(Dictionary<string, KeyBehavior> defaults)
    {
        foreach (var (word, behavior) in Read().Behaviors)
            if (defaults.ContainsKey(word))
                defaults[word] = behavior;

        return defaults;
    }

    public static void Save(Dictionary<string, ushort> keyMap, Dictionary<string, KeyBehavior> behaviors)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var data = new SavedData { KeyMap = keyMap, Behaviors = behaviors };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch
        {
            // If saving fails (e.g. disk full), the app keeps working — it just
            // won't remember the change next time it starts.
        }
    }
}
