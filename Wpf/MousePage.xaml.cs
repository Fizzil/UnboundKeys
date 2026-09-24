using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace UnboundKeys.Wpf;

public partial class MousePage : IDashboardPage
{
    private readonly Dictionary<string, MappingRow> _rows = new();
    private readonly Dictionary<string, string> _labels = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

    public string Title => "Mouse";

    public MousePage()
    {
        InitializeComponent();

        var hint = new TextBlock
        {
            Text = "Click a button on the mouse, or its row, to change what it sends.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 4, 12, 4),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Rows.Children.Add(hint);

        AddGroupLabel("BUTTONS");
        foreach (var button in MouseCatalog.Buttons)
        {
            if (button.Id == "wheelup")
                AddGroupLabel("SCROLL WHEEL");

            string id = button.Id;
            _labels[id] = button.Label;

            var row = new MappingRow(button.Label, MappingRow.ValueOf(MouseMapSource.Instance, id));
            row.Clicked += () => EditRequested?.Invoke(MouseMapSource.Instance, id, button.Label);
            row.HoverChanged += hovered => Diagram.Highlight(hovered ? id : null);
            _rows[id] = row;
            Rows.Children.Add(row);
        }

        Diagram.HotspotHovered += id =>
        {
            foreach (var (rowId, row) in _rows)
                row.SetLinked(rowId == id);
            Diagram.Highlight(id);
        };
        Diagram.HotspotClicked += id => EditRequested?.Invoke(MouseMapSource.Instance, id, _labels[id]);
    }

    public void Refresh()
    {
        foreach (var (id, row) in _rows)
            row.SetChipText(MappingRow.ValueOf(MouseMapSource.Instance, id));
    }

    private void AddGroupLabel(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 4) };
        label.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        Rows.Children.Add(label);
    }
}
