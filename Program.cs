using System.Threading.Tasks;

namespace UnboundKeys;

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
                $"UnboundKeys couldn't start speech recognition:\n\n{ex.Message}\n\n" +
                "Make sure a microphone is connected and set up under " +
                "Windows Settings > Time & Language > Speech.",
                "UnboundKeys",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        voice.CommandRecognized += word =>
        {
            if (word == VoiceEngine.StopWord)
            {
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

        // Its own Caps Lock double-tap panic button, plus mirroring any
        // customized virtual-keyboard digit/letter onto the real physical
        // key — see PhysicalKeyWatcher's own class comment.
        using var physical = new PhysicalKeyWatcher();
        // Two rapid Caps Lock taps: a panic button that works no matter
        // what's mapped, or even if nothing at all is — same effect as
        // saying "press stop". Also releases Fade (directly, not via
        // Task.Run — this lambda already runs on the UI thread, same as
        // the hook callback that fires it, and FadeMode.Changed's handlers
        // touch Form.Opacity/Controls, which needs to happen there) — a
        // safety net for whenever the screen's too washed out to find the
        // dashboard's own Fade button.
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
