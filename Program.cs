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
                // Voice-only safety net: releases every currently-engaged
                // infinite hold/repeat, for when clicking the overlay to
                // pause isn't an option.
                Task.Run(KeyExecutor.ReleaseAll);
                return;
            }

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

        voice.Start();
        mouse.Start();

        using var overlay = new OverlayForm(voice, mouse);
        Application.Run(overlay);

        // In case a word (or mouse button) was mid-"infinite hold" when the
        // app was closed — don't leave a real keyboard key stuck down.
        KeyExecutor.ReleaseAll();

        voice.Dispose();
    }
}
