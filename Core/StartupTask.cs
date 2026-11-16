namespace UnboundKeys;

// "Start with Windows": the app asks to be started when the user signs
// in, and takes that back. How is the system's business
// (IStartupRegistration: a scheduled task on Windows, see
// WindowsStartupTask). Program.cs registers afresh on every launch while
// the setting is on, so the registration always points at the copy that
// last ran (Check for updates changes the exe's folder).

// The system's side: start this exe at sign-in, with the argument given.
internal interface IStartupRegistration
{
    bool IsRegistered();
    void Register(string autoStartArgument);
    void Unregister();
}

public static class StartupTask
{
    // Passed at an automatic start so Program.cs can tell it from a click
    // on the exe (the "start paused" preference applies only here).
    public const string AutoStartArgument = "--autostart";

    // Set by the system's side before anything else runs (WindowsPlatform.
    // Install on Windows): this project knows no system of its own.
    private static IStartupRegistration _registration = null!;

    // Swaps the registration in use and hands back the old one (the tests).
    internal static IStartupRegistration Use(IStartupRegistration registration)
    {
        var old = _registration;
        _registration = registration;
        return old;
    }

    public static bool IsRegistered() => _registration.IsRegistered();
    public static void Register() => _registration.Register(AutoStartArgument);
    public static void Unregister() => _registration.Unregister();
}
