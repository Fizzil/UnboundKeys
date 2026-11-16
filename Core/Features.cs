namespace UnboundKeys;

// The optional features (Settings > Features, Fizzil 2026-11-12): things
// not everyone wants, each one switch, app-wide. Switched off, a feature
// leaves the dashboard altogether and does nothing; what was set up for it
// is kept and comes back with the switch. Steering is the first.
public static class Features
{
    public static bool SteeringEnabled { get; private set; } = Settings.LoadSteeringFeature();

    public static event Action? Changed;

    public static void SetSteering(bool on)
    {
        if (SteeringEnabled == on)
            return;
        SteeringEnabled = on;
        Settings.SaveSteeringFeature(on);
        // Off lets go of everything, as Stop does: steering itself and any
        // pedal a mapping is holding. Off the UI thread, like the rail's Stop.
        if (!on)
            Task.Run(KeyExecutor.ReleaseAll);
        Changed?.Invoke();
    }
}
