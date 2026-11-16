namespace UnboundKeys;

// What the app needs to know about the mouse pointer and to do with it,
// in screen pixels. Steering is the one user. The one implementation
// today is Windows' (WindowsPointer).
internal interface IPointer
{
    (int X, int Y) Position { get; }

    // Puts the pointer at a screen pixel, as the app's own move (the mouse
    // hook must not mistake it for the user's hand).
    void MoveTo(int x, int y);

    // The rectangle of the screen a point is on.
    (int Left, int Top, int Width, int Height) ScreenAt(int x, int y);

    // Whether the real left button is down right now.
    bool LeftButtonDown { get; }
}

internal static class Pointer
{
    // Set by the system's side before anything else runs (WindowsPlatform.
    // Install on Windows): this project knows no system of its own.
    private static IPointer _pointer = null!;

    // The harness swaps in a pointer of its own; returns the one replaced.
    internal static IPointer Use(IPointer pointer)
    {
        var old = _pointer;
        _pointer = pointer;
        return old;
    }

    public static (int X, int Y) Position => _pointer.Position;
    public static void MoveTo(int x, int y) => _pointer.MoveTo(x, y);
    public static (int Left, int Top, int Width, int Height) ScreenAt(int x, int y) => _pointer.ScreenAt(x, y);
    public static bool LeftButtonDown => _pointer.LeftButtonDown;
}
