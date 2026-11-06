namespace UnboundKeys;

// What the virtual pad is told, worked out from where the pointer sits in
// the rubber band (see Steering.cs): sideways is the steering, as a share
// of the way to the band's line, and the middle button (clicked on, clicked off) is
// full throttle. Height means nothing: up and down as pedals were tried and
// got in the way of holding the middle button (Fizzil). The pedals come from
// keys instead: the Throttle and Brake keys in the picker's Controller group
// (PadKeys), which a Push up or Push down mapping can send, so "push down to
// accelerate" is Push down sending Throttle. Pure, so the harness can check
// it without a pad.
public static class PadValues
{
    // dx and dy are the pointer's offset from the band's centre in pixels
    // (dy positive = below it); maxPx and maxPy are the band's lines. dy
    // and maxPy are taken but unused: the pointer's height is read by
    // PushState instead. throttleOn is the middle-click throttle; padThrottle
    // and padBrake are the Throttle and Brake keys being held, and either
    // throttle gives full throttle. A line of 0 gives 0 rather than a
    // division by zero.
    public static (double Steer, double Throttle, double Brake) FromOffset(
        int dx, int dy, int maxPx, int maxPy, bool throttleOn,
        bool padThrottle = false, bool padBrake = false)
    {
        // Squared, keeping the sign: a quarter turn at half a push and full
        // lock at the line. A straight line made small corrections coarse
        // and sent full lock through too easily; a longer stretch was tried
        // instead and made the spring home feel laggy (Fizzil).
        double raw = maxPx > 0 ? Math.Clamp((double)dx / maxPx, -1.0, 1.0) : 0.0;
        double steer = raw * Math.Abs(raw);
        return (steer, throttleOn || padThrottle ? 1.0 : 0.0, padBrake ? 1.0 : 0.0);
    }
}
