namespace UnboundKeys;

// Where a press of a Keyboard-page key arrives from either keyboard — the
// real one (PhysicalKeyWatcher, a taken-over key's first down) or the
// on-screen one (VirtualKeyboardWindow, a click) — and is told apart as a
// single or a double press (PressGestures) before Program.cs runs the
// mapping it names. One detector for both keyboards, so the window is the
// same whichever was used.
internal static class KeyPresses
{
    // Fired with the mapping's id ("va", or "va.double" for a double
    // press; see Gestures) once a press is decided — never for a double
    // press with no key picked for it.
    public static event Action<string>? Pressed;

    private static readonly PressGestures _gestures = new(VirtualKeyMap.IsMapped, id => Pressed?.Invoke(id));

    public static void Press(string keyId) => _gestures.Press(keyId);
}
