using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using UnboundKeys;

// Measures the Infinite pause (see KeyBehavior.Priority) on the real
// KeyExecutor: a low-level keyboard hook timestamps every F13/F14 it
// sends, so the pause key's own press and the repeat's resume can be
// checked in milliseconds rather than believed.
const ushort F13 = 0x7C; // the repeat key
const ushort F14 = 0x7D; // the pause key (and a rotation's second key)
const ushort F15 = 0x7E; // a rotation's third key
const int WmKeyDown = 0x100;
const int WmSysKeyDown = 0x104;

// Started by SingleInstanceTests as its second copy: hold a mutex, leave.
if (args.Length == 3 && args[0] == UnboundKeys.Tests.SingleInstanceTests.HoldArgument)
    return UnboundKeys.Tests.SingleInstanceTests.Hold(args[1], int.Parse(args[2]));

// Settings and the log go to a scratch folder for the whole run, so nothing
// here touches the real profiles and the harness can run with the app open.
string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UnboundKeys.Tests-" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(scratch);
Settings.UseScratchFile(System.IO.Path.Combine(scratch, "settings.json"));
Log.UseScratchFile(System.IO.Path.Combine(scratch, "log.txt"));
// The log is off unless switched on (see SettingsTests.LogSwitch); the
// checks below that read it need it on.
Log.Enabled = true;

// "--online" runs only the one check that needs the internet (see
// UpdaterTests.RunOnline); everything else stays on this machine.
if (args.Contains("--online"))
{
    bool online = UnboundKeys.Tests.UpdaterTests.RunOnline();
    try { System.IO.Directory.Delete(scratch, recursive: true); }
    catch { /* a leftover scratch folder in Temp is not a failure */ }
    Console.WriteLine(online ? "ALL PASSED" : "FAILED");
    return online ? 0 : 1;
}

var events = new List<(double T, ushort Vk)>();
var clock = Stopwatch.StartNew();
var hook = new LowLevelHook();
LowLevelHook.HookProc proc = (nCode, wParam, lParam) =>
{
    if (nCode >= 0 && ((int)wParam == WmKeyDown || (int)wParam == WmSysKeyDown))
    {
        ushort vk = (ushort)Marshal.ReadInt32(lParam);
        if (vk >= F13 && vk <= F15)
            lock (events)
                events.Add((clock.Elapsed.TotalSeconds, vk));
    }
    return hook.CallNext(nCode, wParam, lParam);
};
// A low-level hook only fires on a thread that pumps messages.
var hookThread = new Thread(() =>
{
    hook.Start(13, proc);
    Dispatcher.Run();
})
{ IsBackground = true };
hookThread.SetApartmentState(ApartmentState.STA);
hookThread.Start();
Thread.Sleep(400);

var repeatKeys = new List<(ushort Vk, bool Extended)> { (F13, false) };
var pauseKeys = new List<(ushort Vk, bool Extended)> { (F14, false) };
bool allPassed = true;

allPassed &= Scenario("a tap with a 2.0 s pause", new KeyBehavior { Priority = true, PrioritySeconds = 2.0 }, expectedPause: 2.0);
allPassed &= Scenario("Fizzil's recipe: Hold 1.0 s with a 1.0 s pause", new KeyBehavior { Hold = true, DurationSeconds = 1.0, Priority = true, PrioritySeconds = 1.0 }, expectedPause: 1.0);
allPassed &= RotationScenario();
allPassed &= HoldScenario();
allPassed &= GesturesScenario();
allPassed &= OneButtonScenario();
allPassed &= UnboundKeys.Tests.SettingsTests.Run(scratch);
allPassed &= UnboundKeys.Tests.UpdaterTests.Run();
allPassed &= UnboundKeys.Tests.SingleInstanceTests.Run();

try { System.IO.Directory.Delete(scratch, recursive: true); }
catch { /* a leftover scratch folder in Temp is not a failure */ }

Console.WriteLine(allPassed ? "ALL PASSED" : "FAILED");
return allPassed ? 0 : 1;

