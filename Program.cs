using System.Threading.Tasks;

namespace UnboundKeys;

// UnboundKeys' whole runtime lives here: the speech engine and the two
// input hooks feed KeyExecutor, and the WPF dashboard (Wpf/DashboardWindow)
// plus a tray icon are the user's side of it. The dashboard hides rather
// than closing, so the app runs until Quit (the rail's last row, or
// Settings); the tray icon or "press menu" brings the dashboard back.
static class Program
{
    [STAThread]
    static void Main()
    {
        // One copy at a time (see SingleInstance): a second launch asks the
        // running one to show its dashboard and leaves, before any hook is
        // set. After an update the old copy is on its way out, and this one
        // waits for it instead.
        if (!SingleInstance.Claim())
            return;

        // The log is the user's choice (Settings > Action logger), off unless they
        // switched it on. While it is on, anything that goes wrong from
        // here leaves a line in it (see Log), including the errors nothing
        // else catches.
        Log.Enabled = Settings.LoadWriteLog();
        // Likewise the words typed on the on-screen keyboard: kept only if
        // the user switched that on (see WordPredictor.Remember).
        WordPredictor.Remember = Settings.LoadRememberTypedWords();
        Log.Info($"started {typeof(Program).Assembly.GetName().Version} on {Environment.OSVersion.VersionString}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Crash("unhandled", ex);
            // Going down: at least don't leave a key held in the game.
            try { KeyExecutor.ReleaseAll(); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Crash("background task", e.Exception);
            e.SetObserved();
        };

        // Spelled out fully: with both UseWindowsForms (kept for the tray
        // icon) and UseWPF on, a bare "Application" is ambiguous. The
        // dashboard hiding must not end the app, hence explicit shutdown.
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        // The active profile's color theme first, then the shared styles —
        // ThemeSwapper replaces the first entry in place when the theme
        // changes, so the order matters.
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"Themes/Theme.{ThemeMode.Current}.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("Themes/Theme.Controls.xaml", UriKind.Relative) });

        VoiceEngine voice;
        try
        {
            voice = new VoiceEngine();
        }
        catch (Exception ex)
        {
            Log.Crash("starting speech recognition", ex);
            System.Windows.MessageBox.Show(
                $"UnboundKeys couldn't start speech recognition:\n\n{ex.Message}\n\n" +
                "Make sure a microphone is connected and set up under " +
                "Windows Settings > System > Sound.",
                "UnboundKeys",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return;
        }

        using var mouse = new MouseInputWatcher();
        using var physical = new PhysicalKeyWatcher();
        using var tray = new TrayIcon();
        var dashboard = new Wpf.DashboardWindow();

        voice.CommandRecognized += word =>
        {
            if (word == VoiceEngine.StopWord)
            {
                Task.Run(KeyExecutor.ReleaseAll);
                return;
            }
            // Recognized on the microphone's thread; the windows (and the
            // switches that mirror Fade) live on the dispatcher's.
            // "press menu" shows the dashboard, or hides it if it is showing
            // (Fizzil: the second "press menu" should put it away again),
            // the same toggle as the tray icon and the keyboard's Menu key.
            if (word == VoiceEngine.MenuWord)
            {
                app.Dispatcher.InvokeAsync(dashboard.ToggleVisible);
                return;
            }
            if (word == VoiceEngine.FadeWord)
            {
                app.Dispatcher.InvokeAsync(FadeMode.Toggle);
                return;
            }

            var keys = KeyMap.GetAllKeys(word);
            var behavior = KeyMap.Behaviors[word];
            Task.Run(() => KeyExecutor.Execute(word, keys, behavior));
        };

        mouse.ButtonPressed += id =>
        {
            // KeyExecutor tracks in-flight/engaged state per word string —
            // a mouse button id (e.g. "middle") is just as valid a key into
            // that as a spoken word, and the two never collide.
            var keys = MouseMap.GetAllKeys(id);
            var behavior = MouseMap.Behaviors[id];
            Task.Run(() => KeyExecutor.Execute(id, keys, behavior));
        };

        // Two rapid Caps Lock taps on a real keyboard: a panic button that
        // works no matter what is mapped (the same effect as saying "press
        // stop"), and it lifts Fade too, for whenever the screen is too
        // washed out to find the Fade switch. It also shows the dashboard,
        // or hides it if it is showing, the same as the on-screen keyboard
        // Menu key, so a real keyboard has an in-game route to the
        // dashboard as well (Fizzil asked for this). The hook fires this on
        // its own thread; the window lives on the dispatcher.
        physical.StopRequested += () =>
        {
            Task.Run(KeyExecutor.ReleaseAll);
            FadeMode.TurnOff();
            app.Dispatcher.InvokeAsync(dashboard.ToggleVisible);
        };
        physical.VirtualKeyPressed += id =>
        {
            var keys = VirtualKeyMap.GetAllKeys(id);
            var behavior = VirtualKeyMap.Behaviors[id];
            Task.Run(() => KeyExecutor.Execute(id, keys, behavior));
        };

        // The dashboard rail Listening switch pauses the voice keys only.
        // Fizzil wants the mouse buttons and both keyboards to keep working
        // while the mic is ignored (a menu, a chat), so the mouse and
        // physical-key hooks stay up. Only what a spoken word is holding or
        // repeating is released: with the mic paused there is no saying the
        // word again to let go of it, whereas a mouse or keyboard hold can
        // still be ended by pressing its button again.
        ListeningMode.Changed += () =>
        {
            if (ListeningMode.IsPaused)
            {
                voice.Pause();
                foreach (var word in KeyMap.RemappableWords)
                    KeyExecutor.ForceRelease(word);
            }
            else
            {
                voice.Resume();
            }
            tray.SetPaused(ListeningMode.IsPaused);
        };

        tray.Clicked += dashboard.ToggleVisible;
        dashboard.QuitRequested += () => app.Shutdown();
        // Reset app (Settings > Reset): quit, and once everything has shut
        // down and saved for the last time, delete what the app keeps and
        // start it again as a fresh install (see the end of Main).
        bool resetApp = false;
        dashboard.ResetAppRequested += () =>
        {
            resetApp = true;
            app.Shutdown();
        };
        // The first X click: the tray icon says the app is still running.
        dashboard.HiddenByClose += tray.SayStillRunningOnce;

        voice.Start();
        mouse.Start();
        physical.Start();
        // Shortcuts made to an older release open this one from now on (see Shortcuts.cs).
        Task.Run(Shortcuts.PointAtThisVersion);

        // Started by the sign-in task (see StartupTask) rather than a click:
        // honour "start with voice keys paused". And whenever the setting is
        // on, re-point the task at this copy, in case it moved or was
        // updated since the task was made; the Settings switch reports
        // failures, a launch must not.
        bool autoStarted = Environment.GetCommandLineArgs().Contains(StartupTask.AutoStartArgument);
        if (autoStarted && Settings.LoadAutoStartPaused())
            ListeningMode.SetPaused(true);
        if (Settings.LoadStartWithWindows())
            Task.Run(() => { try { StartupTask.Register(); } catch (Exception ex) { Log.Error("refreshing the sign-in task", ex); } });

        // Start as it was left (Fizzil): with the on-screen keyboard open,
        // it comes back where it was, in its Mini or full layout, and the
        // dashboard stays out of the way (the keyboard's Menu key or the
        // tray icon brings it up). Otherwise the dashboard shows, so there
        // is always something on screen.
        if (!dashboard.RestoreKeyboard())
            dashboard.Show();
        app.Run();

        // In case a word (or mouse button) was mid-"infinite hold" when the
        // app was quit — don't leave a real keyboard key stuck down.
        KeyExecutor.ReleaseAll();
        voice.Dispose();
        Log.Info("quit");

        if (resetApp)
            ResetEverythingAndRestart();
    }

    // Everything the app keeps goes: the sign-in task, the keyboard's kept
    // words, the settings (every profile, mapping and switch) with their
    // backup, and the log. Done here, after the windows have closed and
    // saved for the last time, so nothing writes the old state back. The
    // app then starts again and finds a fresh install. UpdateChecker.Launch
    // because this process is elevated and the new one must be too; the new
    // copy waits for this one to leave (see SingleInstance).
    private static void ResetEverythingAndRestart()
    {
        try
        {
            StartupTask.Unregister();
        }
        catch
        {
            // It would not go; the wiped settings no longer ask for it, and
            // the Settings switch can remove it later.
        }
        WordPredictor.ClearLearned();
        Settings.DeleteEverything();
        Log.DeleteFiles();

        string? exe = Environment.ProcessPath;
        if (exe != null)
            UpdateChecker.Launch(exe);
    }
}
