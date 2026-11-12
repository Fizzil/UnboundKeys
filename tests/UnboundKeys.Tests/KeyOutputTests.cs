using System.Diagnostics;

namespace UnboundKeys.Tests;

// KeyOutput is the one way out for every key the app presses, and keeps a
// ledger of what is down so Stop can let go of anything the mappings' own
// bookkeeping missed. Checked with a sender that only notes what it is
// asked to send, so nothing here reaches Windows.
internal static class KeyOutputTests
{
    private static bool _ok;

    private const ushort KeyA = 0x41;
    private const ushort KeyB = 0x42;
    private const ushort MiddleClick = 0x04;

    // Notes every down and up, and which keys are down by its own count.
    private sealed class RecordingSender : IKeySender
    {
        private readonly object _gate = new();
        private readonly HashSet<ushort> _down = new();
        public readonly List<(ushort Key, bool Down)> Events = new();
        public Action<ushort>? OnKeyDown;
        // Runs before a down is noted: whatever it does lands between the
        // executor deciding to press and the press itself.
        public Action<ushort>? BeforeKeyDown;

        public void KeyDown(ushort key, bool extended)
        {
            BeforeKeyDown?.Invoke(key);
            lock (_gate)
            {
                _down.Add(key);
                Events.Add((key, true));
            }
            OnKeyDown?.Invoke(key);
        }

        public void KeyUp(ushort key, bool extended)
        {
            lock (_gate)
            {
                _down.Remove(key);
                Events.Add((key, false));
            }
        }

        public int DownCount { get { lock (_gate) return _down.Count; } }
        public int EventCount { get { lock (_gate) return Events.Count; } }
    }

    public static bool Run()
    {
        _ok = true;
        Section("key output: the ledger of held keys", Ledger);
        Section("key output: a press that crosses a Stop is let go", PressCrossingStop);
        Section("key output: a hold ended just before its keys go down lets go again", HoldEndedBeforeItsPress);
        Section("key output: Stop racing the start of a hold leaves nothing down", StopRacesHold);
        return _ok;
    }

    private static void With(RecordingSender sender, Action body)
    {
        KeyOutput.ReleaseEverything();
        var real = KeyOutput.Use(sender);
        try
        {
            body();
        }
        finally
        {
            KeyOutput.ReleaseEverything();
            KeyOutput.Use(real);
        }
    }

    private static void Ledger()
    {
        var sender = new RecordingSender();
        With(sender, () =>
        {
            KeyOutput.KeyDown(KeyA, false);
            Check(KeyOutput.HeldCount == 1 && sender.DownCount == 1, "a key pressed is sent and noted as down");
            KeyOutput.KeyUp(KeyA, false);
            Check(KeyOutput.HeldCount == 0 && sender.DownCount == 0, "let go, it is sent up and no longer noted");

            KeyOutput.TapKey(KeyB, false);
            Check(KeyOutput.HeldCount == 0 && sender.DownCount == 0, "a tap leaves nothing down");

            KeyOutput.KeyDown(KeyA, false);
            KeyOutput.KeyDown(KeyA, false);
            KeyOutput.KeyDown(MiddleClick, false);
            Check(KeyOutput.HeldCount == 2, "a key pressed twice (a hold repeating) is one held key; a mouse button counts too");
            int released = KeyOutput.ReleaseEverything();
            Check(released == 2 && sender.DownCount == 0 && KeyOutput.HeldCount == 0, "letting go of everything sends each one up, once");
            Check(KeyOutput.ReleaseEverything() == 0, "and with nothing down there is nothing to send");

            int before = sender.EventCount;
            KeyOutput.KeyDown(PadKeys.Throttle, false);
            KeyOutput.KeyUp(PadKeys.Throttle, false);
            Check(sender.EventCount == before, "a pad pedal is not a system key: it is never handed to the sender");
        });
    }

