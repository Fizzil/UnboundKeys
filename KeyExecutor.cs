using System.Diagnostics;
using System.Threading;

namespace UnboundKeys;

// Carries out a recognized command (a spoken word, or a mapped mouse
// button) according to its behavior: a single tap, holding the key down for
// a duration, tapping it repeatedly for a duration, or — if Infinite is set
// — starting an indefinite hold/repeat that only stops the next time the
// same word/button is recognized. Runs on a background thread (see
// Program.cs) so a long hold/repeat doesn't block speech recognition (or
// the mouse hook) from noticing the next command.
internal static class KeyExecutor
{
    private const int RepeatIntervalMs = 100;

    // A timed Hold or Repeat only ever checked elapsed time, so "press
    // stop" had no way to interrupt one already in progress — it could take
    // the full duration (now possibly several seconds, with per-key gaps)
    // before it noticed anything. This broadcasts a cancel to every
    // in-flight sleep: ReleaseAll cancels the current source and swaps in a
    // fresh one, so already-running executions stop immediately while new
    // ones (captured after the swap) start unaffected.
    private static CancellationTokenSource _stopSignal = new();

    private static void InterruptibleSleep(int ms, CancellationToken token) =>
        token.WaitHandle.WaitOne(ms);

    // A 2+ key word can customize the gap after each key in its sequence
    // (see KeyBehavior.UseCustomRepeatIntervals) — keyIndex is which key
    // was just tapped (0 = Key 1), and its own gap value is what to wait
    // before the next one. A 0 entry, or the feature being off entirely,
    // means "use the normal fixed gap" above.
    private static int GapMsAfterKey(KeyBehavior behavior, int keyIndex, int keyCount)
    {
        if (behavior.UseCustomRepeatIntervals)
        {
            int slot = keyIndex % keyCount;
            if (slot < behavior.RepeatKeyIntervalsSeconds.Count)
            {
                double seconds = behavior.RepeatKeyIntervalsSeconds[slot];
                if (seconds > 0)
                    return (int)(seconds * 1000);
            }
        }
        if (behavior.RepeatGapSeconds > 0)
            return (int)(behavior.RepeatGapSeconds * 1000);
        // The sub-profile (class) global cooldown, when one is set.
        if (GameTiming.GcdSeconds > 0)
            return (int)(GameTiming.GcdSeconds * 1000);
        return RepeatIntervalMs;
    }

    // Tracks which words/button-ids/physical-key-ids currently have an
    // infinite hold/repeat running, so the next time it's heard/pressed we
    // know to stop it instead of starting another one. Shared across all
    // three sources — a spoken word, a mouse button id, and a physical key
    // id never collide, so one dictionary is fine — but which SLOT (see
    // InfiniteState below) a given key occupies does depend on its source
    // (see StateFor), which in turn depends on that same never-collide
    // assumption. Nothing in the type system enforces it — only the static
    // constructor's assert below does, at least catching a future
    // collision loudly instead of silently misrouting.
    private static readonly Dictionary<string, bool> _engaged = new();
    private static readonly object _lock = new();

    // A collision here would mean two of KeyMap.RemappableWords/
    // MouseMap.ButtonIds/VirtualKeyMap.KeyIds share an id — StateFor and
    // _engaged above would then silently route the wrong source's press to
    // the wrong InfiniteState slot, with no exception and no obvious
    // symptom beyond "that one word/button/key behaves strangely." None of
    // the three catalogs are expected to ever change often enough for this
    // to be a real risk, but it costs nothing to turn a mistake into an
    // immediate, loud failure during development instead.
    static KeyExecutor()
    {
        Debug.Assert(!KeyMap.RemappableWords.Intersect(MouseMap.ButtonIds, StringComparer.OrdinalIgnoreCase).Any(),
            "A word and a mouse button id collide — KeyExecutor's shared _engaged dictionary can't tell them apart.");
        Debug.Assert(!KeyMap.RemappableWords.Intersect(VirtualKeyMap.KeyIds, StringComparer.OrdinalIgnoreCase).Any(),
            "A word and a virtual key id collide — KeyExecutor's shared _engaged dictionary can't tell them apart.");
        Debug.Assert(!MouseMap.ButtonIds.Intersect(VirtualKeyMap.KeyIds, StringComparer.OrdinalIgnoreCase).Any(),
            "A mouse button id and a virtual key id collide — KeyExecutor's shared _engaged dictionary can't tell them apart.");
    }

