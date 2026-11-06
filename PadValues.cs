namespace UnboundKeys;

// What the virtual pad is told, worked out from where the pointer sits in
// the rubber band (see Steering.cs): sideways is the steering, as a share
// of the way to the band's line. The pedals are keys: the Throttle and
// Brake keys in the picker's Controller group (PadKeys), which any mapping
// can send, a mouse button's or a Move up / Move down's, so "move down to
// accelerate" is Move down sending Throttle, and a middle click that
// toggles the throttle is Middle Button sending Throttle with Hold and
// Infinite. (A hard-wired middle-click throttle came first and went:
// Fizzil wants the button to do only what its mapping says.) Height
// means nothing here: PushState reads it. Pure, so the harness can check
// it without a pad.
public static class PadValues
{
    // dx and dy are the pointer's offset from the band's centre in pixels
    // (dy positive = below it); maxPx and maxPy are the band's lines. dy
    // and maxPy are taken but unused: the pointer's height is read by
    // PushState instead. padThrottle and padBrake are the Throttle and
    // Brake keys being held. A line of 0 gives 0 rather than a division
    // by zero.
    public static (double Steer, double Throttle, double Brake) FromOffset(
        int dx, int dy, int maxPx, int maxPy, bool padThrottle = false, bool padBrake = false)
    {
        // Squared, keeping the sign: a quarter turn at half a push and full
        // lock at the line. A straight line made small corrections coarse
        // and sent full lock through too easily; a longer stretch was tried
        // instead and made the spring home feel laggy (Fizzil).
        double raw = maxPx > 0 ? Math.Clamp((double)dx / maxPx, -1.0, 1.0) : 0.0;
        double steer = raw * Math.Abs(raw);
        return (steer, padThrottle ? 1.0 : 0.0, padBrake ? 1.0 : 0.0);
    }
}
