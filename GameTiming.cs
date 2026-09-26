namespace UnboundKeys;

// The active sub-profile's game timing and game keys, held where
// KeyExecutor can read them on every repeated key without touching the
// settings file. A sub-profile is a class (Fizzil's WoW setup), so they
// live there. The GCD: set it once and every infinite repeat in that
// sub-profile waits one GCD between keys, unless a mapping sets its own
// gap (KeyBehavior.RepeatGapSeconds); 0 means not set, so repeats use
// the usual 0.1 s. The game keys are what Farm nearby presses around a
// mapping's own keys (see KeyExecutor.RunFarmLoop): Target nearest enemy
// (0 = Tab, the game's default) and Interact with target (0 = not set, so
// a farm loop skips looting). Reloaded on every profile or sub-profile
// switch.
public static class GameTiming
{
    public const ushort DefaultTargetVk = 0x09; // Tab

    public static double GcdSeconds { get; internal set; }
    public static ushort TargetVk { get; internal set; }
    public static ushort InteractVk { get; internal set; }

    public static ushort EffectiveTargetVk => TargetVk != 0 ? TargetVk : DefaultTargetVk;

    public static void Reload()
    {
        GcdSeconds = Settings.LoadGcdSeconds(KeyMap.ActiveProfile);
        (TargetVk, InteractVk) = Settings.LoadGameKeys(KeyMap.ActiveProfile);
    }

    public static void Set(double seconds)
    {
        GcdSeconds = seconds;
        Settings.SaveGcdSeconds(KeyMap.ActiveProfile, seconds);
    }

    public static void SetGameKeys(ushort targetVk, ushort interactVk)
    {
        TargetVk = targetVk;
        InteractVk = interactVk;
        Settings.SaveGameKeys(KeyMap.ActiveProfile, targetVk, interactVk);
    }
}
