using System.IO;
using System.Text.Json;

namespace UnboundKeys.Tests;

// Settings and profiles, checked against a scratch folder (see
// Settings.UseScratchFile): the real settings file is never touched, so
// these run with the app open. Each section starts from no file at all
// unless it says otherwise.
internal static class SettingsTests
{
    private static string _file = "";
    private static string _backup = "";
    private static string _log = "";
    private static bool _ok;

    // scratchDir is the folder Program.cs pointed Settings and Log at.
    public static bool Run(string scratchDir)
    {
        _file = Path.Combine(scratchDir, "settings.json");
        _backup = _file + ".bak";
        _log = Path.Combine(scratchDir, "log.txt");
        _ok = true;

        Section("settings: a first start", FirstStart);
        Section("settings: mappings survive a save", Mappings);
        Section("the log: off unless switched on", LogSwitch);
        Section("the keyboard's learned words: off unless switched on", LearnedWords);
        Section("mouse: a button reset to Not Mapped stays that way after a restart", MouseReset);
        Section("settings: games (profiles)", Games);
        Section("settings: sub-profiles", SubProfiles);
        Section("settings: files from older versions", OlderFiles);
        Section("settings: damaged files", DamagedFiles);
        Section("reset app: everything the app keeps is deleted", ResetApp);
        return _ok;
    }

