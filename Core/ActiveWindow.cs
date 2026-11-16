namespace UnboundKeys;

// A window, as far as the app's logic cares: something to compare with
// another. What the number is belongs to the system (on Windows, the
// window's handle); None is no window at all.
internal readonly record struct WindowId(long Value)
{
    public static readonly WindowId None = default;
}

// What the app needs to know about the window in front: which one it is,
// and its title. The safety nets use it to let go of a held key when the
// game stops being the window in front (KeyExecutor) and to end steering
// (Steering). The one implementation today is Windows' (WindowsActiveWindow).
internal interface IActiveWindow
{
    WindowId Current { get; }
    string TitleOf(WindowId window);
}

internal static class ActiveWindow
{
    // Set by the system's side before anything else runs (WindowsPlatform.
    // Install on Windows): this project knows no system of its own.
    private static IActiveWindow _windows = null!;

    // The harness swaps in one of its own; returns the one replaced.
    internal static IActiveWindow Use(IActiveWindow windows)
    {
        var old = _windows;
        _windows = windows;
        return old;
    }

    public static WindowId Current => _windows.Current;
    public static string TitleOf(WindowId window) => _windows.TitleOf(window);
}
