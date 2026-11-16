using System.IO;
using System.Text.Json;

namespace UnboundKeys.Tests;

// Keys by name (stage 1, step 5): the names themselves, the numbers old
// files hold for them, the one-time upgrade of settings.json with its
// kept copy, shared profiles old and new, and Windows' translator.
internal static class KeyNamesTests
{
    private static bool _ok;
    private static string _dir = "";
    private static string _file = "";

    // The numbers every file up to 4.15.0 used, written out here by hand
    // rather than read from the app, so a slip in the app's list shows.
    private static readonly (string Name, ushort Number)[] Old =
    {
        ("None", 0),
        ("LeftClick", 0x01), ("RightClick", 0x02), ("MiddleClick", 0x04), ("Mouse4", 0x05), ("Mouse5", 0x06),
        ("Backspace", 0x08), ("Tab", 0x09), ("Enter", 0x0D), ("Shift", 0x10), ("Ctrl", 0x11), ("Alt", 0x12),
        ("CapsLock", 0x14), ("Escape", 0x1B), ("Space", 0x20),
        ("PageUp", 0x21), ("PageDown", 0x22), ("End", 0x23), ("Home", 0x24),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28), ("Insert", 0x2D), ("Delete", 0x2E),
        ("Digit0", 0x30), ("Digit1", 0x31), ("Digit5", 0x35), ("Digit9", 0x39),
        ("A", 0x41), ("M", 0x4D), ("W", 0x57), ("Z", 0x5A),
        ("Win", 0x5B), ("RightWin", 0x5C),
        ("F1", 0x70), ("F5", 0x74), ("F12", 0x7B), ("F13", 0x7C), ("F24", 0x87),
        ("Semicolon", 0xBA), ("EqualSign", 0xBB), ("Comma", 0xBC), ("Minus", 0xBD), ("Period", 0xBE), ("Slash", 0xBF),
        ("Backtick", 0xC0), ("OpenBracket", 0xDB), ("Backslash", 0xDC), ("CloseBracket", 0xDD), ("Quote", 0xDE),
        ("Throttle", 0xFF01), ("Brake", 0xFF02),
    };

    public static bool Run(string scratch)
    {
        _ok = true;
        _dir = Path.Combine(scratch, "key-names");
        _file = Path.Combine(_dir, "settings.json");
        try
        {
            Section("key names: every key has one, and old numbers find theirs", Names);
            Section("key names: in a file", InAFile);
            Section("key names: an old settings file is upgraded once, and kept", Upgrade);
            Section("key names: a file already upgraded is left alone", NoUpgradeNeeded);
            Section("key names: shared profiles, old and new", Shared);
            Section("key names: Windows' translator", Windows);
        }
        finally
        {
            Settings.UseScratchFile(Path.Combine(scratch, "settings.json"));
        }
        return _ok;
    }

    private static void Fresh()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        Directory.CreateDirectory(_dir);
        Settings.UseScratchFile(_file);
    }

    private static void Names()
    {
        var all = Enum.GetValues<Key>();
        Check(all.Select(k => k.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Length
              && all.Select(k => (ushort)k).Distinct().Count() == all.Length,
              $"the {all.Length} keys have {all.Length} different names and numbers");
        Check(all.All(k => KeyNames.Parse(k.ToString()) == k), "every key is found again by its name");
        Check(KeyNames.Parse("a") == Key.A && KeyNames.Parse("PAGEUP") == Key.PageUp, "whatever the capitals");
        Check(KeyNames.Parse("Hyperdrive") == Key.None && KeyNames.Parse("") == Key.None && KeyNames.Parse(null) == Key.None,
              "a name the app does not know is no key, never some other key");
        Check(KeyNames.Parse("65") == Key.None && KeyNames.Parse("-1") == Key.None, "and a number in quotes is not a name");

        var wrong = Old.Where(o => !Enum.TryParse<Key>(o.Name, out var k) || (ushort)k != o.Number).Select(o => o.Name).ToList();
        Check(wrong.Count == 0, wrong.Count == 0 ? $"the {Old.Length} old numbers checked are still their keys" : "old numbers that moved: " + string.Join(", ", wrong));
        Check((ushort)Key.Digit3 == 0x33 && (ushort)Key.Q == 0x51 && (ushort)Key.F8 == 0x77, "and so are the ones in between (3, Q, F8)");

        var unnamed = KeyCatalog.Groups.SelectMany(g => g.Keys).Where(e => !KeyNames.HasName(e.Key) || e.Key == Key.None).ToList();
        Check(unnamed.Count == 0, "every key the picker offers has a name");
        Check(KeyCatalog.DisplayNameFor(Key.Up) == "Up Arrow" && KeyCatalog.DisplayNameFor(Key.Semicolon) == "; (semicolon)"
              && KeyCatalog.DisplayNameFor((Key)0x5D) == "Key 0x5D", "and reads as before on screen, a key with no name by its number");
        Check(KeyNames.Digit(7) == Key.Digit7 && KeyNames.Letter('q') == Key.Q && KeyNames.Function(12) == Key.F12, "digits, letters and F keys are found by what is on them");
        Check(KeyMap.DefaultWords["one"] == Key.Digit1 && KeyMap.DefaultWords["nine"] == Key.Digit9 && KeyMap.DefaultWords["ten"] == Key.Digit0,
              "the ten words still default to 1 to 9, then 0");
        Check(VirtualKeyCatalog.Keys.Single(k => k.Id == "v0").DefaultKey == Key.Digit0
              && VirtualKeyCatalog.Keys.Single(k => k.Id == "v5").DefaultKey == Key.Digit5
              && VirtualKeyCatalog.Keys.Single(k => k.Id == "vq").DefaultKey == Key.Q,
              "and the Keyboard page's keys to their own key");
    }

    private static void InAFile()
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, Key> { ["one"] = Key.A, ["two"] = Key.PageDown, ["three"] = Key.None });
        Check(json == """{"one":"A","two":"PageDown","three":"None"}""", "a key is written as its name");
        Check(JsonSerializer.Serialize(new List<Key> { Key.Throttle, (Key)0x5D }) == """["Throttle",93]""",
              "a key with no name is written as the number it came as");

        KeyJsonConverter.SawNumber = false;
        var read = JsonSerializer.Deserialize<List<Key>>("""["B","pagedown","Hyperdrive"]""")!;
        Check(read.SequenceEqual(new[] { Key.B, Key.PageDown, Key.None }) && !KeyJsonConverter.SawNumber,
              "names are read, an unknown one as no key");

        read = JsonSerializer.Deserialize<List<Key>>("[65,38,65281,0]")!;
        Check(read.SequenceEqual(new[] { Key.A, Key.Up, Key.Throttle, Key.None }) && KeyJsonConverter.SawNumber,
              "old numbers are read as their keys, and noticed as old");

        KeyJsonConverter.SawNumber = false;
        read = JsonSerializer.Deserialize<List<Key>>("[93]")!;
        Check(read[0] == (Key)93 && !KeyJsonConverter.SawNumber, "a number with no name stays that number, and is nothing to upgrade");

        bool refused = false;
        try { JsonSerializer.Deserialize<List<Key>>("[70000]"); } catch (JsonException) { refused = true; }
        bool refusedToo = false;
        try { JsonSerializer.Deserialize<List<Key>>("[true]"); } catch (JsonException) { refusedToo = true; }
        Check(refused && refusedToo, "a number no key could have, or something that is neither, is refused as a broken file");
    }

    // A settings file as 4.15.0 wrote it: every kind of mapping, numbers
    // for keys.
    private const string OldFile = """
        {"ActiveProfile":"WoW","Profiles":{"WoW":{"ThemeColor":"Green","ActiveSubProfile":"Hunter","SubProfiles":{
        "Default":{"KeyMap":{"one":49,"two":65},"ExtraKeys":{"two":[17,67]},"Behaviors":{"two":{"Hold":true,"DurationSeconds":2}},
        "MouseEnabled":{"middle":true,"right":false},"MouseKeyMap":{"middle":38,"right":0},"MouseExtraKeys":{"middle":[16]},"MouseBehaviors":{},
        "VirtualKeyMap":{"va":66,"va.double":0},"VirtualExtraKeys":{},"VirtualBehaviors":{}},
        "Hunter":{"KeyMap":{"one":112,"two":93},"ExtraKeys":{},"Behaviors":{},
        "MouseEnabled":{"middle":true},"MouseKeyMap":{"middle":4},"MouseExtraKeys":{},"MouseBehaviors":{},
        "VirtualKeyMap":{},"VirtualExtraKeys":{},"VirtualBehaviors":{}}}}},
        "KeyboardScale":0.8,"SteeringStretch":0.25,
        "SteeringEnabled":{"pushdown":true},"SteeringKeyMap":{"pushdown":65282},"SteeringExtraKeys":{"pushdown":[]},"SteeringBehaviors":{"pushdown":{"Hold":true,"DurationSeconds":10}},
        "SteeringFeature":true,"WriteLog":false}
        """;

    private static string BeforeFile => Path.Combine(_dir, "settings.before-key-names.json");

    private static Dictionary<string, Key> Words() => new() { ["one"] = Key.Digit1, ["two"] = Key.Digit2 };

    private static void Upgrade()
    {
        Fresh();
        File.WriteAllText(_file, OldFile);

        var hunter = Settings.LoadKeyMap("WoW", Words());
        Check(hunter["one"] == Key.F1 && hunter["two"] == (Key)93, "the old file reads: the active sub-profile's keys, one of them a key with no name");
        Check(File.Exists(BeforeFile) && File.ReadAllText(BeforeFile) == OldFile, "the file from before is kept beside it, untouched");

        using (var doc = JsonDocument.Parse(File.ReadAllText(_file)))
        {
            var subs = doc.RootElement.GetProperty("Profiles").GetProperty("WoW").GetProperty("SubProfiles");
            var def = subs.GetProperty("Default");
            Check(def.GetProperty("KeyMap").GetProperty("one").GetString() == "Digit1"
                  && def.GetProperty("KeyMap").GetProperty("two").GetString() == "A"
                  && def.GetProperty("ExtraKeys").GetProperty("two")[0].GetString() == "Ctrl"
                  && def.GetProperty("ExtraKeys").GetProperty("two")[1].GetString() == "C",
                  "settings.json now holds the words' keys by name, extra keys too");
            Check(def.GetProperty("MouseKeyMap").GetProperty("middle").GetString() == "Up"
                  && def.GetProperty("MouseKeyMap").GetProperty("right").GetString() == "None"
                  && def.GetProperty("MouseExtraKeys").GetProperty("middle")[0].GetString() == "Shift"
                  && def.GetProperty("VirtualKeyMap").GetProperty("va").GetString() == "B",
                  "the mouse buttons' and the Keyboard page's as well");
            var hunterSet = subs.GetProperty("Hunter");
            Check(hunterSet.GetProperty("KeyMap").GetProperty("one").GetString() == "F1"
                  && hunterSet.GetProperty("KeyMap").GetProperty("two").GetInt32() == 93
                  && hunterSet.GetProperty("MouseKeyMap").GetProperty("middle").GetString() == "MiddleClick",
                  "every sub-profile, not only the active one; the key with no name keeps its number");
            Check(doc.RootElement.GetProperty("SteeringKeyMap").GetProperty("pushdown").GetString() == "Brake", "and steering's pedals");
            Check(doc.RootElement.GetProperty("ActiveProfile").GetString() == "WoW"
                  && doc.RootElement.GetProperty("KeyboardScale").GetDouble() == 0.8
                  && doc.RootElement.GetProperty("SteeringStretch").GetDouble() == 0.25
                  && doc.RootElement.GetProperty("SteeringFeature").GetBoolean()
                  && doc.RootElement.GetProperty("Profiles").GetProperty("WoW").GetProperty("ThemeColor").GetString() == "Green"
                  && doc.RootElement.GetProperty("Profiles").GetProperty("WoW").GetProperty("ActiveSubProfile").GetString() == "Hunter",
                  "everything that is not a key is as it was");
        }
        Check(File.Exists(_file + ".bak") && File.ReadAllText(_file + ".bak") == OldFile, "the ordinary backup is the old file too");

        // Read again, as the app does at every turn, and through every door.
        Settings.UseScratchFile(_file);
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == Key.F1
              && Settings.LoadMouseKeyMap("WoW", new() { ["middle"] = Key.None })["middle"] == Key.MiddleClick
              && Settings.LoadMouseEnabled("WoW", new() { ["middle"] = false })["middle"],
              "the upgraded file reads the same as the old one did");
        Settings.SetActiveSubProfile("WoW", "Default");
        var extras = Settings.LoadExtraKeys("WoW", new() { ["two"] = new List<Key>() })["two"];
        var behavior = Settings.LoadBehaviors("WoW", new() { ["two"] = new KeyBehavior() })["two"];
        Check(Settings.LoadKeyMap("WoW", Words())["two"] == Key.A && extras.SequenceEqual(new[] { Key.Ctrl, Key.C }) && behavior.Hold && behavior.DurationSeconds == 2,
              "the other sub-profile too: its key, its extra keys in order, how it is pressed");
        Check(Settings.LoadVirtualKeyMap("WoW", new() { ["va"] = Key.A, ["va.double"] = Key.None })["va"] == Key.B
              && Settings.LoadSteeringKeyMap(SteeringMap.FreshWords())["pushdown"] == Key.Brake
              && Settings.LoadSteeringBehaviors(SteeringMap.FreshBehaviors())["pushdown"].DurationSeconds == 10,
              "the Keyboard page's key and steering's brake with its 10 s hold");
        Check(File.ReadAllText(BeforeFile) == OldFile, "and the kept file is still the one from before");

        // An old file turning up again (put back by hand) is upgraded again,
        // but the kept file stays the first one.
        string second = OldFile.Replace("\"one\":112", "\"one\":113");
        File.WriteAllText(_file, second);
        Settings.UseScratchFile(_file);
        Check(Settings.LoadKeyMap("WoW", Words())["one"] == Key.F2 && !File.ReadAllText(_file).Contains("\"one\":113"),
              "an old file put back later is upgraded again");
        Check(File.ReadAllText(BeforeFile) == OldFile, "without replacing the file kept the first time");

        Settings.DeleteEverything();
        Check(!File.Exists(BeforeFile), "Reset app deletes the kept file with the rest");
    }

    private static void NoUpgradeNeeded()
    {
        Fresh();
        Settings.SaveProfile("Default", new() { ["one"] = Key.A }, new() { ["one"] = new() { Key.Shift } }, new());
        Settings.SaveMouseProfile("Default", new() { ["middle"] = true }, new() { ["middle"] = Key.Up }, new(), new());
        string written = File.ReadAllText(_file);
        Check(written.Contains("\"one\":\"A\"") && written.Contains("[\"Shift\"]") && written.Contains("\"middle\":\"Up\""), "a new install saves names from the start");
        Settings.UseScratchFile(_file);
        Check(Settings.LoadKeyMap("Default", Words())["one"] == Key.A && !File.Exists(BeforeFile) && File.ReadAllText(_file) == written,
              "reading it changes nothing and keeps no copy: there is nothing to go back to");
    }

    private static void Shared()
    {
        Fresh();
        Settings.CreateProfileIfMissing("WoW", new() { ["one"] = Key.A }, new() { ["one"] = new() { Key.Ctrl } }, new(), new() { ["middle"] = true }, new() { ["middle"] = Key.Mouse4 }, new(), new());
        string? path = Settings.ExportProfile("WoW", Path.Combine(_dir, "share"));
        string text = path == null ? "" : File.ReadAllText(path);
        Check(text.Contains("\"UnboundKeysProfile\": 2") && text.Contains("\"one\": \"A\"") && text.Contains("\"Ctrl\"") && text.Contains("\"middle\": \"Mouse4\""),
              "a shared profile holds its keys by name, and says it is the new kind of file");
        string? back = path == null ? null : Settings.ImportProfile(path, out _);
        Check(back == "WoW 2" && Settings.LoadKeyMap("WoW 2", Words())["one"] == Key.A
              && Settings.LoadMouseKeyMap("WoW 2", new() { ["middle"] = Key.None })["middle"] == Key.Mouse4, "and imports again");

        // One shared by 4.14 or 4.15: numbers.
        string old = Path.Combine(_dir, "old-share.unboundkeys");
        File.WriteAllText(old, """{"UnboundKeysProfile":1,"Name":"Friend","Profile":{"ThemeColor":"Blue","ActiveSubProfile":"Default","SubProfiles":{"Default":{"KeyMap":{"one":72},"ExtraKeys":{"one":[18,115]},"MouseEnabled":{"right":true},"MouseKeyMap":{"right":32}}}}}""");
        string? friend = Settings.ImportProfile(old, out string problem);
        Check(friend == "Friend" && problem == "", "a profile shared by an older version still imports");
        Check(Settings.LoadKeyMap("Friend", Words())["one"] == Key.H
              && Settings.LoadExtraKeys("Friend", new() { ["one"] = new List<Key>() })["one"].SequenceEqual(new[] { Key.Alt, Key.F4 })
              && Settings.LoadMouseKeyMap("Friend", new() { ["right"] = Key.None })["right"] == Key.Space,
              "with the keys its numbers stood for");
        Check(File.ReadAllText(_file).Contains("\"one\":\"H\"") && !File.Exists(BeforeFile), "saved by name, with no upgrade of this install's own file");
    }

    private static void Windows()
    {
        var wrong = Old.Where(o => WindowsKeys.ToVk(Enum.Parse<Key>(o.Name)) != o.Number || WindowsKeys.FromVk(o.Number) != Enum.Parse<Key>(o.Name)).ToList();
        Check(wrong.Count == 0, "each key goes to Windows as the code it always was, and comes back as itself");
        Check(WindowsKeys.FromVk(0x5D) == (Key)0x5D && !KeyNames.HasName(WindowsKeys.FromVk(0x5D)), "a Windows key the app has no name for is still told apart");

        // The keys Windows wants flagged as "extended" when sent: the list
        // KeyMap.IsExtendedKey held, and the flags the on-screen keyboard's
        // layout carried per key, both gone into the translator.
        var extended = new HashSet<ushort> { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C };
        var differs = Enum.GetValues<Key>().Where(k => WindowsKeys.IsExtended(k) != extended.Contains((ushort)k)).ToList();
        Check(differs.Count == 0, "the extended keys are the same twelve as before");

        var layoutKeys = KeyboardLayout.FullRows().SelectMany(r => r).Concat(KeyboardLayout.MiniRow())
            .Where(k => k.Kind is KeyboardLayout.KeyKind.Plain or KeyboardLayout.KeyKind.StickyFixed).ToList();
        Check(layoutKeys.Count > 0 && layoutKeys.All(k => k.Key != Key.None && KeyNames.HasName(k.Key)), "every fixed key of the on-screen keyboard has a named key");
        var layoutExtended = layoutKeys.Where(k => WindowsKeys.IsExtended(k.Key)).Select(k => k.Key).Distinct().OrderBy(k => (ushort)k).ToList();
        Check(layoutExtended.SequenceEqual(new[] { Key.Left, Key.Up, Key.Right, Key.Down, Key.Delete, Key.Win }),
              "and the ones sent as extended are the six the layout used to mark: the arrows, Del and Win");
        var spec = layoutKeys.First(k => k.Label == "Esc");
        var semi = layoutKeys.First(k => k.Label == ";");
        Check(spec.Key == Key.Escape && semi.Key == Key.Semicolon && layoutKeys.First(k => k.Label == "Enter").Key == Key.Enter, "Esc, ; and Enter are still themselves");
    }

    // "--upgrade-check <settings.json>": the upgrade, tried on a copy of a
    // real settings file. Nothing of the real file is changed. Every key
    // in the copy is compared before and after: a number must have become
    // the name of that same number, and everything else must be as it was.
    public static bool UpgradeCheck(string scratch, string realFile)
    {
        _ok = true;
        _dir = Path.Combine(scratch, "upgrade-check");
        _file = Path.Combine(_dir, "settings.json");
        Console.WriteLine("--- the upgrade, on a copy of a real settings file ---");
        if (!File.Exists(realFile))
        {
            Check(false, "the file to check exists");
            return false;
        }
        Fresh();
        string before = File.ReadAllText(realFile);
        File.WriteAllText(_file, before);

        Settings.LoadActiveProfileName(); // any read upgrades
        string after = File.ReadAllText(_file);
        Check(File.Exists(BeforeFile) && File.ReadAllText(BeforeFile) == before, "the copy from before is kept, byte for byte");

        int keys = 0, others = 0;
        var problems = new List<string>();
        using (var oldDoc = JsonDocument.Parse(before))
        using (var newDoc = JsonDocument.Parse(after))
            Compare(oldDoc.RootElement, newDoc.RootElement, "", ref keys, ref others, problems);
        foreach (string p in problems.Take(20))
            Console.WriteLine("    " + p);
        Check(problems.Count == 0, $"{keys} keys became their names, {others} other values are as they were, {problems.Count} differences");
        Check(keys > 0, "and the file did hold keys as numbers (it was an old one)");

        // And once more: the upgraded file read back gives a file that is
        // the same again.
        Settings.UseScratchFile(_file);
        Settings.SaveKeyboardScale(Settings.LoadKeyboardScale());
        Check(File.ReadAllText(_file) == after, "saving the upgraded file again writes the very same file");
        return _ok;
    }

    // Whether a place in the file holds keys: the last name on the way to it.
    private static bool HoldsKeys(string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // .../KeyMap/<id> ; .../ExtraKeys/<id>/<n>
        return (parts.Length >= 2 && parts[^2].EndsWith("KeyMap")) || (parts.Length >= 3 && parts[^3].EndsWith("ExtraKeys"));
    }

    private static void Compare(JsonElement old, JsonElement now, string path, ref int keys, ref int others, List<string> problems)
    {
        if (old.ValueKind == JsonValueKind.Object && now.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in old.EnumerateObject())
            {
                string child = path + "/" + property.Name;
                // The oldest shapes' flat fields move elsewhere on a save
                // (see SettingsTests.OlderFiles); a file with those is not
                // what this check is for.
                if (!now.TryGetProperty(property.Name, out var counterpart))
                {
                    if (property.Value.ValueKind != JsonValueKind.Null)
                        problems.Add($"{child}: gone");
                    continue;
                }
                Compare(property.Value, counterpart, child, ref keys, ref others, problems);
            }
            return;
        }
        if (old.ValueKind == JsonValueKind.Array && now.ValueKind == JsonValueKind.Array)
        {
            if (old.GetArrayLength() != now.GetArrayLength())
            {
                problems.Add($"{path}: {old.GetArrayLength()} entries became {now.GetArrayLength()}");
                return;
            }
            for (int i = 0; i < old.GetArrayLength(); i++)
                Compare(old[i], now[i], path + "/" + i, ref keys, ref others, problems);
            return;
        }
        if (HoldsKeys(path) && old.ValueKind == JsonValueKind.Number)
        {
            ushort number = old.GetUInt16();
            bool named = Enum.IsDefined((Key)number);
            bool right = named
                ? now.ValueKind == JsonValueKind.String && Enum.TryParse<Key>(now.GetString(), out var key) && (ushort)key == number
                : now.ValueKind == JsonValueKind.Number && now.GetUInt16() == number;
            if (right)
                keys++;
            else
                problems.Add($"{path}: key {number} became {now.GetRawText()}");
            return;
        }
        if (old.GetRawText() == now.GetRawText())
            others++;
        else
            problems.Add($"{path}: {old.GetRawText()} became {now.GetRawText()}");
    }

    private static void Section(string name, Action body)
    {
        Console.WriteLine($"--- {name} ---");
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