// The repeat taps every 0.1 s. One second in, the pause key fires: it
// should go out at once, and the repeat should stay quiet for the pause
// (measured from the repeat's last key before it) and then carry on.
bool Scenario(string name, KeyBehavior pause, double expectedPause)
{
    const double gap = 0.1;
    Console.WriteLine($"--- {name} ---");
    lock (events)
        events.Clear();

    var repeat = new KeyBehavior { Repeat = true, Infinite = true };

    clock.Restart();
    _ = Task.Run(() => KeyExecutor.Execute("one", repeatKeys, repeat));
    Thread.Sleep(1000);
    double pressAt = clock.Elapsed.TotalSeconds;
    var pauseTask = Task.Run(() => KeyExecutor.Execute("two", pauseKeys, pause));
    Thread.Sleep((int)(expectedPause * 1000) + 1500);
    KeyExecutor.ReleaseAll();
    pauseTask.Wait(3000);
    Thread.Sleep(300);

    List<(double T, ushort Vk)> log;
    lock (events)
        log = new List<(double, ushort)>(events);

    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    int pauseIndex = log.FindIndex(e => e.Vk == F14);
    Check(pauseIndex > 0, "the pause key fired");
    if (pauseIndex <= 0)
        return false;

    var before = log.Take(pauseIndex).Where(e => e.Vk == F13).ToList();
    var after = log.Skip(pauseIndex + 1).Where(e => e.Vk == F13).ToList();
    Console.WriteLine($"  {before.Count} repeat keys, then the pause key at {log[pauseIndex].T:0.000} s, then {after.Count} repeat keys");
    Check(before.Count >= 8, $"the repeat was running before it (got {before.Count} keys in a second)");
    Check(Math.Abs(log[pauseIndex].T - pressAt) < 0.15, $"the pause key went out at once: {log[pauseIndex].T - pressAt:0.000} s after the press");
    if (pause.Hold)
    {
        // A held key repeats (see KeyExecutor.HoldRepeatDelayMs): every
        // down must fall inside the hold.
        var holdDowns = log.Where(e => e.Vk == F14).ToList();
        double span = holdDowns[^1].T - holdDowns[0].T;
        Check(holdDowns.Count >= 5 && span <= pause.DurationSeconds + 0.1, $"the held key repeated while held: {holdDowns.Count} downs over {span:0.00} s");
    }
    else
        Check(log.Count(e => e.Vk == F14) == 1, "it went out once (the repeat's gap is inside the queue window)");
    Check(after.Count >= 1, "the repeat resumed");
    if (after.Count >= 1)
    {
        double quiet = after[0].T - before[^1].T;
        Check(quiet >= expectedPause - 0.05 && quiet < expectedPause + gap + 0.3, $"the repeat stayed quiet for {quiet:0.000} s, expected about {expectedPause:0.0} plus one gap");
    }
    for (int i = 1; i < Math.Min(after.Count, 5); i++)
        Check(Math.Abs(after[i].T - after[i - 1].T - gap) < 0.08, $"repeat gap after resume {i}: {after[i].T - after[i - 1].T:0.000} s");

    return ok;
}

// Rotation (see KeyBehavior.Rotation): three keys on one infinite repeat.
// Every tick should press all three in order a few milliseconds apart,
// then pause a tenth of a second, then start again from the first.
bool RotationScenario()
{
    Console.WriteLine("--- rotation: bursts in priority order ---");
    lock (events)
        events.Clear();

    var keys = new List<(ushort Vk, bool Extended)> { (F13, false), (F14, false), (F15, false) };
    var rotation = new KeyBehavior { Repeat = true, Infinite = true, Rotation = true };

    clock.Restart();
    _ = Task.Run(() => KeyExecutor.Execute("three", keys, rotation));
    Thread.Sleep(1000);
    KeyExecutor.ReleaseAll();
    Thread.Sleep(300);

    List<(double T, ushort Vk)> log;
    lock (events)
        log = new List<(double, ushort)>(events);

    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    Console.WriteLine($"  {log.Count} keys in a second, {log.Count / 3} bursts");
    Check(log.Count >= 18, $"several bursts went out (got {log.Count / 3})");
    var expected = new[] { F13, F14, F15 };
    bool inOrder = log.Count > 0;
    for (int i = 0; i < log.Count; i++)
        inOrder &= log[i].Vk == expected[i % 3];
    Check(inOrder, "every burst pressed Key 1, Key 2, Key 3 in that order");
    for (int b = 0; b + 2 < log.Count && b < 9; b += 3)
        Check(log[b + 2].T - log[b].T < 0.08, $"burst {b / 3 + 1} took {(log[b + 2].T - log[b].T) * 1000:0} ms from its first key to its last");
    for (int b = 3; b + 2 < log.Count && b < 12; b += 3)
    {
        double period = log[b].T - log[b - 3].T;
        Check(period > 0.10 && period < 0.25, $"burst {b / 3 + 1} began {period * 1000:0} ms after the one before");
    }
    return ok;
}

