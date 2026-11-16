using System.Diagnostics;

namespace UnboundKeys.Tests;

// The small services behind their seams (stage 1, step 4): the tray icon,
// the microphone keeper, the key click, Start with Windows and opening
// things. Each is checked against a stand-in for the system's side, so
// nothing here shows an icon, records, plays a sound, touches Task
// Scheduler or opens a window.
internal static class ServicesTests
{
    private static bool _ok;

    public static bool Run()
    {
        _ok = true;
        Section("tray: what the icon says", Tray);
        Section("microphone: missing at the start, then found", MicrophoneMissingAtStart);
        Section("microphone: lost while listening, paused, resumed", MicrophoneLostAndPaused);
        Section("key click: one sound, only while switched on", Click);
        Section("start with Windows: asked of the system", Startup);
        Section("opening things: asked of the system", Opening);
        return _ok;
    }

    // ---- tray ----

    private sealed class FakeTray : ITrayShell
    {
        public event Action? Clicked;
        public bool Dimmed;
        public string Tooltip = "";
        public readonly List<string> Notes = new();
        public bool Disposed;

        public void Show(bool dimmed, string tooltip)
        {
            Dimmed = dimmed;
            Tooltip = tooltip;
        }

        public void Notify(string title, string text) => Notes.Add(title);
        public void Click() => Clicked?.Invoke();
        public void Dispose() => Disposed = true;
    }

    private static void Tray()
    {
        var shell = new FakeTray();
        var tray = new TrayIcon(shell);
        Check(!shell.Dimmed && shell.Tooltip.EndsWith("listening"), "at the start: bright, \"listening\"");

        tray.SetPaused(true);
        Check(shell.Dimmed && shell.Tooltip.EndsWith("voice paused"), "paused: dimmed, \"voice paused\"");
        tray.SetMicrophoneLost(true);
        Check(shell.Dimmed && shell.Tooltip.Contains("microphone lost"), "the microphone lost as well: the tooltip says that, the more urgent of the two");
        tray.SetPaused(false);
        Check(shell.Dimmed && shell.Tooltip.Contains("microphone lost"), "listening again but still no microphone: still dimmed");
        tray.SetMicrophoneLost(false);
        Check(!shell.Dimmed && shell.Tooltip.EndsWith("listening"), "the microphone back: bright, \"listening\"");

        int clicks = 0;
        tray.Clicked += () => clicks++;
        shell.Click();
        Check(clicks == 1, "a click on the icon is passed on");

        tray.SayStillRunningOnce();
        tray.SayStillRunningOnce();
        Check(shell.Notes.Count == 1 && shell.Notes[0].Contains("still running"), "\"still running\" is said once, not twice");

        tray.Dispose();
        Check(shell.Disposed, "and the icon goes when the tray does");
    }

    // ---- microphone ----

    private sealed class FakeMicrophone : IMicrophone
    {
#pragma warning disable CS0067 // the keeper does not listen to the sound itself
        public event Action<byte[], int>? Data;
#pragma warning restore CS0067
        public event Action<Exception?>? Stopped;

        public volatile bool Present = true;
        public volatile bool Recording;
        public int Starts;
        public int Stops;

        public void Start()
        {
            Interlocked.Increment(ref Starts);
            if (!Present)
                throw new InvalidOperationException("no microphone");
            Recording = true;
        }

        // Like the real one: a stop asked for is reported as a stop too.
        public void Stop()
        {
            Interlocked.Increment(ref Stops);
            Recording = false;
            Stopped?.Invoke(null);
        }

        public void Unplug()
        {
            Present = false;
            Recording = false;
            Stopped?.Invoke(new InvalidOperationException("unplugged"));
        }

        public void Dispose()
        {
        }
    }

    private const int RetryMs = 100;

    private static void MicrophoneMissingAtStart()
    {
        var mic = new FakeMicrophone { Present = false };
        using var keeper = new MicrophoneKeeper(mic, RetryMs);
        int lost = 0, back = 0;
        Exception? reason = new Exception("unset");
        keeper.Lost += ex => { reason = ex; Interlocked.Increment(ref lost); };
        keeper.Back += () => Interlocked.Increment(ref back);

        keeper.Begin();
        Check(lost == 1 && reason == null, "no microphone at the start is a loss, said once, not an error thrown at the caller");
        Check(Within(1000, () => mic.Starts >= 3) && lost == 1 && back == 0, "it is tried again and again, without saying \"lost\" again");

        mic.Present = true;
        Check(Within(1000, () => back == 1) && mic.Recording, "the microphone appears: recording, and \"back\" is said");
        int starts = mic.Starts;
        Thread.Sleep(RetryMs * 4);
        Check(mic.Starts == starts && back == 1, "and the trying stops");
    }

