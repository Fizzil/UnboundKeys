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
            Text = "Say \"press\" and a number. Each row shows the key that number sends. \"Press stop\" releases everything.",
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

        Diagram.SetKey(MappingRow.ValueOf(KeyMapSource.Instance, KeyMap.RemappableWords[0]));
    }

    public void Refresh()
    {
        foreach (var (word, row) in _rows)
            row.SetChipText(MappingRow.ValueOf(KeyMapSource.Instance, word));
        Diagram.SetKey(MappingRow.ValueOf(KeyMapSource.Instance, KeyMap.RemappableWords[0]));
    }
}
