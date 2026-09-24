using System.IO;
using System.Reflection;

namespace UnboundKeys;

// Word suggestions for the on-screen keyboard — the strip of completions
// Windows' own on-screen keyboard shows, done the only way it can be done
// from outside the app being typed into: VirtualKeyboardWindow sends
// every keystroke itself, so it knows the word in progress; this just
// turns that prefix into candidates. Two sources: a built-in list of the
// 20,000 most frequent English words (Assets/words-en.txt, from Hermit
// Dave's OpenSubtitles-based FrequencyWords, CC BY-SA 4.0 — film
// subtitles skew conversational, which suits chat while gaming), and the
// words Fizzil has actually typed, which rank first. Everything is
// offline.
public static class WordPredictor
{
    public const int MaxSuggestions = 8;

    private static readonly string LearnedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnboundKeys", "learned-words.txt");

    private static string[]? _words;
    private static readonly Dictionary<string, int> _learned = new(StringComparer.OrdinalIgnoreCase);
    private static bool _learnedDirty;

    // Completions for what's been typed so far, best first: learned words
    // by how often they've been typed, then the built-in list in frequency
    // order. Never the prefix itself — there's nothing to complete.
    public static IReadOnlyList<string> Suggest(string prefix)
    {
        EnsureLoaded();
        prefix = prefix.ToLowerInvariant();
        if (prefix.Length == 0)
            return Array.Empty<string>();

        var results = new List<string>(MaxSuggestions);

        foreach (var (word, _) in _learned.OrderByDescending(entry => entry.Value))
        {
            if (results.Count >= MaxSuggestions)
                break;
            if (word.Length > prefix.Length && word.StartsWith(prefix, StringComparison.Ordinal))
                results.Add(word);
        }

        foreach (var word in _words!)
        {
            if (results.Count >= MaxSuggestions)
                break;
            if (word.Length > prefix.Length && word.StartsWith(prefix, StringComparison.Ordinal) && !results.Contains(word))
                results.Add(word);
        }

        return results;
    }

    // Called once a word is finished (a space, Enter, or punctuation after
    // it, or a suggestion taken). Only real words — letters, three or more
    // — so a stray "asd" doesn't start showing up as a suggestion.
    public static void Learn(string word)
    {
        EnsureLoaded();
        word = word.ToLowerInvariant();
        if (word.Length < 3 || !word.All(char.IsAsciiLetter))
            return;

        _learned[word] = _learned.TryGetValue(word, out int count) ? count + 1 : 1;
        _learnedDirty = true;
    }

    // Written by the keyboard window on close and on a slow timer, not on
    // every word — the file is small, but there's no reason to touch the
    // disk mid-typing.
    public static void Save()
    {
        if (!_learnedDirty)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LearnedPath)!);
            File.WriteAllLines(LearnedPath, _learned.Select(entry => $"{entry.Key}\t{entry.Value}"));
            _learnedDirty = false;
        }
        catch
        {
            // Same stance as Settings: a failed save just means the new
            // words aren't remembered next time.
        }
    }

    private static void EnsureLoaded()
    {
        if (_words != null)
            return;

        _words = LoadBuiltInWords();

        try
        {
            if (File.Exists(LearnedPath))
            {
                foreach (var line in File.ReadAllLines(LearnedPath))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0 && int.TryParse(line[(tab + 1)..], out int count))
                        _learned[line[..tab]] = count;
                }
            }
        }
        catch
        {
            // Unreadable learned-words file — start fresh, the built-in
            // list still works.
        }
    }

    // The list is an embedded resource; its manifest name is derived from
    // the project's root namespace and folder, so it's looked up by suffix
    // rather than spelled out.
    private static string[] LoadBuiltInWords()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string? resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("words-en.txt", StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
            return Array.Empty<string>();

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return Array.Empty<string>();

        using var reader = new StreamReader(stream);
        var words = new List<string>(20000);
        while (reader.ReadLine() is string line)
        {
            line = line.Trim();
            if (line.Length > 0)
                words.Add(line);
        }
        return words.ToArray();
    }
}
