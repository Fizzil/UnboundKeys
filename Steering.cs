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
// lock, so the game
// gets a real analog turn. Up and down hold the pointer too but mean
// nothing to the car: pedals on them were tried and got in the way of
// holding the middle button (Fizzil). Height instead presses two
// mappings of their own, Push up and Push down (PushState): pressed when
// the pointer is pushed past a third of the stretch, released when it
// comes back, so "push down to accelerate" is Push down sending the
// Throttle key (PadKeys) in Hold with Infinite. The
// first version held arrow keys from the pointer's zone instead; the
// game only ever saw full left or full right, so it went (Fizzil: "hard
// left, then hard right"). A separate program read the pointer into its
// own pad for a while; it needed a held key to keep its rows in a layer,
// and one program is easier to set up than two.
//
// Switched on and off by any word, button or key whose editor has the
// Steering switch on (KeyBehavior.Steering: "press nine" toggles it, no
// new spoken word, Fizzil; the Steering page has no switch of its own,
// so the way in is always the way out), and off by
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
    // 10 %: what won Fizzil a race (50 % and 80 % felt loose, and the
    // spring home took longer from further out). Steps of 5 % from there.
    public const double DefaultStretch = 0.1;
    public const double MinStretch = 0.05;
    public const double MaxStretch = 1.0;

    private const int TickMs = 15;

    private static readonly object _lock = new();
    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static RubberBand? _band;
    private static System.Threading.Timer? _timer;
    private static IVirtualPad? _pad;
    private static readonly PushState _push = new();
    private static bool _padThrottle;
    private static bool _padBrake;

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

    // Push up or Push down was pressed or released ("pushup" / "pushdown"):
    // raised outside the lock, because the handlers run mappings.
    public static event Action<string>? PushPressed;
    public static event Action<string>? PushReleased;

    // The Throttle or Brake key (PadKeys) is being held, for the pad.
    // Pressed while steering is off they do nothing.
    public static bool PadThrottleHeld { get { lock (_lock) return _padThrottle; } }
    public static bool PadBrakeHeld { get { lock (_lock) return _padBrake; } }

    // Whenever the key is held, steering on or not. With steering on, the
    // timer tells the pad every tick. With steering off, the pedals work
    // on their own: the pad is plugged in for as long as a pedal is held
    // and told straight away, so Throttle on a button accelerates even
    // before the steering word is said (Fizzil, testing as a new user:
    // Throttle did nothing, Up Arrow did). The pad is made outside the
    // lock, as in SetOn.
    public static void SetPadPedal(ushort padKey, bool down)
    {
        IVirtualPad? toDispose = null;
        bool needPad = false;
        lock (_lock)
        {
            if (padKey == PadKeys.Throttle)
                _padThrottle = down;
            else if (padKey == PadKeys.Brake)
                _padBrake = down;
            if (_band == null)
            {
                if (_padThrottle || _padBrake)
                {
                    if (_pad == null)
                        needPad = true;
                    else
                        _pad.Set(0, _padThrottle ? 1 : 0, _padBrake ? 1 : 0);
                }
                else
                {
                    toDispose = _pad;
                    _pad = null;
                }
            }
        }
        toDispose?.Dispose();
        if (!needPad)
            return;
        var pad = VirtualPadDriver.TryCreate(out var problem);
        lock (_lock)
        {
            if (_pad == null && (_padThrottle || _padBrake))
            {
                _pad = pad;
                PadProblem = pad == null ? problem : null;
                pad?.Set(0, _padThrottle ? 1 : 0, _padBrake ? 1 : 0);
                pad = null;
            }
        }
        pad?.Dispose();
    }

    // How far the pointer can be pulled from the centre, as a share of the
    // way to the screen's edge: 0.5 is halfway.
    public static double Stretch { get; private set; } = Settings.LoadSteeringStretch();

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
        bool havePad;
        lock (_lock)
            havePad = _pad != null;
        if (on && !IsOn && !havePad)
            newPad = VirtualPadDriver.TryCreate(out problem);
        IVirtualPad? oldPad = null;
        PushEvent letGo = PushEvent.None;
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
                if (_pad == null)
                {
                    _pad = newPad;
                    PadProblem = _pad == null ? problem : null;
                }
                else
                    newPad?.Dispose();
                _gameWindow = Suspended?.Invoke() == true ? IntPtr.Zero : NativeInput.GetFocusedWindow();
            }
            else
            {
                _timer?.Dispose();
                _timer = null;
                _band = null;
                // The pedal flags stay: the mapping holding a pedal owns them
                // and clears them with its own key-up (a panic path releases
                // every hold, which does that too). A pedal still held keeps
                // the pad, with the steering let go.
                if (_padThrottle || _padBrake)
                    _pad?.Set(0, _padThrottle ? 1 : 0, _padBrake ? 1 : 0);
                else
                {
                    oldPad = _pad;
                    _pad = null;
                }
                letGo = _push.Release();
            }
        }
        oldPad?.Dispose();
        RaisePushes(letGo);
        Changed?.Invoke();
    }

    public static void Toggle() => SetOn(!IsOn);

    // The panic paths' entry (see KeyExecutor.ReleaseAll): the pointer is
    // free again.
    public static void LetGo() => SetOn(false);

    public static void SetStretch(double share)
    {
        Stretch = Math.Clamp(share, MinStretch, MaxStretch);
        Settings.SaveSteeringStretch(Stretch);
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
        bool switchOff = false;
        PushEvent pushes;
        lock (_lock)
            pushes = TickLocked(out switchOff);
        // Outside the lock: these run mappings, and switching off takes it.
        RaisePushes(pushes);
        if (switchOff)
            SetOn(false);
    }

    // Push up and Push down go out as events, one call each, from outside
    // the lock.
    private static void RaisePushes(PushEvent e)
    {
        if (e.HasFlag(PushEvent.UpReleased)) PushReleased?.Invoke("pushup");
        if (e.HasFlag(PushEvent.DownReleased)) PushReleased?.Invoke("pushdown");
        if (e.HasFlag(PushEvent.UpPressed)) PushPressed?.Invoke("pushup");
        if (e.HasFlag(PushEvent.DownPressed)) PushPressed?.Invoke("pushdown");
    }

    private static PushEvent TickLocked(out bool switchOff)
    {
        switchOff = false;
        if (_band == null)
            return PushEvent.None;
        // The dashboard in front: the band lets go, and so does the
        // pad, so the car is not driven from the dashboard. A push
        // held at that moment is let go of too.
        if (Suspended?.Invoke() == true)
        {
            _pad?.Set(0, 0, 0);
            return _push.Release();
        }
        // The game no longer in front: steering is over (see
        // _gameWindow). Its first time in front, after a start from the
        // dashboard, is remembered instead.
        var front = NativeInput.GetFocusedWindow();
        if (_gameWindow == IntPtr.Zero)
            _gameWindow = front;
        else if (front != _gameWindow)
        {
            switchOff = true;
            return PushEvent.None;
        }
        if (NativeInput.IsLeftButtonDown())
        {
            _pad?.Set(0, 0, 0);
            return _push.Release();
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
            x - _band.CentreX, y - _band.CentreY, _band.MaxPx, _band.MaxPy,
            _padThrottle, _padBrake);
        _pad?.Set(steer, throttle, brake);
        return _push.Update(y - _band.CentreY, _band.MaxPy);
    }

    private static double Now => _clock.Elapsed.TotalSeconds;
}

