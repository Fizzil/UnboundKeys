using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace UnboundKeys.Wpf;

public partial class VoicePage : IDashboardPage
{
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
            string label = $"press {word}";
            var row = new MappingRow(label, MappingRow.ValueOf(KeyMapSource.Instance, word));
            row.Clicked += () => EditRequested?.Invoke(KeyMapSource.Instance, word, $"\"{label}\"");
            _rows[word] = row;
            Rows.Children.Add(row);
        }
    }

    public void Refresh()
    {
        foreach (var (word, row) in _rows)
            row.SetChipText(MappingRow.ValueOf(KeyMapSource.Instance, word));
    }
}
