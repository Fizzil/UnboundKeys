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
}
