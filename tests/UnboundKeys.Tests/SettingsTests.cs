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
        Section("steering: the stretch is the app's, not the profile's", SteeringStretch);
        Section("steering: the pushes are the app's, not the profile's", SteeringPushes);
        Section("features: Steering is off on a new install, on for an older file", SteeringFeature);
        Section("settings: games (profiles)", Games);
        Section("settings: sub-profiles", SubProfiles);
        Section("sharing: a profile goes out as a file and comes back whole", ShareRoundTrip);
        Section("sharing: files that are not profiles are refused", ShareRefusals);
        Section("sharing: files with holes in them still come in", ShareHoles);
        Section("sharing: the files found for the import list", ShareFind);
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

    // The log is the user's choice (Settings > Log what I do): off writes
    // no ordinary lines; errors and crashes are written either way (the
    // 2026-11-07 review: a fault with the switch off left no trace); on
    // writes everything. Left on at the end, as the damaged-file checks
    // further down read the log.
    private static void LogSwitch()
    {
        Settings.SaveWriteLog(true);
        Check(Settings.LoadWriteLog(), "the switch is saved on");
        Settings.SaveWriteLog(false);
        Check(!Settings.LoadWriteLog(), "and off again");

        Log.Enabled = false;
        long before = File.Exists(_log) ? new FileInfo(_log).Length : 0;
        Log.Info("a line while off");
        long afterInfo = File.Exists(_log) ? new FileInfo(_log).Length : 0;
        Check(afterInfo == before, "switched off, an ordinary line is not written");
        Log.Error("an error while off");
        Log.Crash("a crash while off", new InvalidOperationException("test"));
        string text = File.Exists(_log) ? File.ReadAllText(_log) : "";
        Check(text.Contains("an error while off") && text.Contains("a crash while off"), "but an error and a crash are, so a fault leaves a trace");

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

    // The steering stretch (see Steering.cs) is app-wide: one number, with
    // the app's default until it is saved, the same whichever profile is
    // active, and untouched by a save of a profile's mouse mappings.
    private static void SteeringStretch()
    {
        Check(Settings.LoadSteeringStretch() == Steering.DefaultStretch, "never saved reads the default");

        Settings.SaveMouseProfile("Default",
            new() { ["middle"] = true },
            new() { ["middle"] = 0x26 },
            new() { ["middle"] = new() },
            new() { ["middle"] = new KeyBehavior { Hold = true, Infinite = true } });
        Settings.SaveSteeringStretch(0.25);
        Check(Settings.LoadSteeringStretch() == 0.25, "a saved stretch loads again");
        Check(Settings.LoadMouseKeyMap("Default", new() { ["middle"] = 0 })["middle"] == 0x26, "saving it keeps the mouse mappings");

        Settings.SaveMouseProfile("Default",
            new() { ["middle"] = true },
            new() { ["middle"] = 0x28 },
            new() { ["middle"] = new() },
            new() { ["middle"] = new KeyBehavior() });
        Check(Settings.LoadSteeringStretch() == 0.25, "and saving the mouse mappings keeps the stretch");

        Settings.CreateProfileIfMissing("Racing", new(), new(), new(), new(), new(), new(), new());
        Settings.SetActiveProfile("Racing");
        Check(Settings.LoadSteeringStretch() == 0.25, "it is the same whatever profile is active");
    }

    // Settings > Features: a new install starts with Steering off; a
    // settings file from before the switch existed keeps it on, since its
    // owner had the feature all along; the choice is saved either way.
    private static void SteeringFeature()
    {
        Check(!Settings.LoadSteeringFeature(), "no settings file: Steering is off");
        Settings.SaveKeyboardScale(0.8);
        Check(!Settings.LoadSteeringFeature(), "and still off once the first save has written the file");

        Settings.SaveSteeringFeature(true);
        Check(Settings.LoadSteeringFeature(), "switched on, it is saved on");
        Settings.SaveSteeringFeature(false);
        Check(!Settings.LoadSteeringFeature(), "and off again");

        Fresh();
        File.WriteAllText(_file, """{"Profiles":{"Default":{"SubProfiles":{"Default":{"KeyMap":{"one":68}}}}}}""");
        Check(Settings.LoadSteeringFeature(), "a file from before the switch reads on");
        Settings.SaveKeyboardScale(0.8);
        Check(Settings.LoadSteeringFeature(), "and stays on through a save");

        Settings.DeleteEverything();
        Check(!Settings.LoadSteeringFeature(), "Reset app puts it back to off, as on a new install");
    }

    // Push up and Push down (the Steering page) are app-wide as well: a
    // mapping made in one profile is there in every other, and neither a
    // profile's mouse save nor Reset profile's MouseMap.ResetAll touches it.
    private static void SteeringPushes()
    {
        var enabled = Settings.LoadSteeringEnabled(SteeringMap.FreshEnabled());
        Check(!enabled["pushup"] && !enabled["pushdown"], "never saved: both pushes are unmapped");

        SteeringMap.Rebind("pushup", 0x57);
        SteeringMap.SetBehavior("pushup", new KeyBehavior { Hold = true, Infinite = true });
        SteeringMap.AddExtraKey("pushup", 0x45);
        Settings.CreateProfileIfMissing("Browsing", new(), new(), new(), new(), new(), new(), new());
        Settings.SetActiveProfile("Browsing");
        enabled = Settings.LoadSteeringEnabled(SteeringMap.FreshEnabled());
        Check(enabled["pushup"] && Settings.LoadSteeringKeyMap(SteeringMap.FreshWords())["pushup"] == 0x57,
            "a Push up mapping still loads after another profile is made active");
        Check(Settings.LoadSteeringExtraKeys(SteeringMap.FreshExtraKeys())["pushup"].SequenceEqual(new ushort[] { 0x45 }), "with its extra key");
        Check(Settings.LoadSteeringBehaviors(SteeringMap.FreshBehaviors())["pushup"].Hold, "and its behavior");

        Settings.SaveMouseProfile("Browsing",
            new() { ["middle"] = true }, new() { ["middle"] = 0x26 }, new() { ["middle"] = new() }, new() { ["middle"] = new KeyBehavior() });
        Check(Settings.LoadSteeringEnabled(SteeringMap.FreshEnabled())["pushup"], "a mouse profile save leaves it alone");

        MouseMap.SwitchProfile("Browsing");
        MouseMap.ResetAll();
        Check(Settings.LoadSteeringEnabled(SteeringMap.FreshEnabled())["pushup"]
              && Settings.LoadSteeringKeyMap(SteeringMap.FreshWords())["pushup"] == 0x57, "Reset profile's MouseMap.ResetAll leaves it alone");
        Check(!MouseMap.ButtonIds.Contains("pushup") && !MouseMap.ButtonIds.Contains("pushdown"), "the mouse map no longer carries the pushes");

        SteeringMap.ResetToDefault("pushup");
        Check(!SteeringMap.Enabled["pushup"] && !Settings.LoadSteeringEnabled(SteeringMap.FreshEnabled())["pushup"], "a push reset to Not Mapped stays so in the file");
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
    // Sharing (Settings > Profiles > Share / Import profile): one game with
    // all its sub-profiles as a .unboundkeys file. The files go to a folder
    // beside the scratch settings file, never to Documents or Downloads.
    private static string ShareFolder()
    {
        string folder = Path.Combine(Path.GetDirectoryName(_file)!, "shared");
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private const string ShareHead = "{\"UnboundKeysProfile\":1,";

    private static void ShareRoundTrip()
    {
        string folder = ShareFolder();
        const string game = "xcom2: WotC";
        Settings.CreateProfileIfMissing(game, new() { ["one"] = 0x41 }, new(), new(), new(), new(), new(), new());
        Settings.SaveThemeColor(game, "Green");
        Settings.CreateSubProfile(game, "Sniper");
        Settings.SetActiveSubProfile(game, "Sniper");
        Settings.SaveProfile(game, new() { ["one"] = 0x48 }, new(), new() { ["one"] = new KeyBehavior { Hold = true, DurationSeconds = 2 } });
        Settings.SaveSteeringStretch(0.25);

        Check(Settings.ExportProfile("Nope", folder) == null, "a game that does not exist cannot be shared");

        string? path = Settings.ExportProfile(game, folder);
        Check(path != null && File.Exists(path), "sharing writes a file");
        if (path == null)
            return;
        Check(Path.GetFileName(path) == "xcom2_ WotC.unboundkeys", "named after the game, without the characters Windows forbids");
        Check(Settings.SharedProfileName(path) == game, "the real name is kept inside the file");
        string text = File.ReadAllText(path);
        Check(!text.Contains("SteeringStretch") && !text.Contains("SteeringKeyMap"), "steering's settings stay behind");
        Check(!text.Contains("KeyboardScale") && !text.Contains("StartWithWindows"), "and so does everything else that is the app's");
        Check(Same(Settings.LoadProfileNames(), "Default", game), "sharing changes nothing here");

        string? name = Settings.ImportProfile(path, out _);
        Check(name == game + " 2", "a name already here gets a 2 and nothing is overwritten");
        Check(Same(Settings.LoadProfileNames(), "Default", game, game + " 2"), "the imported game is listed last");
        Check(Settings.LoadActiveProfileName() == "Default", "importing does not switch to it");
        if (name == null)
            return;
        Check(Same(Settings.LoadSubProfileNames(name), "Default", "Sniper"), "its sub-profiles come with it");
        Check(Settings.LoadActiveSubProfile(name) == "Sniper", "on the one it was last on");
        Check(Settings.LoadKeyMap(name, Words())["one"] == 0x48, "with that one's mappings");
        var behavior = Settings.LoadBehaviors(name, new() { ["one"] = new KeyBehavior() })["one"];
        Check(behavior.Hold && behavior.DurationSeconds == 2, "and how each key is pressed");
        Settings.SetActiveSubProfile(name, "Default");
        Check(Settings.LoadKeyMap(name, Words())["one"] == 0x41, "the other sub-profile kept its own mappings");
        Check(Settings.LoadThemeColor(name, "Red") == "Green", "and the colour came too");
        Check(Settings.LoadKeyMap(game, Words())["one"] == 0x48, "the game it was shared from is untouched");

        Check(Settings.ImportProfile(path, out _) == game + " 3", "importing again gives a 3");
        Settings.DeleteProfile(game);
        Check(Settings.ImportProfile(path, out _) == game, "and with the name free, it keeps its own name");

        Fresh();
        string? fresh = Settings.ExportProfile("Default", folder);
        Check(fresh != null && Settings.ImportProfile(fresh, out _) == "Default 2", "Default can be shared, even before anything was saved");
        Check(Settings.LoadKeyMap("Default 2", Words())["one"] == 0x31, "and reads the app's defaults");
    }

    private static void ShareRefusals()
    {
        string folder = ShareFolder();
        string Make(string name, string contents)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, contents);
            return path;
        }
        bool Refused(string path)
        {
            string? name = Settings.ImportProfile(path, out string problem);
            return name == null && problem.Length > 0 && Same(Settings.LoadProfileNames(), "Default");
        }

        Check(Refused(Path.Combine(folder, "missing.unboundkeys")), "a file that is not there");
        Check(Refused(Make("junk.unboundkeys", "this is not a profile")), "a file of junk");
        Check(Refused(Make("empty.unboundkeys", "{}")), "an empty object");
        Check(Refused(Make("list.unboundkeys", "[1,2,3]")), "a list");
        Check(Refused(Make("noname.unboundkeys", ShareHead + "\"Name\":\"  \",\"Profile\":{}}")), "a profile with no name");
        Check(Refused(Make("nobody.unboundkeys", ShareHead + "\"Name\":\"Ghost\"}")), "a name with no profile");
        Check(Refused(Make("huge.unboundkeys", ShareHead + "\"Name\":\"Big\",\"Profile\":{},\"Pad\":\"" + new string('x', 2_000_000) + "\"}")), "a file far too large to be a profile");

        string subs = string.Join(",", Enumerable.Range(1, 11).Select(n => "\"S" + n + "\":{}"));
        Check(Refused(Make("many.unboundkeys", ShareHead + "\"Name\":\"Many\",\"Profile\":{\"SubProfiles\":{" + subs + "}}}")), "more sub-profiles than a game can hold");

        Settings.SaveWriteLog(true);
        Check(Refused(Make("settings.unboundkeys", File.ReadAllText(_file))), "the app's own settings file");
        Check(Settings.SharedProfileName(Make("junk2.unboundkeys", "nope")) == null, "and junk has no profile name to show");

        string good = Make("good.unboundkeys", ShareHead + "\"Name\":\"Good\",\"Profile\":{}}");
        for (int n = 1; n < Settings.MaxProfiles; n++)
            Settings.CreateProfileIfMissing($"Game {n}", new(), new(), new(), new(), new(), new(), new());
        string? full = Settings.ImportProfile(good, out string why);
        Check(full == null && why.Contains("ten"), "with ten profiles already, there is no room, and it says so");
        Settings.DeleteProfile("Game 1");
        Check(Settings.ImportProfile(good, out _) == "Good", "with one deleted, it comes in");
    }

    // A file somebody edited by hand, or from a version that saved less:
    // whatever is missing reads as the app's defaults.
    private static void ShareHoles()
    {
        string folder = ShareFolder();
        string path = Path.Combine(folder, "holes.unboundkeys");

        File.WriteAllText(path, ShareHead + "\"Name\":\"Bare\",\"Profile\":{}}");
        Check(Settings.ImportProfile(path, out _) == "Bare", "a profile with nothing in it comes in");
        Check(Same(Settings.LoadSubProfileNames("Bare"), "Default"), "with one sub-profile, Default");
        Check(Settings.LoadKeyMap("Bare", Words())["one"] == 0x31, "at the app's defaults");

        File.WriteAllText(path, ShareHead + "\"Name\":\"Nulls\",\"Profile\":{\"ThemeColor\":null,\"ActiveSubProfile\":\"Gone\",\"SubProfiles\":{"
            + "\"A\":{\"KeyMap\":null,\"Behaviors\":{\"one\":null},\"ExtraKeys\":{\"one\":[1,2,3,4,5,6,7,8,9]}},\"B\":null}}}");
        Check(Settings.ImportProfile(path, out _) == "Nulls", "nulls where mappings should be are tolerated");
        Check(Same(Settings.LoadSubProfileNames("Nulls"), "A", "B"), "both sub-profiles are there");
        Check(Settings.LoadActiveSubProfile("Nulls") == "A", "an active sub-profile that does not exist falls back to the first");
        Check(Settings.LoadKeyMap("Nulls", Words())["one"] == 0x31, "a missing key map reads the defaults");
        Check(Settings.LoadBehaviors("Nulls", new() { ["one"] = new KeyBehavior() })["one"] != null, "a null behaviour is dropped, not loaded");
        Check(Settings.LoadExtraKeys("Nulls", new() { ["one"] = new() })["one"].Count == RemapStore.MaxExtraKeys, "extra keys are cut to what a mapping can hold");
        Check(Settings.LoadThemeColor("Nulls", "Red") == "Red", "and no colour means the default colour");

        File.WriteAllText(path, ShareHead + "\"Name\":\"A name that is much too long to fit\",\"Profile\":{\"ThemeColor\":\"Purple\"}}");
        Check(Settings.ImportProfile(path, out _) == "A name that is much", "a name longer than the rename keyboard allows is cut to fit");
        Check(Settings.LoadThemeColor("A name that is much", "Red") == "Red", "and a colour the app does not have means the default colour");
    }

    private static void ShareFind()
    {
        string folder = ShareFolder();
        string other = Path.Combine(folder, "downloads");
        Directory.CreateDirectory(other);
        Settings.CreateProfileIfMissing("WoW", new(), new(), new(), new(), new(), new(), new());
        Settings.CreateProfileIfMissing("Halo", new(), new(), new(), new(), new(), new(), new());

        string wow = Settings.ExportProfile("WoW", folder)!;
        string halo = Settings.ExportProfile("Halo", other)!;
        File.SetLastWriteTimeUtc(wow, DateTime.UtcNow.AddMinutes(-5));
        // A browser's second download of the same file, a junk file with
        // the right ending, and a real profile with the wrong ending.
        string second = Path.Combine(other, "Halo (1).unboundkeys");
        File.Copy(halo, second);
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddMinutes(-1));
        File.WriteAllText(Path.Combine(other, "junk.unboundkeys"), "nope");
        File.Copy(halo, Path.Combine(other, "Halo.txt"));

        var found = ProfileShare.Find(other, folder, Path.Combine(folder, "not-there"));
        Check(found.Count == 3, "only real profile files with the right ending are found, and a missing folder is no trouble");
        Check(found.Count == 3 && found[0].Path == halo && found[2].Path == wow, "newest first");
        Check(found.Count == 3 && found[0].Name == "Halo" && found[2].Name == "WoW", "each with the name inside it");

        for (int n = 0; n < 12; n++)
            File.Copy(halo, Path.Combine(other, $"More {n}.unboundkeys"));
        Check(ProfileShare.Find(other, folder).Count == ProfileShare.MaxListed, "a long list is cut to the newest few");
    }

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
