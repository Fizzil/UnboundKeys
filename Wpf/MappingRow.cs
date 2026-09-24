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
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _highlight = new Border { CornerRadius = new CornerRadius(6), Visibility = Visibility.Collapsed };
        _highlight.SetResourceReference(Border.BackgroundProperty, "ElevatedBrush");
        SetColumnSpan(_highlight, 2);
        Children.Add(_highlight);

        var divider = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0.35 };
        divider.SetResourceReference(Border.BackgroundProperty, "MutedBrush");
        SetColumnSpan(divider, 2);
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

        _chip = new Button { Content = value, Width = 150, Margin = new Thickness(0, 0, 8, 0) };
        _chip.SetResourceReference(StyleProperty, "OutlineButtonStyle");
        _chip.Click += (_, _) => Clicked?.Invoke();
        SetColumn(_chip, 1);
        Children.Add(_chip);

        // The chip marks its own clicks handled, so this only fires for
        // the label side of the row — no double click-through.
        MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        MouseEnter += (_, _) => { _hovered = true; UpdateHighlight(); HoverChanged?.Invoke(true); };
        MouseLeave += (_, _) => { _hovered = false; UpdateHighlight(); HoverChanged?.Invoke(false); };
    }

    public void SetChipText(string value) => _chip.Content = value;

    public void SetLinked(bool linked)
    {
        _linked = linked;
        UpdateHighlight();
    }

    private void UpdateHighlight() =>
        _highlight.Visibility = _hovered || _linked ? Visibility.Visible : Visibility.Collapsed;

    // Every IRemapSource label is "Key 1: X"; the row only shows X.
    public static string ValueOf(IRemapSource source, string id)
    {
        string full = source.KeyLabelFor(id);
        int i = full.IndexOf(": ", StringComparison.Ordinal);
        return i < 0 ? full : full[(i + 2)..];
    }
}
