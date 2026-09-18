using System.Threading.Tasks;

namespace VoicePress;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        VoiceEngine voice;
        try
        {
            voice = new VoiceEngine();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"VoicePress couldn't start speech recognition:\n\n{ex.Message}\n\n" +
                "Make sure a microphone is connected and set up under " +
                "Windows Settings > Time & Language > Speech.",
                "VoicePress",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        voice.CommandRecognized += word =>
        {
            if (word == VoiceEngine.StopWord)
            {
                // Always works regardless of PressMode below — a safety
                // net that only covers voice/mouse/physical while one of
                // them isn't recognized at all would defeat the point of
                // being a safety net, for whenever clicking the overlay to
                // pause isn't an option.
                Task.Run(KeyExecutor.ReleaseAll);
                return;
            }

            // Voice and Physical are mutually exclusive (see PressMode) —
            // an able-bodied-friendly feature, unlike Mouse, which is
            // always independently active alongside whichever of these two
            // currently is. A recognized word while Physical is active is
            // simply not acted on.
            if (PressMode.Active != "voice")
                return;

            var keys = KeyMap.GetAllKeys(word);
            var behavior = KeyMap.Behaviors[word];
            Task.Run(() => KeyExecutor.Execute(word, keys, behavior));
        };

        using var mouse = new MouseInputWatcher();
        mouse.ButtonPressed += id =>
        {
            // KeyExecutor tracks in-flight/engaged state per word string —
            // a mouse button id (e.g. "middle") is just as valid a key into
            // that as a spoken word, and the two never collide.
            var keys = MouseMap.GetAllKeys(id);
            var behavior = MouseMap.Behaviors[id];
            Task.Run(() => KeyExecutor.Execute(id, keys, behavior));
        };

        using var physical = new PhysicalKeyWatcher();
        physical.KeyPressed += id =>
        {
            var keys = PhysicalKeyMap.GetAllKeys(id);
            var behavior = PhysicalKeyMap.Behaviors[id];
            Task.Run(() => KeyExecutor.Execute(id, keys, behavior));
        };
        // Three rapid Caps Lock taps: a panic button that works no matter
        // which Press source is active, or even if nothing at all is
        // mapped — same effect as saying "press stop".
        physical.StopRequested += () => Task.Run(KeyExecutor.ReleaseAll);

        voice.Start();
        mouse.Start();
        physical.Start();

        using var overlay = new OverlayForm(voice, mouse, physical);
        Application.Run(overlay);

        // In case a word (or mouse button) was mid-"infinite hold" when the
        // app was closed — don't leave a real keyboard key stuck down.
        KeyExecutor.ReleaseAll();

        voice.Dispose();
    }
}
