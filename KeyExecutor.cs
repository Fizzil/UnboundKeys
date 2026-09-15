using System.Threading;

namespace VoicePress;

// Carries out a recognized voice command according to its behavior: a single
// tap, holding the key down for a duration, tapping it repeatedly for a
// duration, or — if Infinite is set — starting an indefinite hold/repeat that
// only stops the next time the same word is recognized. Runs on a background
// thread (see Program.cs) so a long hold/repeat doesn't block speech
// recognition from hearing the next command.
internal static class KeyExecutor
{
    private const int RepeatIntervalMs = 100;

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
        return RepeatIntervalMs;
    }

    // Tracks which words currently have an infinite hold/repeat running, so
    // the next time the word is heard we know to stop it instead of starting
    // another one.
    private static readonly Dictionary<string, bool> _engaged = new();
    private static readonly object _lock = new();

    // Only one word may be doing an infinite hold at a time, and separately
    // only one may be doing an infinite repeat at a time — engaging a new one
    // bumps whichever word was already occupying that slot. The two slots are
    // independent, so one word can be infinite-holding while a different word
    // is infinite-repeating.
    private static string? _activeInfiniteHoldWord;
    private static IReadOnlyList<(ushort Vk, bool Extended)>? _activeInfiniteHoldKeys;
    private static string? _activeInfiniteRepeatWord;

    // The window (and its title, at the time) that was focused when each
    // slot was engaged — e.g. the game. If focus moves to a different window
    // (alt-tabbing out, clicking elsewhere), or the SAME window's title
    // changes (switching tabs in a browser doesn't change the window at all,
    // but the title usually updates to match the new tab), _focusWatchTimer
    // notices and releases that slot.
    private static IntPtr? _activeInfiniteHoldWindow;
    private static string? _activeInfiniteHoldWindowTitle;
    private static IntPtr? _activeInfiniteRepeatWindow;
    private static string? _activeInfiniteRepeatWindowTitle;
    private static readonly System.Threading.Timer _focusWatchTimer =
        new(CheckFocus, null, FocusCheckIntervalMs, FocusCheckIntervalMs);
    private const int FocusCheckIntervalMs = 300;

    // A word can have up to three keys (its main one plus two extras) that
    // all fire together as a combo — pressing each down in quick succession,
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

    public static void Execute(string word, IReadOnlyList<(ushort Vk, bool Extended)> keys, KeyBehavior behavior)
    {
        if (behavior.Infinite)
        {
            ExecuteInfinite(word, keys, behavior);
            return;
        }

        if (behavior.Hold && behavior.DurationSeconds > 0)
        {
            PressAllDown(keys);
            Thread.Sleep((int)(behavior.DurationSeconds * 1000));
            ReleaseAllUp(keys);
        }
        else if (behavior.Repeat && behavior.DurationSeconds > 0)
        {
            var end = DateTime.UtcNow.AddSeconds(behavior.DurationSeconds);
            int i = 0;
            while (DateTime.UtcNow < end)
            {
                TapKeySequentially(keys, i);
                Thread.Sleep(GapMsAfterKey(behavior, i, keys.Count));
                i++;
            }
        }
        else if (behavior.Repeat)
        {
            // No repeat duration set (0s) — still march through the whole
            // sequence once, respecting each key's own gap, instead of
            // collapsing into a single simultaneous tap of every key. For a
            // 1-key word this is just one tap either way, same as before.
            for (int i = 0; i < keys.Count; i++)
            {
                TapKeySequentially(keys, i);
                if (i < keys.Count - 1)
                    Thread.Sleep(GapMsAfterKey(behavior, i, keys.Count));
            }
        }
        else
        {
            PressAllDown(keys);
            ReleaseAllUp(keys);
        }
    }

    // If the app closes while a key is being held/repeated indefinitely, this
    // lets Program.cs let go of it instead of leaving it stuck down.
    public static void ReleaseAll()
    {
        List<string> engagedWords;
        lock (_lock)
        {
            engagedWords = new List<string>();
            foreach (var (word, engaged) in _engaged)
                if (engaged)
                    engagedWords.Add(word);
            _engaged.Clear();
            _activeInfiniteHoldWord = null;
            _activeInfiniteHoldKeys = null;
            _activeInfiniteHoldWindow = null;
            _activeInfiniteHoldWindowTitle = null;
            _activeInfiniteRepeatWord = null;
            _activeInfiniteRepeatWindow = null;
            _activeInfiniteRepeatWindowTitle = null;
        }

        foreach (var word in engagedWords)
            ReleaseAllUp(KeyMap.GetAllKeys(word));
    }

    // Lets the dashboard forcibly let go of a word's infinite hold/repeat if
    // it's currently engaged — e.g. when the user edits its duration, which
    // signals they're moving away from infinite mode. A no-op if the word
    // isn't actually engaged right now.
    public static void ForceRelease(string word)
    {
        IReadOnlyList<(ushort Vk, bool Extended)>? keysToRelease = null;

        lock (_lock)
        {
            if (_engaged.TryGetValue(word, out var v) && v)
            {
                _engaged[word] = false;

                if (_activeInfiniteHoldWord == word)
                {
                    keysToRelease = _activeInfiniteHoldKeys;
                    _activeInfiniteHoldWord = null;
                    _activeInfiniteHoldKeys = null;
                    _activeInfiniteHoldWindow = null;
                    _activeInfiniteHoldWindowTitle = null;
                }
                else if (_activeInfiniteRepeatWord == word)
                {
                    // The repeat loop notices _engaged[word] flip to false
                    // and exits on its own — nothing to send here.
                    _activeInfiniteRepeatWord = null;
                    _activeInfiniteRepeatWindow = null;
                    _activeInfiniteRepeatWindowTitle = null;
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
                    if (_activeInfiniteRepeatWord != null && _activeInfiniteRepeatWord != word)
                    {
                        bumpedWord = _activeInfiniteRepeatWord;
                        _engaged[bumpedWord] = false;
                    }
                    _activeInfiniteRepeatWord = word;
                    _activeInfiniteRepeatWindow = focusedWindow;
                    _activeInfiniteRepeatWindowTitle = focusedTitle;
                }
                else
                {
                    if (_activeInfiniteHoldWord != null && _activeInfiniteHoldWord != word)
                    {
                        bumpedWord = _activeInfiniteHoldWord;
                        bumpedHoldKeys = _activeInfiniteHoldKeys;
                        _engaged[bumpedWord] = false;
                    }
                    _activeInfiniteHoldWord = word;
                    _activeInfiniteHoldKeys = keys;
                    _activeInfiniteHoldWindow = focusedWindow;
                    _activeInfiniteHoldWindowTitle = focusedTitle;
                }
            }
            else
            {
                if (repeatMode && _activeInfiniteRepeatWord == word)
                {
                    _activeInfiniteRepeatWord = null;
                    _activeInfiniteRepeatWindow = null;
                    _activeInfiniteRepeatWindowTitle = null;
                }
                if (!repeatMode && _activeInfiniteHoldWord == word)
                {
                    _activeInfiniteHoldWord = null;
                    _activeInfiniteHoldKeys = null;
                    _activeInfiniteHoldWindow = null;
                    _activeInfiniteHoldWindowTitle = null;
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
            int i = 0;
            while (IsEngaged(word))
            {
                TapKeySequentially(keys, i);
                Thread.Sleep(GapMsAfterKey(behavior, i, keys.Count));
                i++;
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
    // automatically. A safety net for whenever saying the word again or
    // "press stop" isn't an option.
    private static void CheckFocus(object? state)
    {
        var current = NativeInput.GetFocusedWindow();
        string? currentTitle = null; // fetched lazily, only if actually needed below
        string? holdWordToRelease = null;
        IReadOnlyList<(ushort Vk, bool Extended)>? holdKeysToRelease = null;

        lock (_lock)
        {
            if (_activeInfiniteHoldWord != null && _activeInfiniteHoldWindow.HasValue)
            {
                currentTitle ??= NativeInput.GetWindowTitle(current);
                bool changed = _activeInfiniteHoldWindow.Value != current
                    || _activeInfiniteHoldWindowTitle != currentTitle;

                if (changed)
                {
                    holdWordToRelease = _activeInfiniteHoldWord;
                    holdKeysToRelease = _activeInfiniteHoldKeys;
                    _engaged[holdWordToRelease] = false;
                    _activeInfiniteHoldWord = null;
                    _activeInfiniteHoldKeys = null;
                    _activeInfiniteHoldWindow = null;
                    _activeInfiniteHoldWindowTitle = null;
                }
            }

            if (_activeInfiniteRepeatWord != null && _activeInfiniteRepeatWindow.HasValue)
            {
                currentTitle ??= NativeInput.GetWindowTitle(current);
                bool changed = _activeInfiniteRepeatWindow.Value != current
                    || _activeInfiniteRepeatWindowTitle != currentTitle;

                if (changed)
                {
                    // The repeat loop notices _engaged flip to false and
                    // exits on its own — nothing to send here.
                    _engaged[_activeInfiniteRepeatWord] = false;
                    _activeInfiniteRepeatWord = null;
                    _activeInfiniteRepeatWindow = null;
                    _activeInfiniteRepeatWindowTitle = null;
                }
            }
        }

        if (holdKeysToRelease != null)
            ReleaseAllUp(holdKeysToRelease);
    }
}