// Hold repeats like a key held on a real keyboard (see
// KeyExecutor.HoldRepeatDelayMs): the first down at once, the next after
// the delay, then one every interval; nothing after the release, which
// must leave the key up.
bool HoldScenario()
{
    Console.WriteLine("--- hold: repeats like a held key, lets go clean ---");
    lock (events)
        events.Clear();

    var hold = new KeyBehavior { Hold = true, Infinite = true };

    clock.Restart();
    var task = Task.Run(() => KeyExecutor.Execute("four", repeatKeys, hold));
    Thread.Sleep(1500);
    KeyExecutor.ReleaseAll();
    double releasedAt = clock.Elapsed.TotalSeconds;
    task.Wait(3000);
    Thread.Sleep(300);

    List<(double T, ushort Vk)> log;
    lock (events)
        log = new List<(double, ushort)>(events);

    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    var downs = log.Where(e => e.Vk == F13).ToList();
    Console.WriteLine($"  {downs.Count} downs in 1.5 s");
    Check(downs.Count >= 10 && downs.Count <= 14, $"one down, then one every 0.1 s after a 0.45 s delay (got {downs.Count})");
    Check(downs.Count > 0 && downs[0].T < 0.15, "the first down went out at once");
    if (downs.Count > 1)
    {
        double delay = downs[1].T - downs[0].T;
        Check(delay > 0.40 && delay < 0.60, $"the first repeat came after the delay ({delay * 1000:0} ms)");
    }
    for (int i = 2; i < Math.Min(downs.Count, 6); i++)
        Check(Math.Abs(downs[i].T - downs[i - 1].T - 0.1) < 0.08, $"repeat gap {i}: {(downs[i].T - downs[i - 1].T) * 1000:0} ms");
    Check(downs.All(d => d.T <= releasedAt + 0.05), "nothing went out after the release");
    Check((Keys.GetAsyncKeyState(F13) & 0x8000) == 0, "the key is up after the release");
    return ok;
}

// MouseGestures (see its comment): fed downs and ups as the hook feeds
// it, with which mappings exist decided here, it must fire the right id
// at the right moment.
bool GesturesScenario()
{
    Console.WriteLine("--- mouse gestures: single, double and long press ---");
    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    var fired = new List<(double T, string Id)>();
    var enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var sw = Stopwatch.StartNew();
    using var gestures = new MouseGestures(id => enabled.Contains(id), id => { lock (fired) fired.Add((sw.Elapsed.TotalSeconds, id)); });
    List<(double T, string Id)> Fired() { lock (fired) return new(fired); }
    void Fresh() { lock (fired) fired.Clear(); sw.Restart(); }
    string Names(List<(double T, string Id)> list) => list.Count == 0 ? "nothing" : string.Join(", ", list.Select(f => $"{f.Id} at {f.T:0.00} s"));

    // Only a single press set: on the down, at once.
    enabled.Add("middle");
    Fresh();
    gestures.Down("middle");
    gestures.Up("middle");
    Thread.Sleep(50);
    var got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle" && got[0].T < 0.03, $"single press alone: fires on the down, at once ({Names(got)})");

    // A double press set too: a lone tap waits out the window, then the
    // single press fires; two taps fire the double press, once, on the
    // second down.
    enabled.Add("middle.double");
    Fresh();
    gestures.Down("middle");
    gestures.Up("middle");
    Thread.Sleep(150);
    Check(Fired().Count == 0, "with a double press set, a lone tap fires nothing yet");
    Thread.Sleep(300);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle" && got[0].T > 0.25 && got[0].T < 0.45, $"then the single press fires once the window is over ({Names(got)})");
    Fresh();
    gestures.Down("middle");
    gestures.Up("middle");
    Thread.Sleep(120);
    gestures.Down("middle");
    gestures.Up("middle");
    Thread.Sleep(450);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle.double" && got[0].T > 0.10 && got[0].T < 0.20, $"two taps: the double press, on the second down, and no single ({Names(got)})");

    // A long press set, no double: held past the clock, the long press
    // fires while still down and the up is nothing; a short press fires
    // the single press on the up.
    enabled.Remove("middle.double");
    enabled.Add("middle.long");
    Fresh();
    gestures.Down("middle");
    Thread.Sleep(800);
    gestures.Up("middle");
    Thread.Sleep(50);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle.long" && got[0].T > 0.45 && got[0].T < 0.65, $"held: the long press fires at the clock, nothing on the up ({Names(got)})");
    Fresh();
    gestures.Down("middle");
    Thread.Sleep(100);
    gestures.Up("middle");
    Thread.Sleep(50);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle" && got[0].T > 0.08 && got[0].T < 0.20, $"a short press: the single press, on the up ({Names(got)})");

    // All three set: tap, then a press is the double press however long
    // the second is held; a lone short press is the single press, a
    // window after the up.
    enabled.Add("middle.double");
    Fresh();
    gestures.Down("middle");
    Thread.Sleep(100);
    gestures.Up("middle");
    Thread.Sleep(100);
    gestures.Down("middle");
    Thread.Sleep(700);
    gestures.Up("middle");
    Thread.Sleep(100);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle.double", $"all three set, tap then a held press: the double press alone ({Names(got)})");
    Fresh();
    gestures.Down("middle");
    Thread.Sleep(100);
    gestures.Up("middle");
    Thread.Sleep(500);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "middle" && got[0].T > 0.35 && got[0].T < 0.55, $"a lone short press: the single press, a window after the up ({Names(got)})");

    // The wheel: notches, no up; two within the window are the double.
    enabled.Add("wheelup");
    enabled.Add("wheelup.double");
    Fresh();
    gestures.Notch("wheelup");
    Thread.Sleep(100);
    gestures.Notch("wheelup");
    Thread.Sleep(400);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "wheelup.double", $"two wheel notches: the double press ({Names(got)})");
    Fresh();
    gestures.Notch("wheelup");
    Thread.Sleep(450);
    got = Fired();
    Check(got.Count == 1 && got[0].Id == "wheelup" && got[0].T > 0.25, $"one notch: the single press, after the window ({Names(got)})");

    // Single press Not Mapped, the others set: a lone tap sends nothing.
    enabled.Remove("middle");
    Fresh();
    gestures.Down("middle");
    Thread.Sleep(100);
    gestures.Up("middle");
    Thread.Sleep(450);
    Check(Fired().Count == 0, "with Single press Not Mapped, a lone tap sends nothing");
    return ok;
}

