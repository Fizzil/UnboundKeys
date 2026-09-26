namespace UnboundKeys;

// How a word's key should be pressed: a single tap (the default, when neither
// flag is set), held down for Duration seconds, or tapped repeatedly for
// Duration seconds. If both Repeat and Hold are set, Hold takes priority.
//
// If Infinite is set, Duration is ignored: saying the word starts the
// hold/repeat, and saying the same word again stops it — instead of it
// running for a fixed length of time.
public sealed class KeyBehavior
{
    public bool Repeat { get; set; }
    public bool Hold { get; set; }
    public double DurationSeconds { get; set; }
    public bool Infinite { get; set; }

    // Only meaningful for a word with 2+ keys while Repeat is on, and only
    // while this is true: a custom gap after each key in the sequence
    // instead of the normal fixed gap (see KeyExecutor.RepeatIntervalMs).
    // False means every gap uses the normal fixed one, regardless of
    // whatever's saved in RepeatKeyIntervalsSeconds below.
    public bool UseCustomRepeatIntervals { get; set; }

    // One entry per key: index 0 is how long to wait after Key 1 before
    // Key 2, index 1 after Key 2 before Key 3, and so on, wrapping back to
    // Key 1 after the last one. A 0 entry means "use the normal fixed gap"
    // for that particular key, same as when UseCustomRepeatIntervals is
    // off entirely.
    public List<double> RepeatKeyIntervalsSeconds { get; set; } = new();

    // ---- Game mode (the RemapCard switch of that name) ----

    // Only while UseCustomRepeatIntervals is on: the gap after every key of
    // this mapping, in seconds (a per-key interval above still wins for its
    // own key). 0, or custom gaps off, means the sub-profile global cooldown
    // (see GameTiming), or the usual 0.1 s if none is set.
    public double RepeatGapSeconds { get; set; }

    // A priority mapping pauses any running repeat: waits out the rest of
    // that repeat gap (the game cooldown), fires, then holds the repeat for
    // PrioritySeconds before it carries on. 0 seconds means "one gap of the
    // running repeat", the right pause for an instant ability; a channel
    // wants two or three seconds.
    public bool Priority { get; set; }
    public double PrioritySeconds { get; set; }
}
