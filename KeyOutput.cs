namespace UnboundKeys;

// What the app needs from the system to press a key or a mouse button.
// The one implementation today is Windows' (WindowsKeySender); another
// system would bring its own, and the harness brings one that only notes
// what it was asked to send.
internal interface IKeySender
{
    void KeyDown(ushort key, bool extended);
    void KeyUp(ushort key, bool extended);
}

// Every key and mouse button the app presses goes out through here, and
// nowhere else. Two things live here because of that:
//
// - The virtual pad's pedals (PadKeys) are keys to the rest of the app,
//   but no system key: they are handed to Steering instead of the sender.
//
// - A ledger of what the app has down right now. The panic paths
//   (KeyExecutor.ReleaseAll) let go of what each mapping says it holds,
//   and then ReleaseEverything lets go of whatever the ledger still has,
//   so a key that slipped past the bookkeeping is not left stuck down.
internal static class KeyOutput
{
    private static IKeySender _sender = new WindowsKeySender();

    private static readonly object _gate = new();
    private static readonly HashSet<(ushort Key, bool Extended)> _down = new();
    private static long _sweeps;

    // The harness swaps in a sender of its own; returns the one replaced.
    internal static IKeySender Use(IKeySender sender)
    {
        var old = _sender;
        _sender = sender;
        return old;
    }

    internal static int HeldCount
    {
        get { lock (_gate) return _down.Count; }
    }

    public static void TapKey(ushort key, bool extended)
    {
        KeyDown(key, extended);
        KeyUp(key, extended);
    }

    public static void KeyDown(ushort key, bool extended)
    {
        long sweepsBefore;
        lock (_gate)
        {
            sweepsBefore = _sweeps;
            _down.Add((key, extended));
        }
        // Never sent while holding the gate: a send waits for the app's
        // own hooks to see the event, and nothing else should wait on that.
        Send(key, extended, down: true);

        // A ReleaseEverything that ran while this press was on its way out
        // may have sent its key-up first. It asked for everything to be
        // let go, so this press is let go too.
        bool swept;
        lock (_gate)
        {
            swept = _sweeps != sweepsBefore;
            if (swept)
                _down.Remove((key, extended));
        }
        if (swept)
            Send(key, extended, down: false);
    }

    public static void KeyUp(ushort key, bool extended)
    {
        lock (_gate)
            _down.Remove((key, extended));
        Send(key, extended, down: false);
    }

    // Lets go of every key the ledger still has down. Returns how many
    // there were: none, when the bookkeeping above it did its job.
    public static int ReleaseEverything()
    {
        (ushort Key, bool Extended)[] left;
        lock (_gate)
        {
            _sweeps++;
            left = _down.ToArray();
            _down.Clear();
        }
        foreach (var (key, extended) in left)
            Send(key, extended, down: false);
        return left.Length;
    }

    private static void Send(ushort key, bool extended, bool down)
    {
        if (PadKeys.IsPadKey(key))
        {
            Steering.SetPadPedal(key, down);
            return;
        }
        if (down)
            _sender.KeyDown(key, extended);
        else
            _sender.KeyUp(key, extended);
    }
}
