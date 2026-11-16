namespace UnboundKeys;

// Which named color palette (Themes/Theme.Red|Green|Blue.xaml) is
// currently active. Picked from the dashboard's Settings page, saved per
// profile the same way KeyMap/MouseMap/VirtualKeyMap all are, so
// switching profiles can carry a different color with it.
public static class ThemeMode
{
    public const string DefaultName = "Red";

    public static string Current { get; private set; } = Settings.LoadThemeColor(KeyMap.ActiveProfile, DefaultName);

    // Raised only when the active color actually changes (not on every
    // call) — the dashboard subscribes to swap the merged color dictionary
    // (see Themes/ThemeSwapper), which re-themes every window live.
    public static event Action? Changed;

    // Called by the Settings page's swatches.
    public static void SwitchTo(string name)
    {
        if (Current == name)
            return;

        Current = name;
        Settings.SaveThemeColor(KeyMap.ActiveProfile, name);
        Changed?.Invoke();
    }

    // Called alongside KeyMap/MouseMap/VirtualKeyMap's own SwitchProfile —
    // loads the new profile's saved color. No save here: this is loading
    // an already-saved value, not setting a new one.
    public static void SwitchProfile(string profileName)
    {
        var name = Settings.LoadThemeColor(profileName, DefaultName);
        if (Current == name)
            return;

        Current = name;
        Changed?.Invoke();
    }
}
