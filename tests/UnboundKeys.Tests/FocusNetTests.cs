using System.Diagnostics;

namespace UnboundKeys.Tests;

// The focus safety net (KeyExecutor.CheckFocus, every 0.3 s): an Infinite
// hold or repeat lets go when the window it was started in stops being the
// one in front, or when that window's title changes to something else (a
// browser tab). Checked with a made-up window in front and a sender that
// only notes what it is asked to send, so nothing here depends on which
// real window has the focus.
internal static class FocusNetTests
{
    private static bool _ok;

    private const ushort KeyA = 0x41;
    private const ushort MiddleClick = 0x04;

    private sealed class FakeWindows : IActiveWindow
    {
        private readonly object _gate = new();
        private long _id = 100;
        private string _title = "The game";

        public void Show(long id, string title)
        {
            lock (_gate)
            {
                _id = id;
                _title = title;
            }
        }

        public WindowId Current { get { lock (_gate) return new WindowId(_id); } }
        public string TitleOf(WindowId window) { lock (_gate) return _title; }
    }

    public static bool Run()
    {
        _ok = true;
        Section("focus net: a held key lets go when the window changes", HoldAndWindow);
        Section("focus net: a title change is told from a tab switch", HoldAndTitle);
        Section("focus net: a hold of pedals only survives a tab's title change", PedalsAndTitle);
        Section("focus net: an infinite repeat stops when the window changes", RepeatAndWindow);
        return _ok;
    }

    private static readonly KeyBehavior InfiniteHold = new() { Hold = true, Infinite = true };

    private static void With(Action<FakeWindows, KeyOutputTests.RecordingSender> body)
    {
        var windows = new FakeWindows();
        var sender = new KeyOutputTests.RecordingSender();
        KeyExecutor.ReleaseAll();
        var realWindows = ActiveWindow.Use(windows);
        var realSender = KeyOutput.Use(sender);
        try
        {
            body(windows, sender);
        }
        finally
        {
            KeyExecutor.ReleaseAll();
            KeyOutput.Use(realSender);
            ActiveWindow.Use(realWindows);
        }
    }

    // The net looks every 0.3 s: long enough for several looks.
    private static bool Within(int ms, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            if (condition())
                return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private static void StartMouseHold(string word = "one") =>
        KeyExecutor.Execute(word, new List<(ushort Vk, bool Extended)> { (MiddleClick, false) }, InfiniteHold);

    private static void HoldAndWindow()
    {
        With((windows, sender) =>
        {
            StartMouseHold();
            Check(KeyExecutor.IsRunning("one") && sender.DownCount == 1, "an infinite hold is running, its button down");
            Thread.Sleep(700);
            Check(KeyExecutor.IsRunning("one") && sender.DownCount == 1, "and stays so while the same window is in front");
            windows.Show(200, "Something else");
            Check(Within(1500, () => !KeyExecutor.IsRunning("one") && sender.DownCount == 0), "another window in front: the hold ends and the button goes up");
        });
    }

    private static void HoldAndTitle()
    {
        With((windows, sender) =>
        {
            windows.Show(100, "notes.txt");
            StartMouseHold();
            windows.Show(100, "notes.txt *");
            Thread.Sleep(900);
            Check(KeyExecutor.IsRunning("one") && sender.DownCount == 1, "the title gaining a mark (unsaved changes) does not end the hold");
            windows.Show(100, "A different tab");
            Check(Within(1500, () => !KeyExecutor.IsRunning("one") && sender.DownCount == 0), "a title that is a different one altogether (a tab switch) does");
        });
    }

    private static void PedalsAndTitle()
    {
        With((windows, sender) =>
        {
            windows.Show(100, "Race: lap 1");
            // A hold of anything but a mouse button stays in Execute,
            // repeating its key, until it is ended: so on its own thread.
            var run = Task.Run(() => KeyExecutor.Execute("one", new List<(ushort Vk, bool Extended)> { (PadKeys.Throttle, false) }, InfiniteHold));
            Check(Within(1000, () => KeyExecutor.IsRunning("one")), "an infinite hold of the throttle is running");
            windows.Show(100, "Finished!");
            Thread.Sleep(900);
            Check(KeyExecutor.IsRunning("one"), "a hold of only the throttle is kept when just the tab's title changes");
            windows.Show(200, "Something else");
            Check(Within(1500, () => !KeyExecutor.IsRunning("one")) && run.Wait(1000), "but another window in front still ends it");
        });
    }

    private static void RepeatAndWindow()
    {
        With((windows, sender) =>
        {
            var repeat = new KeyBehavior { Repeat = true, Infinite = true };
            var run = Task.Run(() => KeyExecutor.Execute("one", new List<(ushort Vk, bool Extended)> { (KeyA, false) }, repeat));
            Check(Within(1000, () => KeyExecutor.IsRunning("one") && sender.EventCount >= 4), "an infinite repeat is running, pressing its key");
            windows.Show(200, "Something else");
            Check(Within(1500, () => !KeyExecutor.IsRunning("one")) && run.Wait(1000), "another window in front: the repeat stops");
            Check(sender.DownCount == 0, "with its key up");
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