// What a change of the pointer's height does to Push up and Push down.
[Flags]
public enum PushEvent
{
    None = 0,
    UpPressed = 1,
    UpReleased = 2,
    DownPressed = 4,
    DownReleased = 8,
}

// Push up and Push down as a state machine, with no pointer of its own:
// told the pointer's vertical offset from the band's centre (positive =
// below) and the band's line, it answers what was pressed or released.
// A push is pressed when the offset passes a third of the line and
// released when it comes back inside a quarter, so a wobble at the line
// does not chatter. Up and down are never pressed together: crossing from
// one side to the other releases one and (past a third) presses the other.
public sealed class PushState
{
    public bool UpPressed { get; private set; }
    public bool DownPressed { get; private set; }

    public PushEvent Update(int dy, int maxPy)
    {
        var e = PushEvent.None;
        if (maxPy <= 0)
            return Release();

        // Held pushes stay only on their own side and outside a quarter.
        if (UpPressed && !(dy < 0 && -dy * 4 > maxPy))
        {
            UpPressed = false;
            e |= PushEvent.UpReleased;
        }
        if (DownPressed && !(dy > 0 && dy * 4 > maxPy))
        {
            DownPressed = false;
            e |= PushEvent.DownReleased;
        }
        // Nothing new is pressed while the other side is still held.
        if (!UpPressed && !DownPressed)
        {
            if (dy < 0 && -dy * 3 > maxPy)
            {
                UpPressed = true;
                e |= PushEvent.UpPressed;
            }
            else if (dy > 0 && dy * 3 > maxPy)
            {
                DownPressed = true;
                e |= PushEvent.DownPressed;
            }
        }
        return e;
    }

    // Lets go of whatever is pressed (steering off, the dashboard in front).
    public PushEvent Release()
    {
        var e = PushEvent.None;
        if (UpPressed)
            e |= PushEvent.UpReleased;
        if (DownPressed)
            e |= PushEvent.DownReleased;
        UpPressed = false;
        DownPressed = false;
        return e;
    }
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
