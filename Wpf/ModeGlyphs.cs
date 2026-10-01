using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace UnboundKeys.Wpf;

// The small accent glyphs that say what a mapping has set beyond a plain
// tap (Fizzil's ask), shared by the Mouse rows, the Voice tiles and the
// keyboard map: the Mode (R, a circling arrow for Rotation, H), then ∞ for
// Infinite or P for Infinite pause (either/or in the editor),
// with a grey plus between two. A plain tap gets nothing, so a list stays
// quiet unless a mapping is special; accent is right here, these are "on"
// states (see the Discord rule). Hovering a glyph names it.
//
// All of a mapping's glyphs go in ONE TextBlock, so they share a baseline
// like the letters of a word whatever their font or size (as separate
// blocks, each centred on its own box, an icon sat below the H: a letter's
// box keeps empty room under the baseline). Sizes are tuned by eye with
// Fizzil: the icon font fills its box, so its glyph runs small; ∞ is a
// small, low, heavy glyph in Segoe UI, so it runs large and Light. The plus
// is the chip's own size with full spaces around it.
internal static class ModeGlyphs
{
    private const double BaseSize = 14; // the chip's text size (OutlineButtonStyle)

    // Fills target with behavior's glyphs and returns whether there were
    // any. scale shrinks every size together for the Voice tiles, which
    // have less room than a Mouse row. (The keyboard map's keys have room
    // for none; their tooltip uses Describe instead.)
    public static bool Fill(TextBlock target, KeyBehavior behavior, double scale = 1.0)
    {
        target.Inlines.Clear();
        target.FontSize = BaseSize * scale;
        if (behavior.Repeat && behavior.Rotation)
            Add(target, "", "Rotation", 10 * scale, icon: true);
        else if (behavior.Repeat)
            Add(target, "R", "Repeat", BaseSize * scale);
        else if (behavior.Hold)
            Add(target, "H", "Hold", BaseSize * scale);
        if (behavior.Infinite)
            Add(target, "∞", "Infinite", 18 * scale, light: true);
        if (behavior.Priority)
            Add(target, "P", "Infinite pause", BaseSize * scale);
        return target.Inlines.Count > 0;
    }

    // The same in words, for a tooltip: "Hold + Infinite". Empty for a
    // plain tap.
    public static string Describe(KeyBehavior behavior)
    {
        var parts = new List<string>();
        if (behavior.Repeat && behavior.Rotation)
            parts.Add("Rotation");
        else if (behavior.Repeat)
            parts.Add("Repeat");
        else if (behavior.Hold)
            parts.Add("Hold");
        if (behavior.Infinite)
            parts.Add("Infinite");
        if (behavior.Priority)
            parts.Add("Infinite pause");
        return string.Join(" + ", parts);
    }

    private static void Add(TextBlock target, string text, string name, double size, bool icon = false, bool light = false)
    {
        if (target.Inlines.Count > 0)
        {
            var plus = new Run(" + ");
            plus.SetResourceReference(TextElement.ForegroundProperty, "TextSecondaryBrush");
            target.Inlines.Add(plus);
        }
        target.Inlines.Add(new Run(text)
        {
            ToolTip = name,
            FontSize = size,
            FontWeight = light ? FontWeights.Light : FontWeights.Normal,
            FontFamily = new System.Windows.Media.FontFamily(icon ? "Segoe MDL2 Assets" : "Segoe UI"),
        });
    }
}
