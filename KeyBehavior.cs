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

    // Infinite pause (the editor's switch of that name): this mapping
    // pauses every running repeat while it fires. It presses at once (and
    // once more just before the running repeat's gap ends, if that was
    // early for the game to queue it), then holds the repeats for
    // PrioritySeconds after that gap ends. 0 seconds means one gap of the
    // running repeat. Fizzil's recipe for an ability that must land
    // whatever the game's cooldown: this, plus Hold for the same time.
    public bool Priority { get; set; }
    public double PrioritySeconds { get; set; }
}
