namespace UnboundKeys;

// Whether the voice keys are paused, as one shared on/off switch the same
// shape as FadeMode. The dashboard rail Listening toggle flips it;
// Program.cs owns what pausing actually does (pausing the voice engine,
// releasing whatever a spoken word was holding, dimming the tray icon) by
// subscribing to Changed. Mouse remaps and both keyboards are deliberately
// unaffected: pausing exists to mute the voice keys in a menu or a chat
// without losing the mouse buttons.
public static class ListeningMode
{
    public static bool IsPaused { get; private set; }

    public static event Action? Changed;

    public static void Toggle() => SetPaused(!IsPaused);

    public static void SetPaused(bool paused)
    {
        if (IsPaused == paused)
            return;
        IsPaused = paused;
        Changed?.Invoke();
    }
}
