using System.Diagnostics;

namespace UnboundKeys.Tests;

// One copy at a time, and the hand-over after an update (see
// SingleInstance.Claim), with two real processes: this harness starts a
// second copy of itself that holds a mutex for a while and then exits
// without releasing it, the way the app does when it quits. The mutex names
// are the tests' own, so a running UnboundKeys is left alone.
internal static class SingleInstanceTests
{
    public const string HoldArgument = "--hold-mutex";

    private static bool _ok;

    // The second copy's whole job: claim the named mutex, say so, stay for
    // the given time, leave.
    public static int Hold(string name, int milliseconds)
    {
        if (!SingleInstance.Claim(name, TimeSpan.Zero))
            return 1;
        Console.WriteLine("held");
        Thread.Sleep(milliseconds);
        return 0;
    }

    public static bool Run()
    {
        Console.WriteLine("--- one copy at a time, and the hand-over after an update ---");
        _ok = true;
        try
        {
            string run = Guid.NewGuid().ToString("N");

            Check(SingleInstance.Claim($"UnboundKeys.Tests.{run}.alone", TimeSpan.FromSeconds(5)), "with no other copy, this one runs");

            // An update: the old copy is still there, and quits 1.5 s later.
            string leaving = $"UnboundKeys.Tests.{run}.leaving";
            using (var old = StartHolder(leaving, 1500))
            {
                bool asked = false;
                var watch = Stopwatch.StartNew();
                bool claimed = SingleInstance.Claim(leaving, TimeSpan.FromSeconds(5), () => asked = true);
                double waited = watch.Elapsed.TotalSeconds;
                Check(asked, "a copy that finds another asks it to show its dashboard");
                Check(claimed, "when the other copy quits within the wait, this one takes over");
                Check(waited > 0.5 && waited < 4, $"and it took over as the other left: {waited:0.0} s, not the whole wait");
                old.WaitForExit(5000);
            }

            // A second click on the shortcut: the running copy is staying.
            string staying = $"UnboundKeys.Tests.{run}.staying";
            using (var running = StartHolder(staying, 4000))
            {
                var watch = Stopwatch.StartNew();
                bool claimed = SingleInstance.Claim(staying, TimeSpan.FromSeconds(1));
                double waited = watch.Elapsed.TotalSeconds;
                Check(!claimed, "when the other copy stays, this one leaves");
                Check(waited >= 0.9 && waited < 3, $"after its wait: {waited:0.0} s");
                running.WaitForExit(8000);
            }
        }
        catch (Exception ex)
        {
            Check(false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
        return _ok;
    }

    // Starts the second copy and returns once it says it holds the mutex.
    private static Process StartHolder(string name, int milliseconds)
    {
        var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{HoldArgument} {name} {milliseconds}")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        })!;
        string? line = process.StandardOutput.ReadLine();
        if (line != "held")
            throw new InvalidOperationException($"the second copy did not take the mutex (it said \"{line}\")");
        return process;
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        _ok &= condition;
    }
}
