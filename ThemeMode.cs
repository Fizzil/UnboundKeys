namespace VoicePress;

// Which named color palette (see Theme.ByName) is currently active —
// "Red", "Green", or "Blue". Picked from the overlay icon's color popup
// (four rapid right-clicks — see OverlayForm), saved per profile the same
// way KeyMap/MouseMap/PhysicalKeyMap/PressMode all are, so switching
// profiles can carry a different color with it.
public static class ThemeMode
{
    public const string DefaultName = "Red";

    public static string Current { get; private set; } = Settings.LoadThemeColor(KeyMap.ActiveProfile, DefaultName);

    // Raised only when the active color actually changes (not on every
    // call) — OverlayForm subscribes to rebuild the icon images and close
    // the dashboard if one's open, since neither picks up a new Theme.Current
    // on its own (colors are read once, at construction).
    public static event Action? Changed;

    static ThemeMode()
    {
        Theme.Current = Theme.ByName(Current);
    }

    // Called by the overlay icon's color popup.
    public static void SwitchTo(string name)
    {
        if (Current == name)
            return;

        Current = name;
        Theme.Current = Theme.ByName(name);
        Settings.SaveThemeColor(KeyMap.ActiveProfile, name);
        Changed?.Invoke();
    }

    // Called by DashboardForm.SwitchToProfile alongside KeyMap/MouseMap/
    // PhysicalKeyMap/PressMode's own SwitchProfile — loads the new
    // profile's saved color. No save here: this is loading an already-
    // saved value, not setting a new one.
    public static void SwitchProfile(string profileName)
    {
        var name = Settings.LoadThemeColor(profileName, DefaultName);
        if (Current == name)
            return;

        Current = name;
        Theme.Current = Theme.ByName(name);
        Changed?.Invoke();
    }
}
