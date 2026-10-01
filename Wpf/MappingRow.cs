using System;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// One line of a list page: "Right Button ........ [Ctrl]". The whole row
// is the click target, not just the chip — a 48px-tall strip is a far
// easier thing to hit with a mouse than a 150px chip. Hovering it lights
// the row (and, on the Mouse page, the matching spot on the diagram);
// SetLinked lights it from the other direction.
internal sealed class MappingRow : Grid
{
    private readonly Border _highlight;
    private readonly TextBlock _modeGlyphs;
    private readonly Border _glyphPill;
    private readonly Button _chip;
    private bool _hovered;
    private bool _linked;

    public event Action? Clicked;
    public event Action<bool>? HoverChanged;

    // glyph, when given, is a Segoe MDL2 Assets icon shown before the label
    // (the Voice page's microphone).
    public MappingRow(string label, string value, string? glyph = null)
    {
        Height = 48;
        Margin = new Thickness(0, 0, 0, 4);
        // A Grid with no background is transparent to the mouse over its
        // empty parts — this makes the whole row hoverable/clickable.
        Background = System.Windows.Media.Brushes.Transparent;
        Cursor = System.Windows.Input.Cursors.Hand;
        // Label | mode glyphs | chip. The glyphs (see SetModeGlyphs) say at a
        // glance what a row has set beyond a plain tap (Fizzil's ask).
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _highlight = new Border { CornerRadius = new CornerRadius(6), Visibility = Visibility.Collapsed };
        _highlight.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        SetColumnSpan(_highlight, 3);
        Children.Add(_highlight);

        var divider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.35 };
        divider.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
        SetColumnSpan(divider, 3);
        Children.Add(divider);

        var labelPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        if (glyph != null)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            labelPanel.Children.Add(icon);
        }
        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        labelPanel.Children.Add(text);
        Children.Add(labelPanel);

        // One TextBlock for all the glyphs, so they share a baseline like
        // the letters of a word whatever their font or size (as separate
        // blocks, each centred on its own box, the pause sign sat below
        // the H: a letter's box keeps empty room under the baseline).
        // The chip's own font and size (OutlineButtonStyle), so the pill's
        // letters and plus line up with the chip's "Ctrl + C".
        _modeGlyphs = new TextBlock
        {
            FontSize = 14,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        _modeGlyphs.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        // A rounded fill behind the glyphs, the chip's own (ElevatedBrush,
        // radius 4), so they read as a small button beside it (Fizzil).
        // Hidden while there is nothing to show. One width for every pill,
        // so they make a straight column beside the chips, and the chip's
        // height (bound below, once the chip exists), so the two read as
        // a pair.
        _glyphPill = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 52,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            Child = _modeGlyphs,
        };
        _glyphPill.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        SetColumn(_glyphPill, 1);
        Children.Add(_glyphPill);

        _chip = new Button { Content = ChipText(value), Width = 150, Margin = new Thickness(0, 0, 8, 0) };
        _chip.SetResourceReference(StyleProperty, "OutlineButtonStyle");
        _chip.Click += (_, _) => Clicked?.Invoke();
        SetColumn(_chip, 2);
        Children.Add(_chip);
        _glyphPill.SetBinding(HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = _chip });

        // The chip marks its own clicks handled, so this only fires for
        // the label side of the row — no double click-through.
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        MouseEnter += (_, _) => { _hovered = true; UpdateHighlight(); HoverChanged?.Invoke(true); };
        MouseLeave += (_, _) => { _hovered = false; UpdateHighlight(); HoverChanged?.Invoke(false); };
    }

    public void SetChipText(string value) => _chip.Content = ChipText(value);

    // The glyphs for what the row has set beyond a plain tap, in a pill
    // before the chip (see ModeGlyphs); hidden for a plain tap.
    public void SetModeGlyphs(KeyBehavior behavior) =>
        _glyphPill.Visibility = ModeGlyphs.Fill(_modeGlyphs, behavior) ? Visibility.Visible : Visibility.Collapsed;

    // A source's behavior for an id, or a plain tap if it has none yet.
    public static KeyBehavior BehaviorOf(IRemapSource source, string id) =>
        source.Behaviors.TryGetValue(id, out var behavior) ? behavior : new KeyBehavior();

    // A TextBlock rather than a bare string so a long combo trims with an
    // ellipsis instead of spilling out of the chip.
    private static TextBlock ChipText(string value) => new() { Text = value, TextTrimming = TextTrimming.CharacterEllipsis };

    public void SetLinked(bool linked)
    {
        _linked = linked;
        UpdateHighlight();
    }

    private void UpdateHighlight() =>
        _highlight.Visibility = _hovered || _linked ? Visibility.Visible : Visibility.Collapsed;

    // Every IRemapSource label is "Key 1: X". A chip shows X plus any
    // extra keys, "Ctrl + C", so a combo reads as what it actually sends
    // (Fizzil found "Ctrl" alone misleading).
    public static string ValueOf(IRemapSource source, string id)
    {
        string primary = PrimaryOf(source, id);
        if (!source.ExtraWords.TryGetValue(id, out var extras) || extras.Count == 0)
            return primary;
        return primary + " + " + string.Join(" + ", extras.Select(KeyCatalog.DisplayNameFor));
    }

    // The main key alone, for the drawn key cap on the Voice page.
    public static string PrimaryOf(IRemapSource source, string id)
    {
        string full = source.KeyLabelFor(id);
        int i = full.IndexOf(": ", StringComparison.Ordinal);
        return i < 0 ? full : full[(i + 2)..];
    }
}
