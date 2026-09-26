using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using UnboundKeys;

// Measures Game mode's timing (see KeyBehavior.RepeatGapSeconds, Priority
// and Farm) on the real KeyExecutor: a low-level keyboard hook timestamps
// every F13..F16 it sends, so the gap, the hand-off to a priority key, the
// resume and a farm loop's order can be checked in milliseconds rather
// than believed.
const ushort F13 = 0x7C; // the repeat / rotation key
const ushort F14 = 0x7D; // the priority key
const ushort F15 = 0x7E; // Farm nearby: Target nearest enemy
const ushort F16 = 0x7F; // Farm nearby: Interact with target
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
        if (vk >= F13 && vk <= F16)
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
var priorityKeys = new List<(ushort Vk, bool Extended)> { (F14, false) };
bool allPassed = true;

allPassed &= Scenario("priority with a 2.0 s hold", prioritySeconds: 2.0, expectedResumeGap: 2.0);
allPassed &= Scenario("priority with the default hold (one gap)", prioritySeconds: 0.0, expectedResumeGap: 1.0);
allPassed &= Scenario("class GCD with no per-mapping gap", prioritySeconds: 0.0, expectedResumeGap: 1.0, useClassGcd: true);
allPassed &= FarmScenario();

Console.WriteLine(allPassed ? "ALL PASSED" : "FAILED");
return allPassed ? 0 : 1;

string Name(ushort vk) => vk switch
{
    F13 => "repeat   (F13)",
    F14 => "priority (F14)",
    F15 => "target   (F15)",
    F16 => "interact (F16)",
    _ => vk.ToString(),
};

bool Scenario(string name, double prioritySeconds, double expectedResumeGap, bool useClassGcd = false)
{
    const double gap = 1.0;
    Console.WriteLine($"--- {name} ---");
    lock (events)
        events.Clear();

    // The gap comes from the mapping, or from the sub-profile class GCD (GameTiming).
    GameTiming.GcdSeconds = useClassGcd ? gap : 0;
    var repeat = new KeyBehavior { Repeat = true, Infinite = true, UseCustomRepeatIntervals = !useClassGcd, RepeatGapSeconds = useClassGcd ? 0 : gap };
    var priority = new KeyBehavior { Priority = true, PrioritySeconds = prioritySeconds };

    clock.Restart();
    _ = Task.Run(() => KeyExecutor.Execute("one", repeatKeys, repeat));
    Thread.Sleep(2300);                       // repeat fires at 0, 1.0, 2.0; the priority lands 0.3 s after the third
    var priorityTask = Task.Run(() => KeyExecutor.Execute("two", priorityKeys, priority));
    Thread.Sleep(2000 + (int)(Math.Max(prioritySeconds, gap) * 1000) + 1500);
    KeyExecutor.ReleaseAll();
    priorityTask.Wait(2000);
    Thread.Sleep(300);

    List<(double T, ushort Vk)> log;
    lock (events)
        log = new List<(double, ushort)>(events);
    foreach (var (t, vk) in log)
        Console.WriteLine($"  {t,6:0.000} s  {Name(vk)}");

    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    int priorityIndex = log.FindIndex(e => e.Vk == F14);
    Check(priorityIndex > 0, "the priority key fired");
    if (priorityIndex <= 0)
        return false;

    var before = log.Take(priorityIndex).Where(e => e.Vk == F13).ToList();
    var after = log.Skip(priorityIndex + 1).Where(e => e.Vk == F13).ToList();
    Check(before.Count == 3, $"three repeat keys before it (got {before.Count})");
    for (int i = 1; i < before.Count; i++)
        Check(Math.Abs(before[i].T - before[i - 1].T - gap) < 0.15, $"repeat gap {i}: {before[i].T - before[i - 1].T:0.000} s, expected {gap:0.0}");

    double lastBefore = before[^1].T;
    double handoff = log[priorityIndex].T - lastBefore;
    Check(Math.Abs(handoff - gap) < 0.15, $"priority fired one gap after the last repeat key: {handoff:0.000} s");

    Check(after.Count >= 1, "the repeat resumed");
    if (after.Count >= 1)
    {
        double resume = after[0].T - log[priorityIndex].T;
        Check(resume >= expectedResumeGap - 0.05 && resume < expectedResumeGap + 0.3, $"resumed {resume:0.000} s after the priority key, expected about {expectedResumeGap:0.0}");
    }
    for (int i = 1; i < after.Count; i++)
        Check(Math.Abs(after[i].T - after[i - 1].T - gap) < 0.15, $"repeat gap after resume {i}: {after[i].T - after[i - 1].T:0.000} s");

    return ok;
}

// Farm nearby (see KeyExecutor.RunFarmLoop): one rotation key with a 1.0 s
// gap, two rounds, a 1.0 s wait, loot and skin. One cycle is target at 0,
// the rotation at 0.3 and 1.3, the wait, interact at 3.3 and 4.1, and the
// next cycle's target at 4.9.
bool FarmScenario()
{
    Console.WriteLine("--- farm nearby: target, two rounds, wait, loot, skin ---");
    lock (events)
        events.Clear();

    GameTiming.GcdSeconds = 0;
    GameTiming.TargetVk = F15;
    GameTiming.InteractVk = F16;
    var farm = new KeyBehavior { Farm = true, FarmRounds = 2, FarmPauseSeconds = 1.0, FarmSkin = true, UseCustomRepeatIntervals = true, RepeatGapSeconds = 1.0 };

    clock.Restart();
    var task = Task.Run(() => KeyExecutor.Execute("three", repeatKeys, farm));
    Thread.Sleep(5600);
    KeyExecutor.ReleaseAll();
    task.Wait(2000);
    Thread.Sleep(300);
    GameTiming.TargetVk = 0;
    GameTiming.InteractVk = 0;

    List<(double T, ushort Vk)> log;
    lock (events)
        log = new List<(double, ushort)>(events);
    foreach (var (t, vk) in log)
        Console.WriteLine($"  {t,6:0.000} s  {Name(vk)}");

    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    var expected = new (double T, ushort Vk)[] { (0.0, F15), (0.3, F13), (1.3, F13), (3.3, F16), (4.1, F16), (4.9, F15), (5.2, F13) };
    Check(log.Count >= expected.Length, $"at least {expected.Length} keys were sent (got {log.Count})");
    double offset = log.Count > 0 ? log[0].T : 0; // the first key marks time zero
    for (int i = 0; i < expected.Length && i < log.Count; i++)
    {
        double t = log[i].T - offset;
        Check(log[i].Vk == expected[i].Vk, $"key {i + 1} is {Name(expected[i].Vk)} (got {Name(log[i].Vk)})");
        Check(Math.Abs(t - expected[i].T) < 0.15, $"key {i + 1} at {t:0.000} s, expected {expected[i].T:0.0}");
    }
    return ok;
}
