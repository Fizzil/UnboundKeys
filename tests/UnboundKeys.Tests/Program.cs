using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using UnboundKeys;

// Measures Game mode's timing (see KeyBehavior.RepeatGapSeconds and
// Priority) on the real KeyExecutor: a low-level keyboard hook timestamps
// every F13/F14 it sends, so the gap, the hand-off to a priority key and
// the resume can be checked in milliseconds rather than believed.
const ushort F13 = 0x7C;
const ushort F14 = 0x7D;
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
var priorityKeys = new List<(ushort Vk, bool Extended)> { (F14, false) };
bool allPassed = true;

allPassed &= Scenario("priority with a 2.0 s hold", prioritySeconds: 2.0, expectedResumeGap: 2.0);
allPassed &= Scenario("priority with the default hold (one gap)", prioritySeconds: 0.0, expectedResumeGap: 1.0);
allPassed &= Scenario("class GCD with no per-mapping gap", prioritySeconds: 0.0, expectedResumeGap: 1.0, useClassGcd: true);

Console.WriteLine(allPassed ? "ALL PASSED" : "FAILED");
return allPassed ? 0 : 1;

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
        Console.WriteLine($"  {t,6:0.000} s  {(vk == F13 ? "repeat  (F13)" : "priority (F14)")}");

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
