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

    // Tracks which words currently have an infinite hold/repeat running, so
    // the next time the word is heard we know to stop it instead of starting
    // another one.
    private static readonly Dictionary<string, bool> _engaged = new();
    private static readonly object _lock = new();

    public static void Execute(string word, ushort vk, bool extended, KeyBehavior behavior)
    {
        if (behavior.Infinite)
        {
            ExecuteInfinite(word, vk, extended, behavior);
            return;
        }

        if (behavior.Hold && behavior.DurationSeconds > 0)
        {
            NativeInput.KeyDown(vk, extended);
            Thread.Sleep((int)(behavior.DurationSeconds * 1000));
            NativeInput.KeyUp(vk, extended);
        }
        else if (behavior.Repeat && behavior.DurationSeconds > 0)
        {
            var end = DateTime.UtcNow.AddSeconds(behavior.DurationSeconds);
            while (DateTime.UtcNow < end)
            {
                NativeInput.TapKey(vk, extended);
                Thread.Sleep(RepeatIntervalMs);
            }
        }
        else
        {
            NativeInput.TapKey(vk, extended);
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
        }

        foreach (var word in engagedWords)
        {
            var vk = KeyMap.Words[word];
            NativeInput.KeyUp(vk, KeyMap.IsExtendedKey(vk));
        }
    }

    private static void ExecuteInfinite(string word, ushort vk, bool extended, KeyBehavior behavior)
    {
        bool repeatMode = behavior.Repeat && !behavior.Hold;
        bool starting;

        lock (_lock)
        {
            bool wasEngaged = _engaged.TryGetValue(word, out var v) && v;
            starting = !wasEngaged;
            _engaged[word] = starting;
        }

        if (!starting)
        {
            // Second time hearing this word — stop. In repeat mode the loop
            // below notices _engaged flip to false on its own; in hold mode
            // we have to explicitly let go of the key.
            if (!repeatMode)
                NativeInput.KeyUp(vk, extended);
            return;
        }

        if (repeatMode)
        {
            while (IsEngaged(word))
            {
                NativeInput.TapKey(vk, extended);
                Thread.Sleep(RepeatIntervalMs);
            }
        }
        else
        {
            NativeInput.KeyDown(vk, extended);
        }
    }

    private static bool IsEngaged(string word)
    {
        lock (_lock)
        {
            return _engaged.TryGetValue(word, out var v) && v;
        }
    }
}