    private static void MicrophoneLostAndPaused()
    {
        var mic = new FakeMicrophone();
        using var keeper = new MicrophoneKeeper(mic, RetryMs);
        int lost = 0, back = 0;
        Exception? reason = null;
        keeper.Lost += ex => { reason = ex; Interlocked.Increment(ref lost); };
        keeper.Back += () => Interlocked.Increment(ref back);

        keeper.Begin();
        Check(mic.Recording && lost == 0, "with a microphone there, it records and nothing is lost");

        keeper.Pause();
        Check(!mic.Recording && mic.Stops == 1 && lost == 0, "a pause stops it, and a stop that was asked for is not a loss");
        Thread.Sleep(RetryMs * 3);
        Check(!mic.Recording, "nor is it started again behind the pause");

        keeper.Begin();
        Check(mic.Recording, "resumed: recording again");

        mic.Unplug();
        Check(lost == 1 && reason?.Message == "unplugged", "unplugged while listening: lost, with the reason given");
        keeper.Pause();
        int starts = mic.Starts;
        Thread.Sleep(RetryMs * 4);
        Check(mic.Starts == starts, "paused while lost: no trying while it is not wanted");

        keeper.Begin();
        mic.Present = true;
        Check(Within(1000, () => back == 1) && mic.Recording, "wanted again and plugged back in: recording, \"back\" is said");
        Check(lost == 1, "and \"lost\" was said only the once");
    }

    // ---- key click ----

    private sealed class FakeSound : ISoundOut
    {
        public readonly List<byte[]> Played = new();
        public bool Refuse;

        public void Play(byte[] wav)
        {
            if (Refuse)
                throw new InvalidOperationException("no sound device");
            Played.Add(wav);
        }
    }

    private static void Click()
    {
        var sound = new FakeSound();
        var realSound = KeyClick.Use(sound);
        bool wasOn = KeyClick.Enabled;
        try
        {
            KeyClick.Enabled = false;
            KeyClick.Play();
            Check(sound.Played.Count == 0, "switched off: no sound");

            KeyClick.Enabled = true;
            KeyClick.Play();
            KeyClick.Play();
            Check(sound.Played.Count == 2, "switched on: each press plays");
            var wav = sound.Played[0];
            Check(wav.Length > 44 && wav[0] == 'R' && wav[1] == 'I' && wav[2] == 'F' && wav[3] == 'F', "what is played is a WAV");
            Check(ReferenceEquals(sound.Played[0], sound.Played[1]), "and the very same one each time, so it is prepared once");

            sound.Refuse = true;
            bool threw = false;
            try { KeyClick.Play(); } catch { threw = true; }
            Check(!threw, "a sound device that refuses does not stop the key");
        }
        finally
        {
            KeyClick.Enabled = wasOn;
            KeyClick.Use(realSound);
        }
    }

    // ---- start with Windows ----

    private sealed class FakeStartup : IStartupRegistration
    {
        public string? Argument;
        public bool Registered;
        public bool IsRegistered() => Registered;

        public void Register(string autoStartArgument)
        {
            Argument = autoStartArgument;
            Registered = true;
        }

        public void Unregister() => Registered = false;
    }

    private static void Startup()
    {
        var fake = new FakeStartup();
        var real = StartupTask.Use(fake);
        try
        {
            Check(!StartupTask.IsRegistered(), "not registered to begin with");
            StartupTask.Register();
            Check(StartupTask.IsRegistered() && fake.Argument == StartupTask.AutoStartArgument, "registered, with the argument that marks an automatic start");
            StartupTask.Unregister();
            Check(!StartupTask.IsRegistered(), "and taken back");
        }
        finally
        {
            StartupTask.Use(real);
        }
    }

    // ---- opening things ----

    private sealed class FakeOpener : IOpener
    {
        public readonly List<string> Asked = new();
        public void Link(string url) => Asked.Add("link " + url);
        public void File(string path) => Asked.Add("file " + path);
        public void Folder(string folder) => Asked.Add("folder " + folder);
        public void ShowFile(string path) => Asked.Add("show " + path);
    }

    private static void Opening()
    {
        var fake = new FakeOpener();
        var real = Opener.Use(fake);
        try
        {
            Opener.OpenLink("https://example.com");
            Opener.OpenFile(@"C:\a\words.txt");
            Opener.OpenFolder(@"C:\a");
            Opener.ShowFile(@"C:\a\log.txt");
            Check(fake.Asked.SequenceEqual(new[] { "link https://example.com", @"file C:\a\words.txt", @"folder C:\a", @"show C:\a\log.txt" }),
                "a link, a file, a folder and a file to show each reach the system as asked");
        }
        finally
        {
            Opener.Use(real);
        }
    }

    private static bool Within(int ms, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return condition();
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
