namespace UnboundKeys;

// Whether the dashboard, the virtual keyboard, and the listener icon are
// currently faded down to mostly see-through — a Windows on-screen-
// keyboard feature (its own "Fade" option), toggled from the dashboard's
// own Fade button (a permanent fixture there, not the keyboard — Fizzil
// wanted it reachable regardless of whether the keyboard happens to be
// open). A single shared on/off switch rather than one per window: turning
// it on fades whichever windows happen to be open right now (see
// OverlayForm), and a freshly-opened one picks up the current state
// immediately rather than always starting fully opaque.
public static class FadeMode
{
    // 80% faded, i.e. 20% opaque — matches the "by like eighty percent"
    // ask; still fully clickable either way, Form.Opacity only affects
    // how it looks, not whether it accepts input.
    public const double FadedOpacity = 0.2;

    public static bool IsOn { get; private set; }

    public static event Action? Changed;

    public static void Toggle()
    {
        IsOn = !IsOn;
        Changed?.Invoke();
    }

    // Releases fade specifically (as opposed to Toggle, which could just as
    // easily turn it back ON) — wired into both Caps Lock double-tap panic
    // buttons (the real one in PhysicalKeyWatcher, and the on-screen one in
    // VirtualKeyboardForm) as a safety net: faded-and-can't-see-the-screen-
    // well-enough-to-find-the-Fade-button is exactly the situation a panic
    // gesture needs to recover from. A no-op while already off, so it never
    // fires Changed (and the Opacity churn that comes with it) needlessly —
    // every panic tap ends up calling this regardless of whether Fade
    // actually needs releasing.
    public static void TurnOff()
    {
        if (!IsOn)
            return;
        IsOn = false;
        Changed?.Invoke();
    }
}
