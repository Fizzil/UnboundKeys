using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace UnboundKeys.Wpf;

public partial class VoicePage : IDashboardPage
{
    private const string MicrophoneGlyph = "";

    private readonly Dictionary<string, MappingRow> _rows = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

    public string Title => "Voice";

    public VoicePage()
    {
        InitializeComponent();

        var hint = new TextBlock
        {
            Text = "Say \"press\" and a number. Each row shows the key that number sends.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 4, 12, 12),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Rows.Children.Add(hint);

        foreach (var word in KeyMap.RemappableWords)
        {
            string spoken = $"\"press {word}\"";
            var row = new MappingRow(spoken, MappingRow.ValueOf(KeyMapSource.Instance, word), MicrophoneGlyph);
            row.Clicked += () => EditRequested?.Invoke(KeyMapSource.Instance, word, spoken);
            row.HoverChanged += hovered =>
            {
                if (hovered)
                    Diagram.SetKey(MappingRow.ValueOf(KeyMapSource.Instance, word));
                Diagram.SetSpeaking(hovered);
            };
            _rows[word] = row;
            Rows.Children.Add(row);
        }

        // The fixed commands, not remappable — listed so they're
        // discoverable rather than only in the manual.
        var commandsHeader = new TextBlock { Text = "ALWAYS AVAILABLE", Margin = new Thickness(0, 14, 0, 4) };
        commandsHeader.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        Rows.Children.Add(commandsHeader);
        AddCommandLine($"\"press {VoiceEngine.StopWord}\"", "releases every key being held or repeated");
        AddCommandLine($"\"press {VoiceEngine.MenuWord}\"", "brings this dashboard back");
        AddCommandLine($"\"press {VoiceEngine.FadeWord}\"", "turns Fade on or off");

        Diagram.SetKey(MappingRow.ValueOf(KeyMapSource.Instance, KeyMap.RemappableWords[0]));
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

    public void Refresh()
    {
        foreach (var (word, row) in _rows)
            row.SetChipText(MappingRow.ValueOf(KeyMapSource.Instance, word));
        Diagram.SetKey(MappingRow.ValueOf(KeyMapSource.Instance, KeyMap.RemappableWords[0]));
    }
}
