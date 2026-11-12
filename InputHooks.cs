namespace UnboundKeys;

// What the app needs to hear from the real mouse and keyboard, system-wide,
// and the one thing it needs to say back: whether to swallow the event, so
// the program in front never sees it. The one implementation today is
// Windows' (WindowsInputHooks); MouseInputWatcher and PhysicalKeyWatcher
// decide what each event means and know nothing of where it came from.

internal enum MouseEventKind
{
    Move,
    // A wheel notch is a Down with no Up to follow.
    Down,
    Up,
}

// Button is one of MouseCatalog's ids ("right", "middle", "x1", "x2",
// "wheelup", "wheeldown"), null for a Move. The left button never comes:
// it is never the app's to touch. X and Y are screen pixels.
internal readonly record struct MouseEvent(MouseEventKind Kind, string? Button, int X, int Y);

// Key is the key's code (a Windows virtual-key code, as everywhere else in
// the app for now). A key held down keeps sending Down, as a keyboard does.
internal readonly record struct KeyEvent(ushort Key, bool Down);

internal interface IInputHooks : IDisposable
{
    // Each handler returns true to swallow the event. They are called on
    // the hooks' own thread, one event at a time, and never for anything
    // the app itself sent. They must be quick: the system waits on them.
    void Start(Func<MouseEvent, bool> onMouse, Func<KeyEvent, bool> onKey);
}
