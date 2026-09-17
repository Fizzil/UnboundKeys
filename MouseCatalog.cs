namespace VoicePress;

// The physical mouse buttons VoicePress can remap. Left Button is
// deliberately excluded — remapping it could suppress the very click needed
// to pause VoicePress (or use the dashboard, or anything else), with no way
// to undo that short of unplugging the mouse. The other six are safe: none
// of them are needed to operate VoicePress itself.
public static class MouseCatalog
{
    public readonly record struct ButtonInfo(string Id, string Label);

    public static readonly ButtonInfo[] Buttons =
    {
        new("right", "Right Button"),
        new("middle", "Middle Button"),
        new("x1", "Mouse Button 4"),
        new("x2", "Mouse Button 5"),
        new("wheelup", "Wheel Up"),
        new("wheeldown", "Wheel Down"),
    };
}
