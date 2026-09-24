using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// A tile per VirtualKeyCatalog key. A key that's been customized (a
// different key, extra combo keys, or a Repeat/Hold/Infinite behavior —
// VirtualKeyMap.IsCustomized) shows what it now sends in accent text
// plus the Tag underline; an untouched key just shows itself.
public partial class KeyboardPage : IDashboardPage
{
    private readonly Dictionary<string, (Button Tile, TextBlock Mapping)> _keys = new();
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

        foreach (var key in VirtualKeyCatalog.Keys)
        {
            string id = key.Id;
            string label = key.Label;

            var mapping = new TextBlock
            {
                FontSize = 11,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            };
            mapping.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");

            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = label, FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            content.Children.Add(mapping);

            var tile = new Button { Content = content, Height = 56, Margin = new Thickness(2) };
            tile.SetResourceReference(StyleProperty, "FlatButtonBase");
            tile.Click += (_, _) => EditRequested?.Invoke(VirtualKeyMapSource.Instance, id, $"Key {label}");

            _keys[id] = (tile, mapping);
            KeyGrid.Children.Add(tile);
        }

        ShowKeyboardToggle.Click += (_, _) => ShowKeyboardRequested?.Invoke();
        Refresh();
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
        foreach (var (id, (tile, mapping)) in _keys)
        {
            bool customized = VirtualKeyMap.IsCustomized(id);
            tile.Tag = customized;
            mapping.Text = customized ? MappingRow.ValueOf(VirtualKeyMapSource.Instance, id) : "";
        }
    }
}
