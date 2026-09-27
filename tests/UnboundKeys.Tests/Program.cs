using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using UnboundKeys;

// Measures Game mode's timing (see KeyBehavior.RepeatGapSeconds and
// Priority) on the real KeyExecutor: a low-level keyboard hook timestamps
// every F13/F14 it sends, so the gap, the priority presses and the resume
// can be checked in milliseconds rather than believed.
const ushort F13 = 0x7C; // the repeat key
const ushort F14 = 0x7D; // the priority key
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
allPassed &= Scenario("priority pressed inside the spell queue window", prioritySeconds: 0.0, expectedResumeGap: 1.0, pressAfterMs: 2750);

Console.WriteLine(allPassed ? "ALL PASSED" : "FAILED");
return allPassed ? 0 : 1;

// The repeat fires at 0, 1.0 and 2.0, so its cooldown ends at 3.0. The
// priority press lands pressAfterMs in: 2.3 by default, 0.7 s before the
// end, which is more than the game's 0.4 s spell queue window, so the key
// goes out at once and again at 2.7; at 2.75 it is inside the window and
// goes out once. The repeat resumes expectedResumeGap after 3.0.
bool Scenario(string name, double prioritySeconds, double expectedResumeGap, bool useClassGcd = false, int pressAfterMs = 2300)
{
    const double gap = 1.0;
    const double cooldownEnd = 3.0;
    const double spellQueue = 0.4;
    const double queuedLead = 0.3;
    Console.WriteLine($"--- {name} ---");
    lock (events)
        events.Clear();

    // The gap comes from the mapping, or from the sub-profile class GCD (GameTiming).
    GameTiming.GcdSeconds = useClassGcd ? gap : 0;
    var repeat = new KeyBehavior { Repeat = true, Infinite = true, UseCustomRepeatIntervals = !useClassGcd, RepeatGapSeconds = useClassGcd ? 0 : gap };
    var priority = new KeyBehavior { Priority = true, PrioritySeconds = prioritySeconds };

    clock.Restart();
    _ = Task.Run(() => KeyExecutor.Execute("one", repeatKeys, repeat));
    Thread.Sleep(pressAfterMs);
    var priorityTask = Task.Run(() => KeyExecutor.Execute("two", priorityKeys, priority));
    Thread.Sleep(2000 + (int)(Math.Max(prioritySeconds, gap) * 1000) + 1500);
    KeyExecutor.ReleaseAll();
    priorityTask.Wait(2000);
    Thread.Sleep(300);

    List<(double T, ushort Vk)> log;
    lock (events)
        log = new List<(double, ushort)>(events);
    foreach (var (t, vk) in log)
        Console.WriteLine($"  {t,6:0.000} s  {(vk == F13 ? "repeat   (F13)" : "priority (F14)")}");

    bool ok = true;
    void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        ok &= condition;
    }

    Check(log.Count > 0 && log[0].Vk == F13, "the repeat fired first");
    if (log.Count == 0 || log[0].Vk != F13)
        return false;
    double t0 = log[0].T; // the repeat's first key marks time zero

    int firstPriority = log.FindIndex(e => e.Vk == F14);
    Check(firstPriority > 0, "the priority key fired");
    if (firstPriority <= 0)
        return false;

    var before = log.Take(firstPriority).Where(e => e.Vk == F13).ToList();
    Check(before.Count == 3, $"three repeat keys before it (got {before.Count})");
    for (int i = 1; i < before.Count; i++)
        Check(Math.Abs(before[i].T - before[i - 1].T - gap) < 0.15, $"repeat gap {i}: {before[i].T - before[i - 1].T:0.000} s, expected {gap:0.0}");

    double pressAt = pressAfterMs / 1000.0;
    var presses = log.Where(e => e.Vk == F14).Select(e => e.T - t0).ToList();
    bool early = cooldownEnd - pressAt > spellQueue;
    Check(presses.Count == (early ? 2 : 1), $"{(early ? "two priority presses, the press being early" : "one priority press, inside the window")} (got {presses.Count})");
    Check(Math.Abs(presses[0] - pressAt) < 0.15, $"the first press went out at once: {presses[0]:0.000} s, expected {pressAt:0.00}");
    if (early && presses.Count >= 2)
        Check(Math.Abs(presses[1] - (cooldownEnd - queuedLead)) < 0.15, $"the second press sat inside the window: {presses[1]:0.000} s, expected {cooldownEnd - queuedLead:0.0}");

    double lastPriority = log.Where(e => e.Vk == F14).Max(e => e.T);
    var after = log.Where(e => e.Vk == F13 && e.T > lastPriority).Select(e => e.T - t0).ToList();
    Check(after.Count >= 1, "the repeat resumed");
    if (after.Count >= 1)
    {
        double expectedResume = cooldownEnd + expectedResumeGap;
        Check(after[0] >= expectedResume - 0.05 && after[0] < expectedResume + 0.3, $"resumed at {after[0]:0.000} s, expected about {expectedResume:0.0} (the cooldown end plus {expectedResumeGap:0.0})");
    }
    for (int i = 1; i < after.Count; i++)
        Check(Math.Abs(after[i] - after[i - 1] - gap) < 0.15, $"repeat gap after resume {i}: {after[i] - after[i - 1]:0.000} s");

    return ok;
}
