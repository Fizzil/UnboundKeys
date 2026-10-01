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
// words the user has actually typed, which rank first. Everything is
// offline. The typed words are kept only while Remember is on (the
// Keyboard page's "Remember words I type frequently", off by default; Fizzil: people
// will not be happy with an app recording what they type). Off, nothing
// is learned, saved or suggested from them; ClearLearned deletes what was
// kept.
public static class WordPredictor
{
    public const int MaxSuggestions = 8;

    private static string LearnedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnboundKeys", "learned-words.txt");

    // Set from the saved setting at startup (Program.cs) and by the
    // Keyboard page's switch.
    public static bool Remember { get; set; }

    // For the harness in tests/ only, so it never touches the real list.
    internal static void UseScratchFile(string path)
    {
        LearnedPath = path;
        _words = null;
        _learned.Clear();
        _learnedDirty = false;
        _learnedRead = false;
    }

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

        // The user's own words, only while they asked for them to be
        // remembered: kept words must not surface on screen otherwise.
        if (Remember)
        {
            foreach (var (word, _) in _learned.OrderByDescending(entry => entry.Value))
            {
                if (results.Count >= MaxSuggestions)
                    break;
                if (word.Length > prefix.Length && word.StartsWith(prefix, StringComparison.Ordinal))
                    results.Add(word);
            }
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
        if (!Remember)
            return;
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
        EnsureLoaded(); // a hand edit since the last save wins (see EnsureLoaded)
        if (!_learnedDirty)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LearnedPath)!);
            File.WriteAllLines(LearnedPath, _learned.Select(entry => $"{entry.Key}\t{entry.Value}"));
            _learnedDirty = false;
            _learnedStamp = StampOfFile();
        }
        catch (Exception ex)
        {
            // Same stance as Settings: a failed save just means the new
            // words aren't remembered next time.
            Log.Error("saving learned words", ex);
        }
    }

    // How many words are kept, for the Keyboard page to say so. Counted
    // whether or not Remember is on: what is on disk is what matters.
    public static int LearnedCount
    {
        get
        {
            EnsureLoaded();
            return _learned.Count;
        }
    }

    // Forgets every learned word, in memory and on disk.
    public static void ClearLearned()
    {
        EnsureLoaded();
        _learned.Clear();
        _learnedDirty = false;
        try
        {
            File.Delete(LearnedPath);
        }
        catch (Exception ex)
        {
            Log.Error("clearing learned words", ex);
        }
        _learnedStamp = StampOfFile();
    }

    // The list's file, for the Keyboard page to open in a text editor
    // (Fizzil: to view it, edit it, add to it). What is in memory is
    // written out first, and an empty file is made if there is none, so
    // there is always something to open and add to.
    public static string EnsureListFile()
    {
        Save();
        Directory.CreateDirectory(Path.GetDirectoryName(LearnedPath)!);
        if (!File.Exists(LearnedPath))
            File.WriteAllText(LearnedPath, "");
        return LearnedPath;
    }

    private static bool _learnedRead;
    // The file's last write time when it was last read or written here;
    // MinValue while there is no file.
    private static DateTime _learnedStamp;

    private static DateTime StampOfFile() =>
        File.Exists(LearnedPath) ? File.GetLastWriteTimeUtc(LearnedPath) : DateTime.MinValue;

    // Loads the built-in list once, and the learned words whenever their
    // file has changed since this class last read or wrote it. The file is
    // the user's to edit, so it is the truth: an edited file replaces what
    // is in memory, words learned since the last save included. A line is
    // "word<TAB>count" as Save writes it, or just a word, as someone adding
    // one by hand would type it.
    private static void EnsureLoaded()
    {
        _words ??= LoadBuiltInWords();

        try
        {
            DateTime stamp = StampOfFile();
            if (_learnedRead && stamp == _learnedStamp)
                return;

            _learned.Clear();
            _learnedDirty = false;
            _learnedRead = true;
            _learnedStamp = stamp;
            if (stamp == DateTime.MinValue)
                return;

            foreach (var line in File.ReadAllLines(LearnedPath))
            {
                int tab = line.IndexOf('\t');
                string word = (tab < 0 ? line : line[..tab]).Trim().ToLowerInvariant();
                if (word.Length == 0)
                    continue;
                _learned[word] = tab >= 0 && int.TryParse(line[(tab + 1)..].Trim(), out int count) && count > 0 ? count : 1;
            }
        }
        catch (Exception ex)
        {
            // Unreadable learned-words file — start fresh, the built-in
            // list still works.
            Log.Error("reading learned words", ex);
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
