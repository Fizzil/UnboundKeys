namespace UnboundKeys;

// Windows' side of the plug-in points (see KeyOutput.cs, Pointer.cs and
// ActiveWindow.cs): each passes straight to NativeInput, where the calls
// into Windows are.

internal sealed class WindowsKeySender : IKeySender
{
    public void KeyDown(ushort key, bool extended) => NativeInput.KeyDown(key, extended);
    public void KeyUp(ushort key, bool extended) => NativeInput.KeyUp(key, extended);
}

internal sealed class WindowsPointer : IPointer
{
    public (int X, int Y) Position => NativeInput.GetPointer();
    public void MoveTo(int x, int y) => NativeInput.MovePointerTo(x, y);
    public (int Left, int Top, int Width, int Height) ScreenAt(int x, int y) => NativeInput.MonitorBoundsAt(x, y);
    public bool LeftButtonDown => NativeInput.IsLeftButtonDown();
}

// A WindowId on Windows is the window's handle.
internal sealed class WindowsActiveWindow : IActiveWindow
{
    public WindowId Current => new((long)NativeInput.GetFocusedWindow());
    public string TitleOf(WindowId window) => NativeInput.GetWindowTitle((IntPtr)window.Value);
}
