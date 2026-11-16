namespace UnboundKeys.Tests;

// The maps are changed on one thread and read on others (see
// RemapStore.Gate). A profile switch empties and refills them; a reader on
// another thread must never see them empty, nor trip over the change.
internal static class MapGateTests
{
    private static bool _ok;

    public static bool Run()
    {
        _ok = true;
        Section("maps: read on one thread while a profile switch refills them on another", SwitchWhileReading);
        Section("maps: an id nobody has is refused, not an error", UnknownId);
        return _ok;
    }

    private static void SwitchWhileReading()
    {
        string profile = KeyMap.ActiveProfile;
        MouseMap.Rebind("middle", Key.F);
        MouseMap.AddExtraKey("middle", Key.Shift);
        VirtualKeyMap.Rebind("va", Key.B);
        try
        {
            int reads = 0, empty = 0, wrong = 0;
            Exception? thrown = null;
            using var stop = new CancellationTokenSource();
            var readers = Enumerable.Range(0, 3).Select(reader => Task.Run(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        bool mouse = MouseMap.TryGet("middle", out var keys, out _) && MouseMap.IsEnabled("middle") && MouseMap.AnyGestureEnabled("middle");
                        bool word = KeyMap.TryGet("one", out var wordKeys, out _);
                        bool key = VirtualKeyMap.IsIntercepted("va") && VirtualKeyMap.IsMapped("va") && VirtualKeyMap.TryGet("va", out _, out _);
                        if (!mouse || !word || !key)
                            Interlocked.Increment(ref empty);
                        else if (!keys.SequenceEqual(new[] { Key.F, Key.Shift }) || wordKeys.Count != 1)
                            Interlocked.Increment(ref wrong);
                        Interlocked.Increment(ref reads);
                    }
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }
            })).ToArray();

            for (int i = 0; i < 150; i++)
            {
                KeyMap.SwitchProfile(profile);
                MouseMap.SwitchProfile(profile);
                VirtualKeyMap.SwitchProfile(profile);
            }
            stop.Cancel();
            Task.WaitAll(readers, 5000);

            Check(thrown == null, thrown == null ? "150 switches with three readers at work: no reader failed" : $"a reader failed: {thrown.GetType().Name}: {thrown.Message}");
            Check(reads > 1000, $"the readers did read meanwhile ({reads} times)");
            Check(empty == 0, $"none of them ever found a map empty mid-switch ({empty})");
            Check(wrong == 0, $"or half filled ({wrong})");
        }
        finally
        {
            MouseMap.ResetToDefault("middle");
            VirtualKeyMap.ResetToDefault("va");
        }
    }

    private static void UnknownId()
    {
        Check(!KeyMap.TryGet("eleven", out var keys, out var behavior) && keys.Count == 0 && behavior.IsPlainTap(), "a word that is not one of the ten");
        Check(!MouseMap.TryGet("left", out _, out _) && !MouseMap.IsEnabled("left") && !MouseMap.AnyGestureEnabled("left"), "the left button, never the app's");
        Check(!VirtualKeyMap.IsIntercepted("vshift") && !VirtualKeyMap.IsMapped("nothing") && !SteeringMap.IsEnabled("pushsideways"), "a key or a push that is not in the map");
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
