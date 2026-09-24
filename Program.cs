using System.Threading.Tasks;

namespace UnboundKeys;

// UnboundKeys' whole runtime lives here: the speech engine and the two
// input hooks feed KeyExecutor, and the WPF dashboard (Wpf/DashboardWindow)
// plus a tray icon are the user's side of it. The dashboard hides rather
// than closing, so the app runs until Quit (in the dashboard's Settings);
// the tray icon or "press menu" brings the dashboard back.
static class Program
{
    [STAThread]
    static void Main()
    {
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
            if (word == VoiceEngine.MenuWord)
            {
                app.Dispatcher.InvokeAsync(dashboard.ShowDashboard);
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
        // works no matter what's mapped — same effect as saying "press
        // stop" — and it lifts Fade too, for whenever the screen's too
        // washed out to find the Fade switch.
        physical.StopRequested += () =>
        {
            Task.Run(KeyExecutor.ReleaseAll);
            FadeMode.TurnOff();
        };
        physical.VirtualKeyPressed += id =>
        {
            var keys = VirtualKeyMap.GetAllKeys(id);
            var behavior = VirtualKeyMap.Behaviors[id];
            Task.Run(() => KeyExecutor.Execute(id, keys, behavior));
        };

        // The dashboard's Listening switch. Pausing also releases anything
        // mid-hold/repeat — with listening off there'd be no way to say the
        // word (or press the button) again to let go of it.
        ListeningMode.Changed += () =>
        {
            if (ListeningMode.IsPaused)
            {
                voice.Pause();
                mouse.Pause();
                physical.Pause();
                KeyExecutor.ReleaseAll();
            }
            else
            {
                voice.Resume();
                mouse.Resume();
                physical.Resume();
            }
            tray.SetPaused(ListeningMode.IsPaused);
        };

        tray.Clicked += dashboard.ToggleVisible;
        dashboard.QuitRequested += () => app.Shutdown();

        voice.Start();
        mouse.Start();
        physical.Start();

        dashboard.Show();
        app.Run();

        // In case a word (or mouse button) was mid-"infinite hold" when the
        // app was quit — don't leave a real keyboard key stuck down.
        KeyExecutor.ReleaseAll();
        voice.Dispose();
    }
}
