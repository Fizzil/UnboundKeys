namespace UnboundKeys;

// Windows' answer to every plug-in point Core leaves open, put in place in
// one go. The app calls this first thing (Program.Main), the harness too:
// Core has no system of its own, and until this has run it can neither
// press a key nor see a window.
//
// Three plug-in points are not here because they are handed over where
// they are made, in Program.Main: the input hooks (WindowsInputHooks), the
// tray icon (WindowsTray) and the microphone (WinMmMicrophone).
internal static class WindowsPlatform
{
    public static void Install()
    {
        KeyOutput.Use(new WindowsKeySender());
        Pointer.Use(new WindowsPointer());
        ActiveWindow.Use(new WindowsActiveWindow());
        KeyClick.Use(new WindowsSoundOut());
        Opener.Use(new WindowsOpener());
        StartupTask.Use(new WindowsStartupTask());
        VirtualPadDriver.Use(new ViGEmDriver());
    }
}
