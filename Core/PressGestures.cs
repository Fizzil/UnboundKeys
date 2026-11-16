namespace UnboundKeys;

// Tells a single press from a double press (see Gestures) and fires the id
// of the one it saw. Fed each press of a button or key as it arrives — a
// mouse button's down or a wheel notch (MouseInputWatcher), a real key's
// first down or an on-screen key's click (KeyPresses) — and fires on
// whichever thread decides the matter, the caller's or a timer's;
// Program.cs runs the mapping on a thread of its own either way.
//
// The rules, shaped so a button or key with no double press mapped costs
// nothing it did not before:
// - No double press mapped: the single press fires on the press, at once,
//   as it always did, and nothing is tracked.
// - A double press mapped: a press does not fire at once, because a
//   second may follow. One within the window fires the double press, and
//   both presses are spent on it; none, and the single press fires when
//   the window runs out. So a single press lands a moment late, only on a
//   button or key with a double press mapped.
// A way of pressing with nothing mapped fires nothing. The window is the
// one number below, not a setting until someone needs it to be.
internal sealed class PressGestures : IDisposable
{
    // A second press within this of the first makes a double press.
    public const int DoublePressMs = 300;

    private readonly Func<string, bool> _mapped;
    private readonly Action<string> _fire;
    private readonly object _lock = new();

    // A press waiting to see whether a second follows, by id. The number
    // tells a timer that was overtaken from the one that is current.
    private readonly Dictionary<string, (int Number, System.Threading.Timer Timer)> _waiting = new(StringComparer.OrdinalIgnoreCase);
    private int _lastNumber;

    // mapped: whether an id (a single press's or a double press's) has
    // something to send. fire: an id to run.
    public PressGestures(Func<string, bool> mapped, Action<string> fire)
    {
        _mapped = mapped;
        _fire = fire;
    }

    // One press of a button or key, by the id it always had.
    public void Press(string id)
    {
        string? fire = null;
        lock (_lock)
        {
            string doubleId = Gestures.IdFor(id, Gesture.Double);
            if (!_mapped(doubleId))
            {
                fire = _mapped(id) ? id : null;
            }
            else if (_waiting.TryGetValue(id, out var waiting))
            {
                // The second press of a double press.
                waiting.Timer.Dispose();
                _waiting.Remove(id);
                fire = doubleId;
            }
            else
            {
                int number = ++_lastNumber;
                var timer = new System.Threading.Timer(_ => WindowOver(id, number), null, DoublePressMs, System.Threading.Timeout.Infinite);
                _waiting[id] = (number, timer);
            }
        }
        if (fire != null)
            _fire(fire);
    }

    private void WindowOver(string id, int number)
    {
        string? fire = null;
        lock (_lock)
        {
            if (!_waiting.TryGetValue(id, out var waiting) || waiting.Number != number)
                return;
            waiting.Timer.Dispose();
            _waiting.Remove(id);
            fire = _mapped(id) ? id : null;
        }
        if (fire != null)
            _fire(fire);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, waiting) in _waiting)
                waiting.Timer.Dispose();
            _waiting.Clear();
        }
    }
}
