namespace UnboundKeys;

// Multi-mode (Fizzil, 2026-10-08, after Helldivers 2's button options): a
// mouse button, or a key on the Keyboard page, can do two things, one per
// way of pressing it — a single press and a double press. Each way is a
// whole mapping of its own under its own id: "middle" or "va" for the
// single press (the id it always had, so an older file reads exactly as
// before) and "middle.double" or "va.double" for the double press. To the
// maps, the editor, the settings file and KeyExecutor these are ordinary
// ids, which is what makes a double press of a button whose single press
// is holding a key bump that hold, as a second button would. Which way was
// meant is decided by PressGestures from the presses as they arrive.
// (A long press was a third way for a day. Fizzil's own middle button only
// ever sends a 20 ms pulse, so it went.)
public enum Gesture { Single, Double }

public static class Gestures
{
    public const string DoubleSuffix = ".double";

    public static readonly Gesture[] All = { Gesture.Single, Gesture.Double };

    public static string IdFor(string id, Gesture gesture) =>
        gesture == Gesture.Double ? id + DoubleSuffix : id;

    public static (string Id, Gesture Gesture) Split(string id) =>
        id.EndsWith(DoubleSuffix, StringComparison.Ordinal)
            ? (id[..^DoubleSuffix.Length], Gesture.Double)
            : (id, Gesture.Single);

    // The other way of pressing the same button or key — what a press stops
    // before it starts its own (see Program.cs).
    public static string Sibling(string id)
    {
        var (baseId, gesture) = Split(id);
        return IdFor(baseId, gesture == Gesture.Single ? Gesture.Double : Gesture.Single);
    }

    public static string Label(Gesture gesture) =>
        gesture == Gesture.Double ? "Double press" : "Single press";

    // Every mapping id for a set of buttons or keys: each id, then its
    // double press's.
    public static string[] AllIds(IEnumerable<string> ids)
    {
        var all = new List<string>();
        foreach (var id in ids)
            foreach (var gesture in All)
                all.Add(IdFor(id, gesture));
        return all.ToArray();
    }
}
