namespace UnboundKeys;

// What the virtual pad is told, worked out from where the pointer sits in
// the rubber band (see Steering.cs): sideways is the steering, as a share
// of the way to the band's line, and the middle button (clicked on, clicked off) is
// full throttle. Height means nothing: up and down as pedals were tried and
// got in the way of holding the middle button (Fizzil), so the brake is
// never pressed here. Pure, so the harness can check it without a pad.
public static class PadValues
{
    // dx and dy are the pointer's offset from the band's centre in pixels
    // (dy positive = below it); maxPx and maxPy are the band's lines. dy
    // and maxPy are taken but unused, so the pedals can come back on the
    // same call if a game wants them. A line of 0 gives 0 rather than a
    // division by zero.
    public static (double Steer, double Throttle, double Brake) FromOffset(
        int dx, int dy, int maxPx, int maxPy, bool throttleOn)
    {
        double steer = maxPx > 0 ? Math.Clamp((double)dx / maxPx, -1.0, 1.0) : 0.0;
        return (steer, throttleOn ? 1.0 : 0.0, 0.0);
    }
}
