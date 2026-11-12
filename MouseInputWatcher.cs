namespace UnboundKeys;

// Decides what each real mouse event means (the events come from the
// system through IInputHooks, see InputHooks.cs): for whichever of the six
// remappable buttons (see MouseCatalog) currently has a key assigned in
// MouseMap, for either way of pressing it, the real click is swallowed and
// the press handed to PressGestures, which decides whether it was a single
// or a double press and fires ButtonPressed with that mapping's id, so
// Program.cs can run it through KeyExecutor exactly like a recognized
// voice word. Pointer moves go to Steering while that is on (see
// Steering.cs), which may swallow one that crosses its line.
//
// The left button never reaches this class at all (see MouseEvent): it
// isn't one of MouseCatalog's six buttons, so it can never be looked up as
// "enabled" and never gets suppressed. That's what keeps a normal click
// always available as an escape hatch, no matter what this class does.
public sealed class MouseInputWatcher : IDisposable
{
    // Fired with the mapping's id (e.g. "middle", or "middle.double" for a
    // double press; see Gestures) once a press is decided — never for a
    // way of pressing that has no key assigned.
    public event Action<string>? ButtonPressed;

    private readonly PressGestures _gestures;
    private readonly OwnWindowClicks _ownClicks = new();
    private readonly Func<int, int, bool> _isOwnWindowAt;

    // isOwnWindowAt: whether the window under a screen pixel is one of
    // UnboundKeys' own (the system's to answer).
    public MouseInputWatcher(Func<int, int, bool> isOwnWindowAt)
    {
        _isOwnWindowAt = isOwnWindowAt;
        _gestures = new PressGestures(
            id => MouseMap.Enabled.TryGetValue(id, out bool on) && on,
            id => ButtonPressed?.Invoke(id));
    }

    // One real mouse event; true to swallow it. Never called for the app's
    // own clicks and pointer moves (IInputHooks leaves those out), so a
    // word or button mapped to click another mapped button cannot set
    // itself off.
    internal bool Handle(MouseEvent e)
    {
        // Steering (see Steering.cs): every real pointer move, while it is
        // on, in screen pixels. A move past the band's line is swallowed
        // and the pointer put on the line instead; any other move passes.
        if (e.Kind == MouseEventKind.Move)
            return Steering.IsOn && Steering.PointerMoved(e.X, e.Y);

        string? button = e.Button;
        if (button == null)
            return false;

        // A right-click over one of UnboundKeys' own windows is the
        // window's (Fizzil, 2026-11-11: right-click is Back in the
        // dashboard), even with the right button mapped, and its up goes
        // wherever its down went (see OwnWindowClicks).
        if (button == "right")
        {
            if (e.Kind == MouseEventKind.Down && _ownClicks.Down(_isOwnWindowAt(e.X, e.Y)))
                return false;
            if (e.Kind == MouseEventKind.Up && _ownClicks.Up())
                return false;
        }

        // A button with a key on either way of pressing it is taken over
        // whole: its ordinary click can no longer pass through, because
        // whether a double press is coming cannot be known yet.
        if (!MouseMap.AnyGestureEnabled(button))
            return false;

        // The press runs the mapping. The release is only swallowed: a
        // swallowed button-down with its button-up let through would look
        // to the program in front like a spurious click of its own. (A
        // wheel notch has no release.)
        if (e.Kind == MouseEventKind.Down)
            _gestures.Press(button);
        return true;
    }

    public void Dispose() => _gestures.Dispose();
}

// Keeps a right-click's two halves together (see Handle above): told
// whether a button-down is over one of UnboundKeys' own windows, it says
// whether the down goes to that window, and then whether the matching up
// does: wherever the down went, even if the pointer has moved off the
// window since, so nothing ever sees half a click.
public sealed class OwnWindowClicks
{
    private bool _downWentToOwnWindow;

    public bool Down(bool overOwnWindow)
    {
        _downWentToOwnWindow = overOwnWindow;
        return overOwnWindow;
    }

    public bool Up()
    {
        bool toOwnWindow = _downWentToOwnWindow;
        _downWentToOwnWindow = false;
        return toOwnWindow;
    }
}
