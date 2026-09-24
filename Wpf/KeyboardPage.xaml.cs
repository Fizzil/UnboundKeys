using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using static UnboundKeys.KeyboardLayout;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// A remappable key is a real KeyCapStyle key (click → editor); once
// customized it wears the same accent wash as on the keyboard itself,
// the Tag underline, and its new mapping in small accent text. Every
// other key is a non-interactive outline, so the map reads as "the
// keyboard, with the changeable keys lit".
public partial class KeyboardPage : IDashboardPage
{
    private readonly Dictionary<string, (Button Tile, Border Wash, TextBlock Mapping)> _keys = new();
    private readonly Dictionary<Button, double> _scaleOf;

    internal event Action<IRemapSource, string, string>? EditRequested;
    public event Action? ShowKeyboardRequested;
    public event Action<double>? ScaleSelected;

    public string Title => "Keyboard";

    public KeyboardPage()
    {
        InitializeComponent();

        _scaleOf = new Dictionary<Button, double>
        {
            [SmallSizeButton] = 0.65,
            [MediumSizeButton] = 0.8,
            [LargeSizeButton] = 1.0,
        };
        ShowScale(Settings.LoadKeyboardScale());

        foreach (var row in FullRows())
            KeyRows.Children.Add(BuildRow(row));

        ShowKeyboardToggle.Click += (_, _) => ShowKeyboardRequested?.Invoke();
        Refresh();
    }

    private Grid BuildRow(KeySpec[] specs)
    {
        var row = new Grid { Height = 36 };
        foreach (var spec in specs)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(spec.Width, GridUnitType.Star) });

        for (int i = 0; i < specs.Length; i++)
        {
            FrameworkElement key = specs[i].Kind == KeyKind.Remappable ? BuildRemappableKey(specs[i]) : BuildFixedKey(specs[i]);
            Grid.SetColumn(key, i);
            row.Children.Add(key);
        }

        return row;
    }

    private Button BuildRemappableKey(KeySpec spec)
    {
        string id = spec.Id!;
        string label = spec.Label.ToUpperInvariant();

        var wash = new Border { CornerRadius = new CornerRadius(4), Opacity = 0.24, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        wash.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

        var labelText = new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        var mapping = new TextBlock { FontSize = 9, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, -1, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        mapping.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(labelText);
        stack.Children.Add(mapping);

        var content = new Grid();
        content.Children.Add(wash);
        content.Children.Add(stack);

        var tile = new Button { Content = content, ToolTip = $"Remap {label}" };
        tile.SetResourceReference(StyleProperty, "KeyCapStyle");
        tile.Click += (_, _) => EditRequested?.Invoke(VirtualKeyMapSource.Instance, id, $"Key {label}");

        _keys[id] = (tile, wash, mapping);
        return tile;
    }

    private static Border BuildFixedKey(KeySpec spec)
    {
        var text = new TextBlock
        {
            Text = spec.Label,
            FontSize = 11,
            Opacity = 0.6,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var outline = new Border
        {
            Child = text,
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        outline.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        return outline;
    }

    public void SetKeyboardShown(bool shown) => ShowKeyboardToggle.Tag = shown;

    private void SizeButton_Click(object sender, RoutedEventArgs e)
    {
        double scale = _scaleOf[(Button)sender];
        Settings.SaveKeyboardScale(scale);
        ShowScale(scale);
        ScaleSelected?.Invoke(scale);
    }

    // Lights whichever segment is nearest the saved value.
    private void ShowScale(double scale)
    {
        Button? nearest = null;
        double nearestDistance = double.MaxValue;
        foreach (var (button, value) in _scaleOf)
        {
            double distance = Math.Abs(value - scale);
            if (distance < nearestDistance)
            {
                nearest = button;
                nearestDistance = distance;
            }
        }
        foreach (var button in _scaleOf.Keys)
            button.Tag = button == nearest;
    }

    public void Refresh()
    {
        foreach (var (id, (tile, wash, mapping)) in _keys)
        {
            bool customized = VirtualKeyMap.IsCustomized(id);
            tile.Tag = customized;
            wash.Visibility = customized ? Visibility.Visible : Visibility.Collapsed;
            mapping.Text = customized ? MappingRow.ValueOf(VirtualKeyMapSource.Instance, id, "+") : "";
        }
    }
}