// One button, one thing at a time (see Program.cs): a press stops what the
// button's other ways of pressing started, then runs its own, with
// KeyExecutor.Stop as Program.cs uses it. The single press holds F13
// (Infinite), the double press holds F14 (Infinite), the long press holds
// F15 for 1.0 s.
bool OneButtonScenario()
{
    Console.WriteLine("--- one button, one thing at a time ---");
    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }
    static bool Down(ushort vk) => (Keys.GetAsyncKeyState(vk) & 0x8000) != 0;

    var single = new KeyBehavior { Hold = true, Infinite = true };
    var longPress = new KeyBehavior { Hold = true, DurationSeconds = 1.0 };
    var longKeys = new List<(ushort Vk, bool Extended)> { (F15, false) };
    Task Press(string id, List<(ushort Vk, bool Extended)> keys, KeyBehavior behavior) => Task.Run(() =>
    {
        foreach (var sibling in MouseCatalog.SiblingIds(id))
            KeyExecutor.Stop(sibling);
        KeyExecutor.Execute(id, keys, behavior);
    });

    var a = Press("middle", repeatKeys, single);
    Thread.Sleep(300);
    Check(Down(F13), "a single press holds its key");
    var b = Press("middle.double", pauseKeys, single);
    Thread.Sleep(300);
    Check(!Down(F13) && Down(F14), "a double press lets go of it and holds its own");
    Check(a.Wait(2000), "and the single press's run ended");
    var c = Press("middle.long", longKeys, longPress);
    Thread.Sleep(300);
    Check(!Down(F14) && Down(F15), "a long press lets go of that and holds its own");
    Check(b.Wait(2000), "and the double press's run ended");
    var d = Press("middle", repeatKeys, single);
    Thread.Sleep(300);
    Check(!Down(F15) && Down(F13), "a single press cuts the long press's timed hold short and holds its own");
    Check(c.Wait(2000), "and that run ended");
    var e = Press("middle", repeatKeys, single);
    Thread.Sleep(300);
    Check(!Down(F13), "the same press again stops it");
    Check(d.Wait(2000) && e.Wait(2000), "every run ended");
    KeyExecutor.ReleaseAll();
    return ok;
}

static class Keys
{
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
