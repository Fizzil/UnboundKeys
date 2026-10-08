namespace UnboundKeys;

// The physical mouse buttons UnboundKeys can remap. Left Button is
// deliberately excluded — it's the one gesture that always opens the
// dashboard (see OverlayForm), specifically so there's never a mapping
// that can lock you out of reaching it, with no way to undo that short of
// unplugging the mouse. The other six (Right Button included) are safe to
// remap: none of them are needed to operate UnboundKeys itself anymore.
public static class MouseCatalog
{
    // ShortLabel is what actually fits on the sub-tab button itself (the
    // strip only has room for a handful of pixels per button) — Label is
    // the full name, used as that button's tooltip.
    public readonly record struct ButtonInfo(string Id, string Label, string ShortLabel);

    public static readonly ButtonInfo[] Buttons =
    {
        new("right", "Right Button", "RM"),
        new("middle", "Middle Button", "MM"),
        new("x1", "Mouse Button 4", "M4"),
        new("x2", "Mouse Button 5", "M5"),
        new("wheelup", "Wheel Up", "W↑"),
        new("wheeldown", "Wheel Down", "W↓"),
    };

    // Multi-mode (Fizzil, 2026-10-08, after Helldivers 2's button options):
    // a button can do three things, one per way of pressing it. Each way
    // is a whole mapping of its own — keys, Mode, Infinite — under its own
    // id: "middle" for a single press (the id a button always had, so an
    // older file reads exactly as before), "middle.double" for a double
    // press and "middle.long" for a long press. To MouseMap, the editor,
    // the settings file and KeyExecutor these are ordinary ids, which is
    // what makes a double press of a button whose single press is holding
    // a key bump that hold, as a second button would. Which way of
    // pressing was meant is decided by MouseGestures, from the hook's
    // downs and ups. A wheel notch cannot be held, so the wheel has two.
    public enum Gesture { Single, Double, Long }

    public static readonly Gesture[] Gestures = { Gesture.Single, Gesture.Double, Gesture.Long };

    public static string IdFor(string buttonId, Gesture gesture) => gesture switch
    {
        Gesture.Double => buttonId + ".double",
        Gesture.Long => buttonId + ".long",
        _ => buttonId,
    };

    public static (string ButtonId, Gesture Gesture) Split(string id)
    {
        if (id.EndsWith(".double", StringComparison.Ordinal))
            return (id[..^".double".Length], Gesture.Double);
        if (id.EndsWith(".long", StringComparison.Ordinal))
            return (id[..^".long".Length], Gesture.Long);
        return (id, Gesture.Single);
    }

    public static bool IsWheel(string buttonId) => buttonId.StartsWith("wheel", StringComparison.Ordinal);

    // Whether a button can be pressed this way: every way for a button,
    // all but Long for a wheel notch.
    public static bool Has(string buttonId, Gesture gesture) => gesture != Gesture.Long || !IsWheel(buttonId);

    public static string GestureLabel(Gesture gesture) => gesture switch
    {
        Gesture.Double => "Double press",
        Gesture.Long => "Long press",
        _ => "Single press",
    };

    public static string LabelFor(string buttonId)
    {
        foreach (var button in Buttons)
            if (button.Id == buttonId)
                return button.Label;
        return buttonId;
    }

    // The other ways of pressing the same button, as mapping ids — what
    // a press stops before it starts its own (see Program.cs).
    public static List<string> SiblingIds(string id)
    {
        var (buttonId, gesture) = Split(id);
        var ids = new List<string>();
        foreach (var other in Gestures)
            if (other != gesture && Has(buttonId, other))
                ids.Add(IdFor(buttonId, other));
        return ids;
    }

    // Every mapping id there is, button by button: "right", "right.double",
    // "right.long", "middle", ... — what MouseMap keys its dictionaries by.
    // Declared after Buttons: static initializers run in order.
    public static readonly string[] AllIds = BuildAllIds();

    private static string[] BuildAllIds()
    {
        var ids = new List<string>();
        foreach (var button in Buttons)
            foreach (var gesture in Gestures)
                if (Has(button.Id, gesture))
                    ids.Add(IdFor(button.Id, gesture));
        return ids.ToArray();
    }
}
