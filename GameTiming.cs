namespace UnboundKeys;

// The active sub-profile's global cooldown, held where KeyExecutor can
// read it on every repeated key without touching the settings file. A
// sub-profile is a class (Fizzil's WoW setup), so the GCD lives there:
// set it once and every infinite repeat in that sub-profile waits one
// GCD between keys, unless a mapping sets its own gap
// (KeyBehavior.RepeatGapSeconds). Reloaded on every profile or
// sub-profile switch; 0 means not set, so repeats use the usual 0.1 s.
public static class GameTiming
{
    public static double GcdSeconds { get; internal set; }

    public static void Reload() =>
        GcdSeconds = Settings.LoadGcdSeconds(KeyMap.ActiveProfile);

    public static void Set(double seconds)
    {
        GcdSeconds = seconds;
        Settings.SaveGcdSeconds(KeyMap.ActiveProfile, seconds);
    }
}
