using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// The ten spoken words laid out as a keypad — three across, "press ten"
// alone at the bottom — rather than a list that scrolls (Fizzil's ask):
// each tile is the phrase with what it sends underneath, lit in the
// accent once it's been changed from its default, same as the keyboard
// map. Above them, the fixed commands that always work.
public partial class VoicePage : IDashboardPage
{
    private const string MicrophoneGlyph = "";

    private readonly Dictionary<string, (Button Tile, TextBlock Mapping)> _tiles = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

    public string Title => "Voice";

    public VoicePage()
    {
        InitializeComponent();

        AddHeader("ALWAYS AVAILABLE", topMargin: 4);
        AddCommandLine($"\"press {VoiceEngine.StopWord}\"", "releases every key being held or repeated");
        AddCommandLine($"\"press {VoiceEngine.MenuWord}\"", "brings this dashboard back");
        AddCommandLine($"\"press {VoiceEngine.FadeWord}\"", "turns Fade on or off");

        AddHeader("SAY \"PRESS\" AND A NUMBER", topMargin: 16);
        var hint = new TextBlock
        {
            Text = "Each tile shows the key that number sends. Click one to change it.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 12, 6),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Rows.Children.Add(hint);

        // Full rows of three, then whatever's left over — "press ten" —
        // centered beneath in a row of the same three columns, so it sits
        // under "press eight" at the same width (Fizzil's symmetry call).
        var words = KeyMap.RemappableWords;
        int inFullRows = words.Length - words.Length % 3;

        var keypad = new UniformGrid { Columns = 3, Margin = new Thickness(2, 0, 2, 0) };
        for (int i = 0; i < inFullRows; i++)
            keypad.Children.Add(BuildTile(words[i]));
        Rows.Children.Add(keypad);

        if (inFullRows < words.Length)
        {
            var lastRow = new Grid { Margin = new Thickness(2, 0, 2, 0) };
            for (int column = 0; column < 3; column++)
                lastRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int leftover = words.Length - inFullRows;
            int firstColumn = (3 - leftover) / 2;
            for (int i = inFullRows; i < words.Length; i++)
            {
                var tile = BuildTile(words[i]);
                Grid.SetColumn(tile, firstColumn + (i - inFullRows));
                lastRow.Children.Add(tile);
            }
            Rows.Children.Add(lastRow);
        }

        Refresh();
        Diagram.SetKey(MappingRow.PrimaryOf(KeyMapSource.Instance, KeyMap.RemappableWords[0]));
    }

    private Button BuildTile(string word)
    {
        string spoken = $"\"press {word}\"";

        var icon = new TextBlock
        {
            Text = MicrophoneGlyph,
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        var phrase = new TextBlock { Text = spoken, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var phraseLine = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        phraseLine.Children.Add(icon);
        phraseLine.Children.Add(phrase);

        var mapping = new TextBlock { FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(phraseLine);
        content.Children.Add(mapping);

        var tile = new Button { Content = content, Height = 58, Margin = new Thickness(3), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center };
        tile.SetResourceReference(StyleProperty, "KeyCapStyle");
        tile.Click += (_, _) => EditRequested?.Invoke(KeyMapSource.Instance, word, spoken);
        tile.MouseEnter += (_, _) =>
        {
            Diagram.SetKey(MappingRow.PrimaryOf(KeyMapSource.Instance, word));
            Diagram.SetSpeaking(true);
        };
        tile.MouseLeave += (_, _) => Diagram.SetSpeaking(false);

        _tiles[word] = (tile, mapping);
        return tile;
    }

    // A word counts as changed once its key, its extra keys, or its
    // Repeat/Hold/Infinite differ from a fresh install — the same test
    // VirtualKeyMap.IsCustomized applies to the keyboard's keys.
    private static bool IsCustomized(string word) =>
        KeyMap.Words[word] != KeyMap.DefaultWords[word]
        || KeyMap.ExtraWords[word].Count > 0
        || KeyMap.Behaviors[word] is not { Repeat: false, Hold: false, Infinite: false };

    public void Refresh()
    {
        foreach (var (word, (tile, mapping)) in _tiles)
        {
            bool customized = IsCustomized(word);
            tile.Tag = customized;
            mapping.Text = MappingRow.ValueOf(KeyMapSource.Instance, word);
            mapping.SetResourceReference(TextBlock.ForegroundProperty, customized ? "AccentBrush" : "TextSecondaryBrush");
        }
        Diagram.SetKey(MappingRow.PrimaryOf(KeyMapSource.Instance, KeyMap.RemappableWords[0]));
    }

    private void AddHeader(string text, double topMargin)
    {
        var header = new TextBlock { Text = text, Margin = new Thickness(0, topMargin, 0, 4) };
        header.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        Rows.Children.Add(header);
    }

    private void AddCommandLine(string phrase, string effect)
    {
        var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 2, 12, 2) };
        var spoken = new System.Windows.Documents.Run(phrase);
        spoken.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimaryBrush");
        line.Inlines.Add(spoken);
        line.Inlines.Add(new System.Windows.Documents.Run("  " + effect));
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Rows.Children.Add(line);
    }
}