    // Only one word may be doing an infinite hold at a time, and separately
    // only one may be doing an infinite repeat — engaging a new one bumps
    // whichever word was already occupying that slot. Mouse buttons and
    // physical keys each get their own completely independent pair of
    // slots (see StateFor), so a voice-triggered infinite hold, a
    // mouse-button-triggered one, and a physical-key-triggered one can all
    // run at the same time without bumping each other — only two words
    // from the *same* source ever compete for the same slot.
    private sealed class InfiniteState
    {
        public string? HoldWord;
        public IReadOnlyList<(ushort Vk, bool Extended)>? HoldKeys;
        public IntPtr? HoldWindow;
        public string? HoldWindowTitle;

        public string? RepeatWord;
        public IntPtr? RepeatWindow;
        public string? RepeatWindowTitle;
    }

    private static readonly InfiniteState _voiceState = new();
    private static readonly InfiniteState _mouseState = new();
    private static readonly InfiniteState _virtualState = new();

    private static bool IsMouseSource(string word) => Array.IndexOf(MouseMap.ButtonIds, word) >= 0;
    private static bool IsVirtualSource(string word) => Array.IndexOf(VirtualKeyMap.KeyIds, word) >= 0;

    private static InfiniteState StateFor(string word) =>
        IsMouseSource(word) ? _mouseState :
        IsVirtualSource(word) ? _virtualState :
        _voiceState;

    private static List<(ushort Vk, bool Extended)> GetAllKeysForWord(string word) =>
        IsMouseSource(word) ? MouseMap.GetAllKeys(word) :
        IsVirtualSource(word) ? VirtualKeyMap.GetAllKeys(word) :
        KeyMap.GetAllKeys(word);

    // The window (and its title, at the time) that was focused when each
    // slot was engaged — e.g. the game. If focus moves to a different window
    // (alt-tabbing out, clicking elsewhere), or the SAME window's title
    // changes (switching tabs in a browser doesn't change the window at all,
    // but the title usually updates to match the new tab), _focusWatchTimer
    // notices and releases that slot. Tracked per InfiniteState now, above.
    private static readonly System.Threading.Timer _focusWatchTimer =
        new(CheckFocus, null, FocusCheckIntervalMs, FocusCheckIntervalMs);
    private const int FocusCheckIntervalMs = 300;

    // A word can have several keys (its main one plus up to
    // RemapStore.MaxExtraKeys extras) that all fire together as a combo —
    // pressing each down in quick succession,
    // then releasing each in quick succession, rather than one full
    // down-then-up cycle per key. That's as close to truly simultaneous as
    // discrete SendInput calls get, and it's imperceptible in practice.
    private static void PressAllDown(IReadOnlyList<(ushort Vk, bool Extended)> keys)
    {
        foreach (var key in keys)
            NativeInput.KeyDown(key.Vk, key.Extended);
    }

    private static void ReleaseAllUp(IReadOnlyList<(ushort Vk, bool Extended)> keys)
    {
        foreach (var key in keys)
            NativeInput.KeyUp(key.Vk, key.Extended);
    }

    // Repeat cycles through a word's keys one at a time instead of firing
    // them together — Key 1, then Key 2, then Key 3, then back to Key 1, for
    // as long as the repeat runs (a single key just "cycles" through itself
    // every time, same as before). Hold and a plain tap still fire every key
    // together, since that's what makes a combo like Ctrl+C work at all.
    private static void TapKeySequentially(IReadOnlyList<(ushort Vk, bool Extended)> keys, int index)
    {
        var key = keys[index % keys.Count];
        NativeInput.KeyDown(key.Vk, key.Extended);
        NativeInput.KeyUp(key.Vk, key.Extended);
    }

