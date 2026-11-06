using System.Diagnostics;

namespace UnboundKeys;

// Steering (Fizzil, 2026-11-06, for Need for Speed Underground 2 running
// in the browser): the mouse as a steering wheel with a rubber band.
// While steering is on, the pointer is held near the middle of its
// screen: pushing the TetraMouse's joystick moves it as usual, but never
// past a line on either side (the stretch, a share of the way from the
// centre to the edge, sideways and up and down alike), and the moment
// the push stops the pointer springs back to the centre. The pointer's
// offset from the centre also drives a virtual Xbox 360 controller that
// UnboundKeys makes itself through the ViGEmBus driver (VirtualPad.cs,
// PadValues.cs): sideways = steering, with the stretch line as full
// lock, and the middle mouse button held = full throttle, so the game
// gets a real analog turn. Up and down hold the pointer too but mean
// nothing to the car: pedals on them were tried and got in the way of
// holding the middle button (Fizzil). The
// first version held arrow keys from the pointer's zone instead; the
// game only ever saw full left or full right, so it went (Fizzil: "hard
// left, then hard right"). A separate program read the pointer into its
// own pad for a while; it needed a held key to keep its rows in a layer,
// and one program is easier to set up than two.
//
// Switched on and off by any word, button or key whose editor has the
// Steering switch on (KeyBehavior.Steering: "press nine" toggles it, no
// new spoken word, Fizzil), by the switch on the Mouse page, and off by
// every panic path (Stop, two Caps Lock taps, quit: KeyExecutor.
// ReleaseAll). Only the stretch is saved (per sub-profile, beside the
// mouse mappings); on/off is for the session and starts off. While the
// dashboard is the window in front (Suspended, set by Program.cs) the
// band lets the pointer go, so the dashboard can be used.
//
// MouseInputWatcher's hook hands every real pointer move to PointerMoved;
// a timer ticks the spring. The pointer is moved with SendInput marked as
// UnboundKeys's own (see NativeInput.MovePointerTo), which the hook
// already ignores, so the band never reacts to its own moves. RubberBand
// below is the pure part the harness checks.
public static class Steering
{
    public const double DefaultStretch = 0.5;
    public const double MinStretch = 0.1;
    public const double MaxStretch = 1.0;

    private const int TickMs = 15;

    private static readonly object _lock = new();
    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static RubberBand? _band;
    private static System.Threading.Timer? _timer;
    private static IVirtualPad? _pad;

    // The window in front when steering came on (the game), or zero while
    // that was the dashboard and the game has not been clicked yet. The
    // band holds only while that window is in front: the window changing
    // switches steering off, the same rule the safety nets apply to held
    // keys, so losing the game never leaves the pointer trapped (Fizzil
    // could not click anything until the browser was closed).
    private static IntPtr _gameWindow;

    public static bool IsOn { get; private set; }

    // Why there is no virtual pad, for the Settings page; null when the pad
    // exists. Without the driver the band still holds the pointer but
    // nothing drives the car, and Settings says why.
    public static string? PadProblem { get; private set; }

    // Full throttle is on: while steering, one middle click starts it and
    // the next stops it, the way a Hold + Infinite mapping works. A held
    // button was tried first and failed Fizzil's middle button, which only
    // ever pulses, and its latch gesture, whose press and release could
    // land either side of steering switching on and leave Windows thinking
    // the button was stuck down.
    public static bool ThrottleOn { get; private set; }

    public static void ToggleThrottle()
    {
        lock (_lock)
            ThrottleOn = !ThrottleOn;
    }

    // How far the pointer can be pulled from the centre, as a share of the
    // way to the screen's edge: 0.5 is halfway.
    public static double Stretch { get; private set; } = Settings.LoadSteeringStretch(KeyMap.ActiveProfile);

    // On/off or the stretch changed, from whichever thread did it (the
    // hook's, a panic path's, the voice engine's): the Mouse page dispatches.
    public static event Action? Changed;

    // True while the band should let the pointer go (the dashboard in front).
    public static Func<bool>? Suspended { get; set; }

    public static void SetOn(bool on)
    {
        // The pad takes a few hundred milliseconds to plug in, and the mouse
        // hook waits on the lock for every pointer move meanwhile, which
        // Windows punishes by dropping the hook. So the pad is made and
        // unplugged outside the lock, and only handed over inside it.
        IVirtualPad? newPad = null;
        string? problem = null;
        if (on && !IsOn)
            newPad = VirtualPadDriver.TryCreate(out problem);
        IVirtualPad? oldPad = null;
        lock (_lock)
        {
            if (IsOn == on)
            {
                newPad?.Dispose();
                return;
            }
            IsOn = on;
            if (on)
            {
                // The band is centred on the monitor the pointer is on, and
                // the pointer starts at its centre.
                var (px, py) = NativeInput.GetPointer();
                var m = NativeInput.MonitorBoundsAt(px, py);
                int cx = m.Left + m.Width / 2;
                int cy = m.Top + m.Height / 2;
                _band = new RubberBand(cx, cy, (int)(m.Width / 2 * Stretch), (int)(m.Height / 2 * Stretch), Now);
                NativeInput.MovePointerTo(cx, cy);
                _timer = new System.Threading.Timer(_ => Tick(), null, TickMs, TickMs);
                _pad = newPad;
                PadProblem = _pad == null ? problem : null;
                ThrottleOn = false;
                _gameWindow = Suspended?.Invoke() == true ? IntPtr.Zero : NativeInput.GetFocusedWindow();
            }
            else
            {
                _timer?.Dispose();
                _timer = null;
                _band = null;
                oldPad = _pad;
                _pad = null;
                ThrottleOn = false;
            }
        }
        oldPad?.Dispose();
        Changed?.Invoke();
    }