    // A Stop that lands while a press is on its way out may send its
    // key-up first; the press must not then be left down.
    private static void PressCrossingStop()
    {
        var sender = new RecordingSender();
        With(sender, () =>
        {
            bool once = true;
            sender.OnKeyDown = _ =>
            {
                if (!once)
                    return;
                once = false;
                KeyOutput.ReleaseEverything();
            };
            KeyOutput.KeyDown(KeyA, false);
            Check(sender.DownCount == 0 && KeyOutput.HeldCount == 0, "a Stop arriving in the middle of a press still ends with the key up");
        });
    }

    // An infinite hold claims its slot first and presses its keys a moment
    // later. If something ends the hold in between (here ForceRelease, as an
    // edit in the dashboard does; a focus switch or another word taking the
    // slot are the same), its key-ups go out BEFORE the key-downs, and the
    // keys used to stay down with nothing holding them. The sender's hook
    // puts the release exactly in that gap.
    private static void HoldEndedBeforeItsPress()
    {
        var hold = new KeyBehavior { Hold = true, Infinite = true };
        foreach (var (name, keys) in new[]
        {
            ("a mouse button", new List<(ushort Vk, bool Extended)> { (MiddleClick, false) }),
            ("two keyboard keys", new List<(ushort Vk, bool Extended)> { (KeyA, false), (KeyB, false) }),
        })
        {
            var sender = new RecordingSender();
            With(sender, () =>
            {
                bool once = true;
                sender.BeforeKeyDown = _ =>
                {
                    if (!once)
                        return;
                    once = false;
                    KeyExecutor.ForceRelease("one");
                };
                var run = Task.Run(() => KeyExecutor.Execute("one", keys, hold));
                bool ended = run.Wait(2000);
                Check(ended && !KeyExecutor.IsRunning("one"), $"{name}: the hold that was ended is not running");
                Check(sender.DownCount == 0 && KeyOutput.HeldCount == 0, $"{name}: and its keys are up, not stuck down");
                KeyExecutor.ReleaseAll();
            });
        }
    }

    // The real executor, many times over: an infinite hold starts on one
    // thread while Stop (ReleaseAll) runs on another a moment later. However
    // the two interleave, nothing may be left down afterwards. Keyboard keys
    // and a mouse button, since a held mouse button has no repeat loop to
    // come back through.
    private static void StopRacesHold()
    {
        var sender = new RecordingSender();
        With(sender, () =>
        {
            var hold = new KeyBehavior { Hold = true, Infinite = true };
            var keyboard = new List<(ushort Vk, bool Extended)> { (KeyA, false), (KeyB, false) };
            var mouse = new List<(ushort Vk, bool Extended)> { (MiddleClick, false) };
            var random = new Random(12345);
            int orphans = 0;
            int leftDown = 0;
            bool allEnded = true;
            const int rounds = 300;
            for (int i = 0; i < rounds; i++)
            {
                var keys = i % 2 == 0 ? keyboard : mouse;
                var run = Task.Run(() => KeyExecutor.Execute("one", keys, hold));
                // From "Stop first" to "Stop a little after the keys went down".
                long waitTicks = random.Next(0, 400) * (Stopwatch.Frequency / 1_000_000);
                var clock = Stopwatch.StartNew();
                while (clock.ElapsedTicks < waitTicks)
                    Thread.SpinWait(10);
                KeyExecutor.ReleaseAll();
                // A start that lost the race entirely (Stop ran before it
                // claimed anything) is a hold still running: stop that too.
                if (!run.Wait(100))
                {
                    KeyExecutor.ReleaseAll();
                    allEnded &= run.Wait(2000);
                }
                // A key down while nothing says it is holding one is the
                // stuck key this is about (a mouse hold that started after
                // the Stop is still running, and rightly down).
                if (!KeyExecutor.IsRunning("one") && sender.DownCount != 0)
                    orphans++;
                KeyExecutor.ReleaseAll();
                if (sender.DownCount != 0 || KeyOutput.HeldCount != 0)
                    leftDown++;
            }
            Check(allEnded, $"every one of {rounds} holds ended when stopped");
            Check(orphans == 0, $"no key was ever down with nothing holding it ({orphans} were)");
            Check(leftDown == 0, $"and after a last Stop nothing at all was down ({leftDown} times something was)");
        });
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
