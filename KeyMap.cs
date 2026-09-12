namespace VoicePress;

// Maps the word you say after "press" to a Windows virtual-key code.
public static class KeyMap
{
    public static readonly Dictionary<string, ushort> Words = BuildMap();

    // None of the current keys need the "extended key" SendInput flag (that's only
    // for nav-cluster/arrow keys), but this stays here in case the vocabulary grows.
    public static readonly HashSet<ushort> ExtendedKeys = new();

    private static Dictionary<string, ushort> BuildMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);

        // Kept deliberately small — one through nine plus escape — since a smaller
        // grammar gives the speech engine fewer similar-sounding options to confuse.
        string[] numberWords = { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
        for (int i = 0; i < numberWords.Length; i++)
            map[numberWords[i]] = (ushort)(0x31 + i);

        map["escape"] = 0x1B;
        map["esc"] = 0x1B;

        return map;
    }
}