    public static void Toggle() => SetOn(!IsOn);

    // The panic paths' entry (see KeyExecutor.ReleaseAll): the pointer is
    // free again.
    public static void LetGo() => SetOn(false);

    public static void SetStretch(double share)
    {
        Stretch = Math.Clamp(share, MinStretch, MaxStretch);
        Settings.SaveSteeringStretch(KeyMap.ActiveProfile, Stretch);
        lock (_lock)
        {
            // A band already up takes the new lines at once.
            if (_band != null)
            {
                var m = NativeInput.MonitorBoundsAt(_band.CentreX, _band.CentreY);
                _band.MaxPx = (int)(m.Width / 2 * Stretch);
                _band.MaxPy = (int)(m.Height / 2 * Stretch);
            }
        }
        Changed?.Invoke();
    }

    // Called by the dashboard alongside MouseMap.SwitchProfile. Steering
    // itself stays as it is: a game switch mid-race is not a panic.
    public static void SwitchProfile(string profile)
    {
        Stretch = Settings.LoadSteeringStretch(profile);
        Changed?.Invoke();
    }

    // Reset profile (the dashboard): the stretch back to the default.
    public static void ResetToDefault() => SetStretch(DefaultStretch);

    // Every real pointer move while steering is on, in screen pixels (the
    // hook's own coordinates). True means the move was past the line and
    // the pointer has been put on the line instead, so the hook should
    // swallow the real move.
    public static bool PointerMoved(int x, int y)
    {
        lock (_lock)
        {
            // The left button held frees the pointer (see NativeInput.
            // IsLeftButtonDown): a drag goes wherever it likes.
            if (_band == null || Suspended?.Invoke() == true || NativeInput.IsLeftButtonDown())
                return false;
            var fix = _band.Moved(x, y, Now);
            if (fix == null)
                return false;
            NativeInput.MovePointerTo(fix.Value.X, fix.Value.Y);
            return true;
        }
    }

    private static void Tick()
    {
        lock (_lock)
        {
            if (_band == null)
                return;
            // The dashboard in front: the band lets go, and so does the
            // pad, so the car is not driven from the dashboard.
            if (Suspended?.Invoke() == true)
            {
                _pad?.Set(0, 0, 0);
                return;
            }
            // The game no longer in front: steering is over (see
            // _gameWindow). Its first time in front, after a start from the
            // dashboard, is remembered instead.
            var front = NativeInput.GetFocusedWindow();
            if (_gameWindow == IntPtr.Zero)
                _gameWindow = front;
            else if (front != _gameWindow)
            {
                SetOn(false);
                return;
            }
            if (NativeInput.IsLeftButtonDown())
            {
                _pad?.Set(0, 0, 0);
                return;
            }
            var (x, y) = NativeInput.GetPointer();
            var next = _band.Tick(x, y, Now);
            if (next != null)
            {
                NativeInput.MovePointerTo(next.Value.X, next.Value.Y);
                (x, y) = next.Value;
            }
            // The pointer's offset from the centre, after the spring's move,
            // is what the pad is told.
            var (steer, throttle, brake) = PadValues.FromOffset(
                x - _band.CentreX, y - _band.CentreY, _band.MaxPx, _band.MaxPy, ThrottleOn);
            _pad?.Set(steer, throttle, brake);
        }
    }

    private static double Now => _clock.Elapsed.TotalSeconds;
}

// The rubber band itself, with no pointer of its own: told about real
// moves (Moved) and the passing of time (Tick), it answers where the
// pointer should be put instead, or null for "leave it". A move past a
// line is put back on it: sideways for the steering, up and down for
// the throttle and the brake (Fizzil: push up to accelerate, down to
// brake). Once no real move has arrived for IdleSeconds the
// pointer is pulled home a share of the remaining distance per tick,
// landing exactly on the centre from within a pixel of it. Given its
// clock in seconds rather than reading one, so the harness can run it
// without waiting.
public sealed class RubberBand
{
    public const double IdleSeconds = 0.1;
    public const double StepShare = 0.35;

    private double _lastMove;

    public RubberBand(int centreX, int centreY, int maxPx, int maxPy, double now = 0.0)
    {
        CentreX = centreX;
        CentreY = centreY;
        MaxPx = maxPx;
        MaxPy = maxPy;
        _lastMove = now;
    }

    public int CentreX { get; }
    public int CentreY { get; }
    public int MaxPx { get; set; }
    public int MaxPy { get; set; }

    public (int X, int Y)? Moved(int x, int y, double now)
    {
        _lastMove = now;
        int hx = Math.Clamp(x, CentreX - MaxPx, CentreX + MaxPx);
        int hy = Math.Clamp(y, CentreY - MaxPy, CentreY + MaxPy);
        if (hx == x && hy == y)
            return null;
        return (hx, hy);
    }

    public (int X, int Y)? Tick(int x, int y, double now)
    {
        if (now - _lastMove < IdleSeconds)
            return null;
        int dx = x - CentreX;
        int dy = y - CentreY;
        if (dx == 0 && dy == 0)
            return null;
        if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
            return (CentreX, CentreY);
        return (x - (int)Math.Round(dx * StepShare), y - (int)Math.Round(dy * StepShare));
    }
}
