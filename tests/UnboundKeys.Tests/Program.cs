using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using UnboundKeys;

// Measures the Infinite pause (see KeyBehavior.Priority) on the real
// KeyExecutor: a low-level keyboard hook timestamps every F13/F14 it
// sends, so the pause key's own press and the repeat's resume can be
// checked in milliseconds rather than believed.
const ushort F13 = 0x7C; // the repeat key
const ushort F14 = 0x7D; // the pause key
const int WmKeyDown = 0x100;
const int WmSysKeyDown = 0x104;

var events = new List<(double T, ushort Vk)>();
var clock = Stopwatch.StartNew();
var hook = new LowLevelHook();
LowLevelHook.HookProc proc = (nCode, wParam, lParam) =>
{
    if (nCode >= 0 && ((int)wParam == WmKeyDown || (int)wParam == WmSysKeyDown))
    {
        ushort vk = (ushort)Marshal.ReadInt32(lParam);
        if (vk == F13 || vk == F14)
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
