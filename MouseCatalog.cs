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

    public static string LabelFor(string buttonId)
    {
        foreach (var button in Buttons)
            if (button.Id == buttonId)
                return button.Label;
        return buttonId;
    }

    // Every mapping id there is, button by button: "right", "right.double",
    // "middle", ... (see Gestures) — what MouseMap keys its dictionaries by.
    // Declared after Buttons: static initializers run in order.
    public static readonly string[] AllIds = Gestures.AllIds(Buttons.Select(b => b.Id));
}
