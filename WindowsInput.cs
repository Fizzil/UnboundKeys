namespace UnboundKeys;

// Windows' side of the two plug-in points (see KeyOutput.cs, Pointer.cs):
// both pass straight to NativeInput, where the SendInput calls are.

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
