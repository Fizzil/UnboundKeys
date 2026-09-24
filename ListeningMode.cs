namespace UnboundKeys;

// Whether UnboundKeys is currently paused — no voice commands, no mouse
// remaps, no intercepted keys — as one shared on/off switch, the same
// shape as FadeMode. The dashboard's Listening toggle flips it; the
// overlay owns what pausing actually does (stopping the voice engine and
// the hooks, releasing every held key, swapping its icon) by subscribing
// to Changed, so the dashboard never needs a reference to the overlay.
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
