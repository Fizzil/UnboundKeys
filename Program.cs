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
            var vk = KeyMap.Words[word];
            var extended = KeyMap.ExtendedKeys.Contains(vk);
            NativeInput.TapKey(vk, extended);
        };

        voice.Start();

        using var overlay = new OverlayForm(voice);
        Application.Run(overlay);

        voice.Dispose();
    }
}