    // ---- Priority pauses (see KeyBehavior.Priority) ----
    //
    // Every repeat loop (timed or infinite) waits out _pauseUntil before
    // each key, and notes when it last fired and with what gap. A priority
    // mapping arriving while any repeat runs sets _pauseUntil so the loops
    // stop at once, waits for the game cooldown left over from that last
    // repeat key, fires (the normal Execute path does that), and keeps the
    // loops paused for its own time after that. A second priority press
    // during a pause extends it rather than cutting it short.
    private static readonly object _pauseLock = new();
    private static DateTime _pauseUntil = DateTime.MinValue;
    private static DateTime _lastRepeatKeyAt = DateTime.MinValue;
    private static int _lastRepeatGapMs = RepeatIntervalMs;
    private static int _runningRepeats;

    private static void NoteRepeatKey(int gapMs)
    {
        lock (_pauseLock)
        {
            _lastRepeatKeyAt = DateTime.UtcNow;
            _lastRepeatGapMs = gapMs;
        }
    }

    private static void WaitOutPause(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TimeSpan left;
            lock (_pauseLock)
                left = _pauseUntil - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                return;
            InterruptibleSleep((int)Math.Min(left.TotalMilliseconds, 50), token);
        }
    }

    private static void PauseRepeatsForPriority(KeyBehavior behavior, CancellationToken token)
    {
        int waitMs;
        lock (_pauseLock)
        {
            if (_runningRepeats == 0)
                return;
            var now = DateTime.UtcNow;
            var cooldownEnds = _lastRepeatKeyAt.AddMilliseconds(_lastRepeatGapMs);
            waitMs = (int)Math.Max(0, (cooldownEnds - now).TotalMilliseconds);
            double pauseMs = behavior.PrioritySeconds > 0 ? behavior.PrioritySeconds * 1000 : _lastRepeatGapMs;
            var until = now.AddMilliseconds(waitMs + pauseMs);
            if (until > _pauseUntil)
                _pauseUntil = until;
        }
        if (waitMs > 0)
            InterruptibleSleep(waitMs, token);
    }

    public static void Execute(string word, IReadOnlyList<(ushort Vk, bool Extended)> keys, KeyBehavior behavior)
    {
        if (behavior.Priority)
            PauseRepeatsForPriority(behavior, _stopSignal.Token);

        if (behavior.Infinite)
        {
            ExecuteInfinite(word, keys, behavior);
            return;
        }

        if ((behavior.Hold || behavior.Repeat) && behavior.DurationSeconds > 0)
        {
            ExecuteTimed(word, keys, behavior);
            return;
        }

        var token = _stopSignal.Token;

        if (behavior.Repeat)
        {
            // No repeat duration set (0s) — still march through the whole
            // sequence once, respecting each key's own gap, instead of
            // collapsing into a single simultaneous tap of every key. For a
            // 1-key word this is just one tap either way, same as before.
            for (int i = 0; i < keys.Count && !token.IsCancellationRequested; i++)
            {
                TapKeySequentially(keys, i);
                if (i < keys.Count - 1)
                    InterruptibleSleep(GapMsAfterKey(behavior, i, keys.Count), token);
            }
        }
        else
        {
            PressAllDown(keys);
            ReleaseAllUp(keys);
        }
    }

    // A word/button with a Repeat or Hold duration set behaves like a
    // press-to-start, press-again-to-stop-early toggle, rather than two
    // independent runs stacking on top of each other unaware of one
    // another. Without this, a long Hold duration used as a stand-in for
    // "as long as I need it" (e.g. 20s) could only ever let go on its own
    // timer running out — pressing the button again just started a second,
    // unrelated 20-second hold on top of the first, and never stopped
    // anything early. Tracked separately from _engaged/InfiniteState above,
    // since those are Infinite-only — this applies to plain timed
    // Hold/Repeat instead.
    private static readonly Dictionary<string, CancellationTokenSource> _activeTimedRuns = new();

    private static void ExecuteTimed(string word, IReadOnlyList<(ushort Vk, bool Extended)> keys, KeyBehavior behavior)
    {
        CancellationTokenSource? toCancel = null;
        CancellationTokenSource? mine = null;

        lock (_lock)
        {
            if (_activeTimedRuns.TryGetValue(word, out var existing))
            {
                toCancel = existing;
                _activeTimedRuns.Remove(word);
            }
            else
            {
                mine = new CancellationTokenSource();
                _activeTimedRuns[word] = mine;
            }
        }

        if (toCancel != null)
        {
            // Only ever Cancel here, never Dispose — the still-running
            // Execute call that owns this token disposes it itself, in its
            // own finally block below, once it's actually done reacting to
            // the cancellation. CancellationTokenSource doesn't guarantee
            // Dispose is safe to call from two threads at once, so letting
            // exactly one owner (the run itself) be the one to dispose it
            // avoids that race entirely.
            toCancel.Cancel();
            return;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(mine!.Token, _stopSignal.Token);
            var token = linked.Token;

            if (behavior.Hold)
            {
                PressAllDown(keys);
                InterruptibleSleep((int)(behavior.DurationSeconds * 1000), token);
                ReleaseAllUp(keys);
            }
            else
            {
                var end = DateTime.UtcNow.AddSeconds(behavior.DurationSeconds);
                int i = 0;
                Interlocked.Increment(ref _runningRepeats);
                try
                {
                    while (DateTime.UtcNow < end && !token.IsCancellationRequested)
                    {
                        WaitOutPause(token);
                        if (token.IsCancellationRequested)
                            break;
                        TapKeySequentially(keys, i);
                        int gapMs = GapMsAfterKey(behavior, i, keys.Count);
                        NoteRepeatKey(gapMs);
                        InterruptibleSleep(gapMs, token);
                        i++;
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref _runningRepeats);
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                if (_activeTimedRuns.TryGetValue(word, out var current) && current == mine)
                    _activeTimedRuns.Remove(word);
            }
            mine!.Dispose();
        }
    }

    // If the app closes while a key is being held/repeated indefinitely, this
    // lets Program.cs let go of it instead of leaving it stuck down. Also
    // where "press stop" and the other safety nets land — cancelling the
    // stop signal here is what makes an in-progress timed Hold/Repeat let go
    // immediately instead of running out its full duration first.
    public static void ReleaseAll()
    {
        var oldSignal = Interlocked.Exchange(ref _stopSignal, new CancellationTokenSource());
        oldSignal.Cancel();
        oldSignal.Dispose();

        List<string> engagedWords;
        lock (_lock)
        {
            engagedWords = new List<string>();
            foreach (var (word, engaged) in _engaged)
                if (engaged)
                    engagedWords.Add(word);
            _engaged.Clear();
            ClearState(_voiceState);
            ClearState(_mouseState);
            ClearState(_virtualState);
        }

        foreach (var word in engagedWords)
            ReleaseAllUp(GetAllKeysForWord(word));

        // A sticky Shift/Ctrl/Alt/Win held down from the virtual keyboard
        // isn't tracked in _engaged at all (see StickyModifiers) — this is
        // the one shared place every panic path (press stop, triple Caps
        // Lock, app exit) already funnels through, so it lets go here too.
        StickyModifiers.ReleaseAll();
    }

    private static void ClearState(InfiniteState s)
    {
        s.HoldWord = null;
        s.HoldKeys = null;
        s.HoldWindow = null;
        s.HoldWindowTitle = null;
        s.RepeatWord = null;
        s.RepeatWindow = null;
        s.RepeatWindowTitle = null;
    }

    // Lets the dashboard forcibly let go of a word's infinite hold/repeat if
    // it's currently engaged — e.g. when the user edits its duration, which
    // signals they're moving away from infinite mode. A no-op if the word
    // isn't actually engaged right now.
    public static void ForceRelease(string word)
    {
        IReadOnlyList<(ushort Vk, bool Extended)>? keysToRelease = null;
        var s = StateFor(word);

        lock (_lock)
        {
            if (_engaged.TryGetValue(word, out var v) && v)
            {
                _engaged[word] = false;

                if (s.HoldWord == word)
                {
                    keysToRelease = s.HoldKeys;
                    s.HoldWord = null;
                    s.HoldKeys = null;
                    s.HoldWindow = null;
                    s.HoldWindowTitle = null;
                }
                else if (s.RepeatWord == word)
                {
                    // The repeat loop notices _engaged[word] flip to false
                    // and exits on its own — nothing to send here.
                    s.RepeatWord = null;
                    s.RepeatWindow = null;
                    s.RepeatWindowTitle = null;
                }
            }
        }

        if (keysToRelease != null)
            ReleaseAllUp(keysToRelease);
    }

    private static void ExecuteInfinite(string word, IReadOnlyList<(ushort Vk, bool Extended)> keys, KeyBehavior behavior)
    {
        bool repeatMode = behavior.Repeat && !behavior.Hold;
        bool starting;
        string? bumpedWord = null;
        IReadOnlyList<(ushort Vk, bool Extended)>? bumpedHoldKeys = null;
        var s = StateFor(word);

        lock (_lock)
        {
            bool wasEngaged = _engaged.TryGetValue(word, out var v) && v;
            starting = !wasEngaged;
            _engaged[word] = starting;

            if (starting)
            {
                // Claim this mode's slot, bumping whoever held it before —
                // their own KeyUp (hold) or loop exit (repeat) happens below,
                // outside the lock.
                var focusedWindow = NativeInput.GetFocusedWindow();
                var focusedTitle = NativeInput.GetWindowTitle(focusedWindow);

                if (repeatMode)
                {
                    if (s.RepeatWord != null && s.RepeatWord != word)
                    {
                        bumpedWord = s.RepeatWord;
                        _engaged[bumpedWord] = false;
                    }
                    s.RepeatWord = word;
                    s.RepeatWindow = focusedWindow;
                    s.RepeatWindowTitle = focusedTitle;
                }
                else
                {
                    if (s.HoldWord != null && s.HoldWord != word)
                    {
                        bumpedWord = s.HoldWord;
                        bumpedHoldKeys = s.HoldKeys;
                        _engaged[bumpedWord] = false;
                    }
                    s.HoldWord = word;
                    s.HoldKeys = keys;
                    s.HoldWindow = focusedWindow;
                    s.HoldWindowTitle = focusedTitle;
                }
            }
            else
            {
                if (repeatMode && s.RepeatWord == word)
                {
                    s.RepeatWord = null;
                    s.RepeatWindow = null;
                    s.RepeatWindowTitle = null;
                }
                if (!repeatMode && s.HoldWord == word)
                {
                    s.HoldWord = null;
                    s.HoldKeys = null;
                    s.HoldWindow = null;
                    s.HoldWindowTitle = null;
                }
            }
        }

        // Release whichever word this one just bumped out of its slot.
        if (bumpedHoldKeys != null)
            ReleaseAllUp(bumpedHoldKeys);

        if (!starting)
        {
            // Second time hearing this word — stop. In repeat mode the loop
            // below notices _engaged flip to false on its own; in hold mode
            // we have to explicitly let go of the keys.
            if (!repeatMode)
                ReleaseAllUp(keys);
            return;
        }

        if (repeatMode)
        {
            var token = _stopSignal.Token;
            int i = 0;
            Interlocked.Increment(ref _runningRepeats);
            try
            {
                while (IsEngaged(word) && !token.IsCancellationRequested)
                {
                    WaitOutPause(token);
                    if (!IsEngaged(word) || token.IsCancellationRequested)
                        break;
                    TapKeySequentially(keys, i);
                    int gapMs = GapMsAfterKey(behavior, i, keys.Count);
                    NoteRepeatKey(gapMs);
                    InterruptibleSleep(gapMs, token);
                    i++;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _runningRepeats);
            }
        }
        else
        {
            PressAllDown(keys);
        }
    }

    private static bool IsEngaged(string word)
    {
        lock (_lock)
        {
            return _engaged.TryGetValue(word, out var v) && v;
        }
    }

    // Runs every FocusCheckIntervalMs on a thread-pool thread. If the
    // foreground window has changed since a slot was engaged (alt-tabbing
    // out of a game, clicking a different window) — or the same window's
    // title has changed (switching tabs in a browser) — that slot releases
    // automatically. A safety net for whenever saying the word (or pressing
    // the button) again, or "press stop", isn't an option. Checks all
    // three sources' slots independently, since any or all can be engaged at once.
    // A window handle change is always a genuine focus switch (typing into
    // a target never makes some *other* window take focus), so that always
    // releases. A title change is ambiguous — it might be a real tab switch
    // (the case this was built for), or it might just be the same window
    // reacting to the keys we're actively sending it (an editor appending a
    // "*" for unsaved changes, say — which was making an infinite repeat
    // stop itself after only a couple of taps, since typing "s" for the
    // very first time is exactly the kind of title change this was meant
    // to catch). Telling those apart: a modified-indicator title is
    // (almost) always the old title with a marker simply added — one
    // string still fully contains the other — whereas a genuine tab switch
    // replaces the title's actual content, so neither contains the other.
    // Only the latter counts as a real switch; the former just re-baselines
    // the stored title and keeps going.
    private static bool LooksLikeOwnSideEffect(string? oldTitle, string? newTitle) =>
        !string.IsNullOrEmpty(oldTitle) && !string.IsNullOrEmpty(newTitle)
        && (newTitle.Contains(oldTitle) || oldTitle.Contains(newTitle));

    private static void CheckFocus(object? state)
    {
        var current = NativeInput.GetFocusedWindow();
        string? currentTitle = null; // fetched lazily, only if actually needed below
        var holdReleases = new List<IReadOnlyList<(ushort Vk, bool Extended)>>();

        lock (_lock)
        {
            foreach (var s in new[] { _voiceState, _mouseState, _virtualState })
            {
                if (s.HoldWord != null && s.HoldWindow.HasValue)
                {
                    currentTitle ??= NativeInput.GetWindowTitle(current);
                    bool windowChanged = s.HoldWindow.Value != current;
                    bool titleChanged = !windowChanged && s.HoldWindowTitle != currentTitle;
                    bool ownSideEffect = titleChanged && LooksLikeOwnSideEffect(s.HoldWindowTitle, currentTitle);

                    if (windowChanged || (titleChanged && !ownSideEffect))
                    {
                        _engaged[s.HoldWord] = false;
                        holdReleases.Add(s.HoldKeys!);
                        s.HoldWord = null;
                        s.HoldKeys = null;
                        s.HoldWindow = null;
                        s.HoldWindowTitle = null;
                    }
                    else if (titleChanged)
                    {
                        s.HoldWindowTitle = currentTitle;
                    }
                }

                if (s.RepeatWord != null && s.RepeatWindow.HasValue)
                {
                    currentTitle ??= NativeInput.GetWindowTitle(current);
                    bool windowChanged = s.RepeatWindow.Value != current;
                    bool titleChanged = !windowChanged && s.RepeatWindowTitle != currentTitle;
                    bool ownSideEffect = titleChanged && LooksLikeOwnSideEffect(s.RepeatWindowTitle, currentTitle);

                    if (windowChanged || (titleChanged && !ownSideEffect))
                    {
                        // The repeat loop notices _engaged flip to false and
                        // exits on its own — nothing to send here.
                        _engaged[s.RepeatWord] = false;
                        s.RepeatWord = null;
                        s.RepeatWindow = null;
                        s.RepeatWindowTitle = null;
                    }
                    else if (titleChanged)
                    {
                        s.RepeatWindowTitle = currentTitle;
                    }
                }
            }
        }

        foreach (var keys in holdReleases)
            ReleaseAllUp(keys);
    }
}
