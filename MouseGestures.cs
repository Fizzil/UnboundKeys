using System.Threading;

namespace UnboundKeys;

// Tells a single press, a double press and a long press of a mouse button
// apart (see MouseCatalog.Gesture) and fires the id of the one it saw.
// MouseInputWatcher's hook feeds it each button's downs and ups (a wheel
// notch is a tap on its own, with no up); it fires on whichever thread
// decides the matter, the hook's or a timer's, and Program.cs runs the
// mapping on a thread of its own either way.
//
// The rules, shaped so a button with only a single press set costs
// nothing it did not before:
// - Neither a double nor a long press set: the single press fires on the
//   down, as it always did.
// - A long press set: the down starts the long-press clock. Still down
//   when it runs out, and the long press fires (the up then does nothing).
//   Let go before that, and the press is a tap.
// - A double press set: a tap does not fire at once, because a second tap
//   may follow. One within the window fires the double press on its own
//   down, and both taps are spent on it, however long the second is held.
//   None, and the single press fires when the window runs out. So a
//   single press lands a moment late, on a button with a double press set
//   and only there.
// - A long press set but no double: a short press fires the single press
//   on the up.
// A way of pressing that has nothing mapped fires nothing. The window and
// the clock are the two numbers below; neither is a setting until someone
// needs it to be.
internal sealed class MouseGestures : IDisposable
{
    // A second tap within this of the first makes a double press.
    public const int DoublePressMs = 300;

    // Held this long, a press is a long press.
    public const int LongPressMs = 500;

    private readonly Func<string, bool> _enabled;
    private readonly Action<string> _fire;
    private readonly object _lock = new();
    private readonly Dictionary<string, ButtonState> _buttons = new(StringComparer.OrdinalIgnoreCase);

    private sealed class ButtonState
    {
        public bool Down;

        // This down has been accounted for: it fired something, or it was
        // the second tap of a double press, so its up is nothing.
        public bool Spent;

        // A tap waiting to see whether a second follows.
        public bool TapPending;

        // Goes up whenever the timer's job changes, so a callback from a
        // timer that was overtaken does nothing.
        public int Generation;
        public System.Threading.Timer? Timer;
    }

    // enabled: whether a gesture id (MouseCatalog.IdFor) has a mapping.
    // fire: a gesture id to run.
    public MouseGestures(Func<string, bool> enabled, Action<string> fire)
    {
        _enabled = enabled;
        _fire = fire;
    }

    public void Down(string buttonId)
    {
        string? fire = null;
        lock (_lock)
        {
            var s = StateFor(buttonId);
            s.Down = true;
            s.Spent = false;

            if (s.TapPending)
            {
                // The second tap of a double press.
                s.TapPending = false;
                s.Spent = true;
                StopTimer(s);
                fire = MouseCatalog.IdFor(buttonId, MouseCatalog.Gesture.Double);
            }
            else if (HasLong(buttonId))
            {
                int generation = StartTimer(s, LongPressMs);
                s.Timer = new System.Threading.Timer(_ => LongPressDue(buttonId, generation), null, LongPressMs, Timeout.Infinite);
            }
            else
            {
                s.Spent = true;
                fire = Tap(buttonId, s);
            }
        }
        if (fire != null)
            _fire(fire);
    }

    public void Up(string buttonId)
    {
        string? fire = null;
        lock (_lock)
        {
            var s = StateFor(buttonId);
            if (!s.Down)
                return;
            s.Down = false;
            if (s.Spent)
                return;
            // Let go before the long-press clock ran out: a tap.
            StopTimer(s);
            s.Spent = true;
            fire = Tap(buttonId, s);
        }
        if (fire != null)
            _fire(fire);
    }

    // A wheel notch: a tap with no down or up of its own.
    public void Notch(string buttonId)
    {
        string? fire = null;
        lock (_lock)
        {
            var s = StateFor(buttonId);
            if (s.TapPending)
            {
                s.TapPending = false;
                StopTimer(s);
                fire = MouseCatalog.IdFor(buttonId, MouseCatalog.Gesture.Double);
            }
            else
                fire = Tap(buttonId, s);
        }
        if (fire != null)
            _fire(fire);
    }

    // Under _lock. A tap: with a double press set, it waits in case another
    // follows (the single press fires from the timer if none does);
    // without one, the single press, now.
    private string? Tap(string buttonId, ButtonState s)
    {
        if (!_enabled(MouseCatalog.IdFor(buttonId, MouseCatalog.Gesture.Double)))
            return SingleOrNothing(buttonId);
        s.TapPending = true;
        int generation = StartTimer(s, DoublePressMs);
        s.Timer = new System.Threading.Timer(_ => DoubleWindowOver(buttonId, generation), null, DoublePressMs, Timeout.Infinite);
        return null;
    }

    private string? SingleOrNothing(string buttonId)
    {
        string id = MouseCatalog.IdFor(buttonId, MouseCatalog.Gesture.Single);
        return _enabled(id) ? id : null;
    }

    private bool HasLong(string buttonId) =>
        MouseCatalog.Has(buttonId, MouseCatalog.Gesture.Long) && _enabled(MouseCatalog.IdFor(buttonId, MouseCatalog.Gesture.Long));

    private void LongPressDue(string buttonId, int generation)
    {
        string? fire = null;
        lock (_lock)
        {
            var s = StateFor(buttonId);
            if (s.Generation != generation || !s.Down || s.Spent)
                return;
            s.Spent = true;
            fire = MouseCatalog.IdFor(buttonId, MouseCatalog.Gesture.Long);
        }
        if (fire != null)
            _fire(fire);
    }

    private void DoubleWindowOver(string buttonId, int generation)
    {
        string? fire = null;
        lock (_lock)
        {
            var s = StateFor(buttonId);
            if (s.Generation != generation || !s.TapPending)
                return;
            s.TapPending = false;
            fire = SingleOrNothing(buttonId);
        }
        if (fire != null)
            _fire(fire);
    }

    // Under _lock.
    private ButtonState StateFor(string buttonId)
    {
        if (!_buttons.TryGetValue(buttonId, out var s))
        {
            s = new ButtonState();
            _buttons[buttonId] = s;
        }
        return s;
    }

    // Under _lock: drops the timer there was and hands back the generation
    // the new one must carry (the caller creates it, so its callback can
    // close over that number). ms is only named for the reader.
    private static int StartTimer(ButtonState s, int ms)
    {
        StopTimer(s);
        return s.Generation;
    }

    private static void StopTimer(ButtonState s)
    {
        s.Generation++;
        s.Timer?.Dispose();
        s.Timer = null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var s in _buttons.Values)
                StopTimer(s);
        }
    }
}
