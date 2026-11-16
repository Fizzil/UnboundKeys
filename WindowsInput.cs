namespace UnboundKeys;

// Windows' side of the plug-in points (see KeyOutput.cs, Pointer.cs and
// ActiveWindow.cs): each passes straight to NativeInput, where the calls
// into Windows are.

internal sealed class WindowsKeySender : IKeySender
{
    public void KeyDown(Key key) => NativeInput.KeyDown(WindowsKeys.ToVk(key), WindowsKeys.IsExtended(key));
    public void KeyUp(Key key) => NativeInput.KeyUp(WindowsKeys.ToVk(key), WindowsKeys.IsExtended(key));
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

// Key names to Windows' virtual-key codes and back. Short, because the
// numbers behind the names are Windows' own (see Key): the translation is
// the number itself. What Windows needs beyond the number lives here too.
internal static class WindowsKeys
{
    public static ushort ToVk(Key key) => (ushort)key;
    public static Key FromVk(uint vk) => (Key)(ushort)vk;

    // SendInput needs to know which keys are part of the nav cluster /
    // extended set (arrows, insert/delete, page up/down, home/end, the
    // Windows keys) so it can set the right flag — otherwise those keys
    // don't register correctly.
    public static bool IsExtended(Key key) => key switch
    {
        Key.PageUp or Key.PageDown or Key.End or Key.Home => true,
        Key.Left or Key.Up or Key.Right or Key.Down => true,
        Key.Insert or Key.Delete => true,
        Key.Win or Key.RightWin => true,
        _ => false,
    };
}
