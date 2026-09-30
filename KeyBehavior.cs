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

    // Rotation (the editor's fourth Mode; Repeat must be set too): every
    // tick presses ALL the keys in order a few milliseconds apart instead
    // of one key per tick. The game takes the first it can and rejects the
    // rest, so each tick is "the highest-priority ready ability", Key 1
    // first — a one-button rotation in the player's own order, with no
    // cooldown knowledge on this side at all (Fizzil: the round-robin loop
    // kept firing fillers ahead of the big hits just because of where they
    // sat in it).
    public bool Rotation { get; set; }

    // Infinite pause (the editor's switch of that name): this mapping
    // pauses every running repeat while it fires. It presses at once (and
    // once more just before the running repeat's gap ends, if that was
    // early for the game to queue it), then holds the repeats for
    // PrioritySeconds after that gap ends. 0 seconds means one gap of the
    // running repeat. Fizzil's recipe for an ability that must land
    // whatever the game's cooldown: this, plus Hold for the same time.
    public bool Priority { get; set; }
    public double PrioritySeconds { get; set; }

    // A mapping nobody has changed the behavior of: a single tap that
    // pauses nothing. The one test the Voice page and the on-screen
    // keyboard both use to decide whether a mapping counts as customized.
    // (A method, not a property, so it is never written to the settings
    // file. Rotation needs Repeat, so it needs no mention here.)
    public bool IsPlainTap() => !Repeat && !Hold && !Infinite && !Priority;
}