    private static void FirstStart()
    {
        Check(Same(Settings.LoadProfileNames(), "Default"), "the only game is Default");
        Check(Settings.LoadActiveProfileName() == "Default", "and it is the active one");
        Check(Same(Settings.LoadSubProfileNames("Default"), "Default"), "with one sub-profile, Default");
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x31, "mappings are the app's defaults");
        Check(!Settings.LoadWriteLog(), "the log is off until it is switched on");
        Check(!Settings.LoadRememberTypedWords(), "so is remembering typed words");
        Check(!File.Exists(_file), "reading alone writes nothing to disk");
    }

    // The log is the user's choice (Settings > Action logger): off writes nothing
    // at all, errors and crashes included; on writes. Left on at the end,
    // as the damaged-file checks further down read the log.
    private static void LogSwitch()
    {
        Settings.SaveWriteLog(true);
        Check(Settings.LoadWriteLog(), "the switch is saved on");
        Settings.SaveWriteLog(false);
        Check(!Settings.LoadWriteLog(), "and off again");

        Log.Enabled = false;
        long before = File.Exists(_log) ? new FileInfo(_log).Length : 0;
        Log.Info("a line while off");
        Log.Error("an error while off");
        Log.Crash("a crash while off", new InvalidOperationException("test"));
        long after = File.Exists(_log) ? new FileInfo(_log).Length : 0;
        Check(after == before, "switched off, nothing is written, errors and crashes included");

        Log.Enabled = true;
        Log.Info("a line while on");
        Check(File.Exists(_log) && File.ReadAllText(_log).Contains("a line while on"), "switched on, a line is written");
    }

    // "Reset this mapping" on a mouse button used to save before switching
    // the button off, so the file kept it mapped with no key; after the
    // next start its click was swallowed and nothing sent (Fizzil's right
    // click, dead after every restart). The file is what a restart reads,
    // so it is what these check; and a file already written that way must
    // load as unmapped.
    private static void MouseReset()
    {
        Dictionary<string, bool> AllOff()
        {
            var map = new Dictionary<string, bool>();
            foreach (var id in MouseMap.ButtonIds)
                map[id] = false;
            return map;
        }
        bool SavedAsMapped(string id) => Settings.LoadMouseEnabled("Default", AllOff())[id];

        MouseMap.SwitchProfile("Default");
        MouseMap.Rebind("right", 0x41);
        Check(MouseMap.Enabled["right"] && SavedAsMapped("right"), "a key picked for the right button saves it as mapped");

        MouseMap.ResetToDefault("right");
        Check(!MouseMap.Enabled["right"], "reset, it is unmapped in memory");
        Check(!SavedAsMapped("right"), "and unmapped in the file, so it still is after a restart");

        var enabled = AllOff();
        enabled["right"] = true;
        var keys = new Dictionary<string, ushort>();
        var extras = new Dictionary<string, List<ushort>>();
        var behaviors = new Dictionary<string, KeyBehavior>();
        foreach (var id in MouseMap.ButtonIds)
        {
            keys[id] = 0;
            extras[id] = new List<ushort>();
            behaviors[id] = new KeyBehavior();
        }
        Settings.SaveMouseProfile("Default", enabled, keys, extras, behaviors);
        MouseMap.SwitchProfile("Default");
        Check(SavedAsMapped("right") && !MouseMap.Enabled["right"], "a file left mapped with no key by the old reset loads as unmapped");
    }

    // Reset app (Settings > Reset) deletes the settings with their backup
    // and the log (the kept words are ClearLearned, checked above), and
    // leaves a fresh default settings file rather than none, so the old
    // VoicePress settings are not imported again on the next start.
    private static void ResetApp()
    {
        Settings.SaveProfile("Default", new() { ["one"] = 0x41 }, new(), new());
        Settings.SaveKeyboardScale(0.8); // a second save, so there is a backup too
        Settings.SaveWriteLog(true);
        Check(File.Exists(_file) && File.Exists(_backup), "before it, a settings file and its backup");

        Settings.DeleteEverything();
        Check(!File.Exists(_backup), "the backup is deleted");
        Check(File.Exists(_file), "a fresh settings file is left in place of the old one");
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x31 && !Settings.LoadWriteLog() && Same(Settings.LoadProfileNames(), "Default"),
            "and it reads as a fresh install: default mappings, one game, the switches off");

        Log.Info("a line before the reset");
        Log.DeleteFiles();
        Check(!File.Exists(_log) && !Log.Enabled, "the log is deleted and logging is off");
        Log.Enabled = true; // the suites after this one may log
    }

    // The words typed on the on-screen keyboard are the user's to keep or
    // not (Keyboard page > Smart predictive text): off, nothing is learned,
    // saved or suggested; Clear the list deletes what was kept. On a
    // scratch file, never the real list; a made-up word, so the built-in
    // list cannot supply it.
    private static void LearnedWords()
    {
        const string word = "qwxzvord";
        string file = Path.Combine(Path.GetDirectoryName(_file)!, "learned-words.txt");
        WordPredictor.UseScratchFile(file);
        bool Suggested() => string.Join(",", WordPredictor.Suggest("qwxz")).Contains(word);

        Settings.SaveRememberTypedWords(true);
        Check(Settings.LoadRememberTypedWords(), "the switch is saved on");
        Settings.SaveRememberTypedWords(false);
        Check(!Settings.LoadRememberTypedWords(), "and off again");

        WordPredictor.Remember = false;
        WordPredictor.Learn(word);
        WordPredictor.Save();
        Check(!File.Exists(file), "switched off, a typed word is not kept");
        Check(!Suggested(), "nor suggested");

        WordPredictor.Remember = true;
        WordPredictor.Learn(word);
        WordPredictor.Save();
        Check(File.Exists(file) && File.ReadAllText(file).Contains(word), "switched on, it is kept");
        var first = WordPredictor.Suggest("qwxz");
        Check(first.Count > 0 && first[0] == word, "and comes first in the suggestions");
        Check(WordPredictor.LearnedCount == 1, "the count says one word");

        WordPredictor.Remember = false;
        Check(!Suggested(), "switched off again, it is no longer suggested");
        Check(File.Exists(file) && WordPredictor.LearnedCount == 1, "but stays on disk, and counted, until cleared");

        WordPredictor.ClearLearned();
        Check(!File.Exists(file) && WordPredictor.LearnedCount == 0, "Clear the list deletes the file and the words");
        WordPredictor.Remember = true;
        Check(!Suggested(), "and they do not come back when switched on again");

        // The list is the user's to edit (Open the list shows it in a text
        // editor): the file is the truth, and a line can be just a word.
        Check(WordPredictor.EnsureListFile() == file && File.Exists(file), "Open the list makes an empty file to add to when there is none");
        File.WriteAllText(file, "handmadeqz\r\n" + word + "\t5\r\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5)); // a later save, whatever the clock's resolution
        Check(WordPredictor.LearnedCount == 2, "an edit made by hand is picked up while the app runs");
        var handmade = WordPredictor.Suggest("handm");
        Check(handmade.Count > 0 && handmade[0] == "handmadeqz", "a word added on its own line is suggested");
        WordPredictor.Learn("anotherqzword");
        WordPredictor.Save();
        string saved = File.ReadAllText(file);
        Check(saved.Contains("handmadeqz\t1") && saved.Contains(word + "\t5") && saved.Contains("anotherqzword"), "and a later save keeps the hand-made words beside the new one");

        WordPredictor.ClearLearned();
        WordPredictor.Remember = false;
    }

    private static void Mappings()
    {
        var behavior = new KeyBehavior { Repeat = true, Infinite = true, Rotation = true, Priority = true, PrioritySeconds = 1.5, DurationSeconds = 2.5 };
        Settings.SaveProfile("Default",
            new() { ["one"] = 0x41, ["ghost"] = 0x5A },
            new() { ["one"] = new() { 0x42, 0x43 } },
            new() { ["one"] = behavior });

        var map = Settings.LoadKeyMap("Default", Words());
        Check(map["one"] == 0x41, "a saved key loads again");
        Check(map["two"] == 0x32, "a key that was never saved keeps its default");
        Check(!map.ContainsKey("ghost"), "an id the app does not know is ignored");

        var extras = Settings.LoadExtraKeys("Default", new() { ["one"] = new(), ["two"] = new() });
        Check(extras["one"].SequenceEqual(new ushort[] { 0x42, 0x43 }), "extra keys load again, in order");

        var loaded = Settings.LoadBehaviors("Default", new() { ["one"] = new KeyBehavior(), ["two"] = new KeyBehavior() })["one"];
        Check(loaded.Repeat && loaded.Infinite && loaded.Rotation && loaded.Priority && !loaded.Hold
              && loaded.PrioritySeconds == 1.5 && loaded.DurationSeconds == 2.5,
              "every field of a behavior loads again (Repeat, Infinite, Rotation, the pause and both times)");

        Check(new KeyBehavior().IsPlainTap() && new KeyBehavior { DurationSeconds = 3 }.IsPlainTap()
              && !new KeyBehavior { Priority = true }.IsPlainTap() && !loaded.IsPlainTap(),
              "a fresh behavior is a plain tap; one with a pause, a repeat or a hold is not");

        Settings.SaveMouseProfile("Default", new() { ["middle"] = true }, new() { ["middle"] = 0x46 }, new(), new());
        Settings.SaveVirtualProfile("Default", new() { ["f1"] = 0x47 }, new(), new());
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x41
              && Settings.LoadMouseKeyMap("Default", new() { ["middle"] = 0 })["middle"] == 0x46
              && Settings.LoadMouseEnabled("Default", new() { ["middle"] = false })["middle"]
              && Settings.LoadVirtualKeyMap("Default", new() { ["f1"] = 0 })["f1"] == 0x47,
              "saving voice, mouse or keyboard mappings leaves the other two alone");

        string before = File.ReadAllText(_file);
        Settings.SaveKeyboardShown(true);
        Check(File.Exists(_backup) && File.ReadAllText(_backup) == before, "a save keeps the file as it was as settings.json.bak");
        Check(!File.Exists(_file + ".tmp"), "and leaves no temporary file behind");
        Check(Settings.LoadKeyboardShown(), "and the new value loads again");
    }

    private static void Games()
    {
        Settings.CreateProfileIfMissing("WoW", new() { ["one"] = 0x41 }, new(), new(), new(), new(), new(), new());
        Settings.CreateProfileIfMissing("Diablo", new() { ["one"] = 0x44 }, new(), new(), new(), new(), new(), new());
        Settings.CreateProfileIfMissing("Halo", new() { ["one"] = 0x48 }, new(), new(), new(), new(), new(), new());
        Check(Same(Settings.LoadProfileNames(), "Default", "WoW", "Diablo", "Halo"), "new games are listed in the order they were made, after Default");

        Settings.CreateProfileIfMissing("WoW", new() { ["one"] = 0x5A }, new(), new(), new(), new(), new(), new());
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == 0x41, "creating a game that already exists changes nothing");

        Settings.SetActiveProfile("Diablo");
        Check(Settings.LoadActiveProfileName() == "Diablo", "the active game is remembered");

        Check(!Settings.RenameProfile("Default", "Base"), "Default cannot be renamed");
        Check(!Settings.RenameProfile("Diablo", "wow"), "a name another game has is refused, whatever the capitals");
        Check(!Settings.RenameProfile("Diablo", "   "), "an empty name is refused");
        Check(!Settings.RenameProfile("Nope", "Thing"), "a game that does not exist cannot be renamed");
        Check(Settings.RenameProfile("Diablo", "Diablo 4"), "a free name is accepted");
        Check(Same(Settings.LoadProfileNames(), "Default", "WoW", "Diablo 4", "Halo"), "the renamed game keeps its place in the list");
        Check(Settings.LoadActiveProfileName() == "Diablo 4", "and stays the active game");
        Check(Settings.LoadKeyMap("Diablo 4", Words())["one"] == 0x44, "and keeps its mappings");

        Settings.DeleteProfile("Diablo 4");
        Check(Same(Settings.LoadProfileNames(), "Default", "WoW", "Halo"), "a deleted game is gone");
        Check(Settings.LoadActiveProfileName() == "Default", "and deleting the active game falls back to Default");
    }

    private static void SubProfiles()
    {
        Settings.SaveProfile("WoW", new() { ["one"] = 0x41 }, new(), new());
        Settings.SaveThemeColor("WoW", "Green");

        Check(Settings.CreateSubProfile("WoW", "Hunter"), "a sub-profile can be added");
        Check(!Settings.CreateSubProfile("WoW", "hunter"), "the same name again is refused, whatever the capitals");
        Check(!Settings.CreateSubProfile("WoW", " "), "an empty name is refused");
        Check(Same(Settings.LoadSubProfileNames("WoW"), "Default", "Hunter"), "it is listed after the first");
        Check(Settings.LoadActiveSubProfile("WoW") == "Default", "adding one does not switch to it");

        Check(!Settings.SetActiveSubProfile("WoW", "Mage"), "switching to one that does not exist is refused");
        Check(Settings.SetActiveSubProfile("WoW", "Hunter"), "switching to the new one works");
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == 0x41, "a new sub-profile starts as a copy of the one it was made from");

        Settings.SaveProfile("WoW", new() { ["one"] = 0x48 }, new(), new());
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == 0x48, "a change saved on Hunter loads on Hunter");
        Settings.SetActiveSubProfile("WoW", "Default");
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == 0x41, "and leaves Default as it was");
        Check(Settings.LoadThemeColor("WoW", "Red") == "Green", "the colour belongs to the game, not the sub-profile");

        Settings.CreateSubProfile("WoW", "Mage");
        Check(Settings.RenameSubProfile("WoW", "Hunter", "Marksman"), "a sub-profile can be renamed");
        Check(Same(Settings.LoadSubProfileNames("WoW"), "Default", "Marksman", "Mage"), "and keeps its place in the list");
        Check(!Settings.RenameSubProfile("WoW", "Marksman", "MAGE"), "a name the game already uses is refused");

        Settings.SetActiveSubProfile("WoW", "Marksman");
        Settings.RenameSubProfile("WoW", "Marksman", "Hunter");
        Check(Settings.LoadActiveSubProfile("WoW") == "Hunter", "renaming the active one keeps it active");
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == 0x48, "with its mappings");

        Check(Settings.DeleteSubProfile("WoW", "Hunter"), "the active sub-profile can be deleted");
        Check(Settings.LoadActiveSubProfile("WoW") == "Default", "and the game moves to its first remaining one");
        Check(Settings.DeleteSubProfile("WoW", "Mage"), "down to one");
        Check(!Settings.DeleteSubProfile("WoW", "Default"), "but a game's last sub-profile cannot be deleted");
    }

    private static void OlderFiles()
    {
        // Before profiles: one flat key map.
        File.WriteAllText(_file, """{"KeyMap":{"one":65},"Behaviors":{"one":{"Repeat":true,"DurationSeconds":3}}}""");
        var old = Settings.LoadBehaviors("Default", new() { ["one"] = new KeyBehavior() })["one"];
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 65 && old.Repeat && old.DurationSeconds == 3,
              "a file from before profiles becomes the Default game");
        Check(Same(Settings.LoadSubProfileNames("Default"), "Default"), "with one sub-profile");

        // Before sub-profiles: the mappings sat on the game itself.
        Fresh();
        File.WriteAllText(_file, """{"ActiveProfile":"WoW","Profiles":{"WoW":{"ThemeColor":"Green","KeyMap":{"one":66},"MouseEnabled":{"middle":true},"MouseKeyMap":{"middle":67}}}}""");
        Check(Settings.LoadActiveProfileName() == "WoW" && Settings.LoadThemeColor("WoW", "Red") == "Green",
              "a file from before sub-profiles keeps its game and colour");
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == 66
              && Settings.LoadMouseKeyMap("WoW", new() { ["middle"] = 0 })["middle"] == 67,
              "and its mappings, as the game's Default sub-profile");

        Settings.SaveKeyboardShown(false);
        using (var doc = JsonDocument.Parse(File.ReadAllText(_file)))
        {
            var wow = doc.RootElement.GetProperty("Profiles").GetProperty("WoW");
            Check(!wow.TryGetProperty("KeyMap", out _), "the next save drops the old flat fields");
            Check(wow.GetProperty("SubProfiles").GetProperty("Default").GetProperty("KeyMap").GetProperty("one").GetInt32() == 66,
                  "and writes the mappings in the new place");
        }

        // A file that mentions things this version has never heard of (a
        // removed feature, or a newer version's settings opened by an older one).
        Fresh();
        File.WriteAllText(_file, """{"Mystery":12,"Profiles":{"Default":{"Mystery":true,"SubProfiles":{"Default":{"KeyMap":{"one":68},"Behaviors":{"one":{"Repeat":true,"Infinite":true,"Mystery":250}}}}}}}""");
        var known = Settings.LoadBehaviors("Default", new() { ["one"] = new KeyBehavior() })["one"];
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 68 && known.Repeat && known.Infinite,
              "fields this version does not know are ignored, and the rest still loads");
    }

    private static void DamagedFiles()
    {
        Settings.SaveProfile("Default", new() { ["one"] = 0x41 }, new(), new());
        Settings.SaveKeyboardShown(true);

        File.WriteAllText(_file, "{ \"Profiles\": { \"Defa");
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x41, "a file cut short mid-save falls back to the backup");
        Check(File.Exists(_log) && File.ReadAllText(_log).Contains("settings.json could not be read"), "and says so in the log");

        Settings.SaveKeyboardScale(0.8);
        Check(Parses(_file) && Settings.LoadKeyMap("Default", Words())["one"] == 0x41, "the next save writes a whole file again, mappings intact");

        // One more save, so the backup is a good file again.
        Settings.SaveKeyboardScale(0.9);
        File.WriteAllText(_file, "");
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x41, "an empty file falls back to the backup");
        File.WriteAllText(_file, "null");
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x41, "so does a file that holds nothing");

        File.WriteAllText(_file, "not json");
        File.WriteAllText(_backup, "not json either");
        Check(Settings.LoadKeyMap("Default", Words())["one"] == 0x31 && Same(Settings.LoadProfileNames(), "Default"),
              "with both files damaged the app starts fresh rather than crashing");

        Settings.SaveKeyboardScale(0.7);
        Check(Parses(_file) && Settings.LoadKeyboardScale() == 0.7, "and can save again from there");

        // Another save makes the backup a good file (scale 0.7) again.
        Settings.SaveKeyboardScale(0.75);
        File.Delete(_file);
        Check(Settings.LoadKeyboardScale() == 0.65, "a missing file is a fresh start; the backup is not brought back on its own");
    }

    // The defaults a caller hands to Load*: fresh each time, since Load*
    // fills in the dictionary it is given.
    private static Dictionary<string, ushort> Words() => new() { ["one"] = 0x31, ["two"] = 0x32 };

    private static bool Same(List<string> actual, params string[] expected) => actual.SequenceEqual(expected);

    private static bool Parses(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Fresh()
    {
        File.Delete(_file);
        File.Delete(_backup);
        File.Delete(_file + ".tmp");
        File.Delete(_log);
    }

    // An exception inside a section is a failure of that section, not a
    // crash of the whole run.
    private static void Section(string name, Action body)
    {
        Console.WriteLine($"--- {name} ---");
        Fresh();
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Check(false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        _ok &= condition;
    }
}
