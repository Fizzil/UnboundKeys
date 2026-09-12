namespace VoicePress;

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
}
