using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace UnboundKeys.Wpf;

public partial class MousePage : IDashboardPage
{
    // One row per mapping id: a button's single press, and under it its
    // double and long press while they have a key (see MouseCatalog.Gesture).
    private readonly Dictionary<string, MappingRow> _rows = new();
    private readonly Dictionary<string, string> _buttonOf = new();
    private readonly Dictionary<string, string> _labels = new();

    internal event Action<IRemapSource, string, string>? EditRequested;

    public string Title => "Mouse";

    public MousePage()
    {
        InitializeComponent();

        // The start card, until it has been read once.
        StartCard.Visibility = Settings.LoadStartCardDismissed() ? Visibility.Collapsed : Visibility.Visible;
        GotItButton.Click += (_, _) =>
        {
            StartCard.Visibility = Visibility.Collapsed;
            Settings.SaveStartCardDismissed();
        };

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

            _labels[button.Id] = button.Label;
            AddRow(button.Id, button.Id, button.Label, indented: false);

            // The other ways of pressing it, indented under it, shown
            // while they are mapped (Refresh keeps that up to date); the
            // editor's segments are where they are set up.
            foreach (var gesture in MouseCatalog.Gestures)
            {
                if (gesture == MouseCatalog.Gesture.Single || !MouseCatalog.Has(button.Id, gesture))
                    continue;
                AddRow(MouseCatalog.IdFor(button.Id, gesture), button.Id, MouseCatalog.GestureLabel(gesture), indented: true);
            }
        }

        Diagram.HotspotHovered += id =>
        {
            foreach (var (rowId, row) in _rows)
                row.SetLinked(_buttonOf[rowId] == id);
            Diagram.Highlight(id);
        };
        Diagram.HotspotClicked += id => EditRequested?.Invoke(MouseMapSource.Instance, id, _labels[id]);
    }

    private void AddRow(string id, string buttonId, string label, bool indented)
    {
        var row = new MappingRow(label, MappingRow.ValueOf(MouseMapSource.Instance, id));
        if (indented)
            row.Margin = new Thickness(20, 0, 0, 4);
        row.SetModeGlyphs(MappingRow.BehaviorOf(MouseMapSource.Instance, id));
        row.Clicked += () => EditRequested?.Invoke(MouseMapSource.Instance, id, _labels[buttonId]);
        row.HoverChanged += hovered => Diagram.Highlight(hovered ? buttonId : null);
        _rows[id] = row;
        _buttonOf[id] = buttonId;
        Rows.Children.Add(row);
        RefreshRow(id, row);
    }

    public void Refresh()
    {
        foreach (var (id, row) in _rows)
            RefreshRow(id, row);
    }

    private static void RefreshRow(string id, MappingRow row)
    {
        row.SetChipText(MappingRow.ValueOf(MouseMapSource.Instance, id));
        row.SetModeGlyphs(MappingRow.BehaviorOf(MouseMapSource.Instance, id));
        if (MouseCatalog.Split(id).Gesture != MouseCatalog.Gesture.Single)
            row.Visibility = MouseMap.Enabled.TryGetValue(id, out bool on) && on ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddGroupLabel(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 4) };
        label.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        Rows.Children.Add(label);
    }
}
