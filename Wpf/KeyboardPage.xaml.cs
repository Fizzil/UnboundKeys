using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using static UnboundKeys.KeyboardLayout;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// A remappable key is a real KeyCapStyle key (click → editor); once
// customized it wears the same accent wash as on the keyboard itself and
// the Tag underline (how it is pressed is in its tooltip; a key is too
// small for more, Fizzil). Every other key is a non-interactive outline,
// so the map reads as "the keyboard, with the changeable keys lit".
public partial class KeyboardPage : IDashboardPage
{
    private readonly Dictionary<string, (Button Tile, Border Wash)> _keys = new();
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
        KeyClickToggle.Tag = KeyClick.Enabled;
        KeyClickToggle.Click += (_, _) =>
        {
            KeyClick.Enabled = !KeyClick.Enabled;
            KeyClickToggle.Tag = KeyClick.Enabled;
            KeyClick.Play(); // a sample of what was just switched on (silent when off)
        };

        // Remember words I type frequently (see WordPredictor.Remember): off unless
        // switched on here. Switching it off keeps what was learned so far
        // on disk, unused, until Clear the list, which takes two clicks
        // like every other delete.
        RememberWordsToggle.Tag = WordPredictor.Remember;
        RememberWordsToggle.Click += (_, _) =>
        {
            bool on = !(RememberWordsToggle.Tag is true);
            if (!on)
                WordPredictor.Save();
            Settings.SaveRememberTypedWords(on);
            WordPredictor.Remember = on;
            RememberWordsToggle.Tag = on;
        };
        UnboundKeys.Themes.ConfirmDeleteBehavior.AttachTo(ClearWordsButton, () =>
        {
            WordPredictor.ClearLearned();
            ShowLearnedCount();
        });
        // Open the list: the kept words in the PC's own text editor, to
        // read, change or add to (Fizzil). WordPredictor notices the file
        // changing and takes the edited version; the count catches up when
        // the pointer comes back to this page.
        OpenWordsButton.Click += (_, _) =>
        {
            try
            {
                string path = WordPredictor.EnsureListFile();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error("opening the word list", ex);
            }
        };
        MouseEnter += (_, _) => ShowLearnedCount();
        Refresh();
    }

    private readonly System.Collections.Generic.Dictionary<string, string> _labels = new();

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

        var labelText = new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var content = new Grid();
        content.Children.Add(wash);
        content.Children.Add(labelText);

        _labels[id] = label;

        var tile = new Button { Content = content, ToolTip = $"Remap {label}" };
        tile.SetResourceReference(StyleProperty, "KeyCapStyle");
        tile.Click += (_, _) => EditRequested?.Invoke(VirtualKeyMapSource.Instance, id, $"Key {label}");

        _keys[id] = (tile, wash);
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

    // How many typed words are kept, and nothing to clear when there are none.
    private void ShowLearnedCount()
    {
        int count = WordPredictor.LearnedCount;
        LearnedCountText.Text = count == 0 ? "No words are kept." : count == 1 ? "1 word is kept." : $"{count} words are kept.";
        ClearWordsButton.IsEnabled = count > 0;
    }

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
        ShowLearnedCount();
        foreach (var (id, (tile, wash)) in _keys)
        {
            bool customized = VirtualKeyMap.IsCustomized(id);
            tile.Tag = customized;
            wash.Visibility = customized ? Visibility.Visible : Visibility.Collapsed;
            // No room on a key this small for glyphs or what it sends
            // (Fizzil): the tooltip names the modes (see ModeGlyphs.Describe).
            string modes = ModeGlyphs.Describe(VirtualKeyMap.Behaviors[id]);
            tile.ToolTip = modes.Length > 0 ? $"Remap {_labels[id]}: {modes}" : $"Remap {_labels[id]}";
        }
    }
}
