namespace VoicePress;

// Which of Voice Press or Physical Press is currently "live" — an
// able-bodied-friendly feature: unlike Mouse (always independently
// active alongside Press), Voice and Physical are mutually exclusive by
// design. Whichever isn't Active does nothing at all — a spoken word is
// simply not acted on, and a physical number-row key passes straight
// through untouched (see Program.cs and PhysicalKeyWatcher) — until you
// switch back to it from the dashboard's Press selector.
public static class PressMode
{
    private const string Voice = "voice";
    private const string Physical = "physical";

    // Defaults to Voice — an existing profile saved before this feature
    // existed has no saved value, and Voice-only was the app's entire
    // behavior up to that point, so defaulting any other way would look
    // like Physical had silently taken over on first launch.
    public static string Active { get; private set; } = Settings.LoadActivePressMode(KeyMap.ActiveProfile, Voice);

    // Called by the dashboard's Voice Press/Physical Press selector
    // buttons. Whichever source is being switched away from can no longer
    // hear a second press/word to toggle off whatever it left running, so
    // this also releases that source's own engaged state — same reasoning
    // as every other safety net in the app, just scoped to one source
    // instead of everything.
    public static void SwitchTo(string mode)
    {
        if (Active == mode)
            return;

        string previous = Active;
        Active = mode;
        Settings.SaveActivePressMode(KeyMap.ActiveProfile, mode);

        if (previous == Voice)
            KeyExecutor.ReleaseVoiceWords();
        else
            KeyExecutor.ReleasePhysicalKeys();
    }

    // Called by DashboardForm.SwitchToProfile alongside KeyMap/MouseMap/
    // PhysicalKeyMap's own SwitchProfile — loads the new profile's saved
    // mode. No release needed here: KeyMap.SwitchProfile already calls
    // KeyExecutor.ReleaseAll before this runs, so there's nothing of the
    // old profile's left engaged to clean up.
    public static void SwitchProfile(string profileName) =>
        Active = Settings.LoadActivePressMode(profileName, Voice);
}
