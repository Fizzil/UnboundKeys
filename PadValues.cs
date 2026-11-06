namespace UnboundKeys;

// What the virtual pad is told, worked out from where the pointer sits in
// the rubber band (see Steering.cs): sideways is the steering, up is the
// throttle, down is the brake, each as a share of the way to the band's
// line. The middle button held is full throttle whatever the height; the
// brake still follows the pointer, so the two can be mixed. Pure, so the
// harness can check it without a pad.
public static class PadValues
{
    // dx and dy are the pointer's offset from the band's centre in pixels
    // (dy positive = below it); maxPx and maxPy are the band's lines. A
    // line of 0 gives 0 for that axis rather than a division by zero.
    public static (double Steer, double Throttle, double Brake) FromOffset(
        int dx, int dy, int maxPx, int maxPy, bool throttleHeld)
    {
        double steer = maxPx > 0 ? Math.Clamp((double)dx / maxPx, -1.0, 1.0) : 0.0;
        double up = maxPy > 0 ? Math.Clamp((double)-dy / maxPy, 0.0, 1.0) : 0.0;
        double brake = maxPy > 0 ? Math.Clamp((double)dy / maxPy, 0.0, 1.0) : 0.0;
        return (steer, throttleHeld ? 1.0 : up, brake);
    }
}
