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

            string id = button.Id;
            _labels[id] = button.Label;

            var row = new MappingRow(button.Label, MappingRow.ValueOf(MouseMapSource.Instance, id));
            row.SetModeGlyphs(MappingRow.BehaviorOf(MouseMapSource.Instance, id), DoublePressOf(id));
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

        // Steering (see Steering.cs): the switch is for the session, the
        // stretch is saved. Changed can fire from the hook's thread or a
        // panic path's, so the picture is redrawn through the dispatcher.
        SteeringToggle.Click += (_, _) => Steering.SetOn(!Steering.IsOn);
        StretchMinusButton.Click += (_, _) => Steering.SetStretch(Math.Round(Steering.Stretch - 0.1, 2));
        StretchPlusButton.Click += (_, _) => Steering.SetStretch(Math.Round(Steering.Stretch + 0.1, 2));
        StretchResetButton.Click += (_, _) => Steering.SetStretch(Steering.DefaultStretch);
        Steering.Changed += () => Dispatcher.InvokeAsync(ShowSteering);
        ShowSteering();
    }

    private void ShowSteering()
    {
        SteeringToggle.Tag = Steering.IsOn;
        StretchText.Text = $"{Math.Round(Steering.Stretch * 100)} %";
    }

    public void Refresh()
    {
        foreach (var (id, row) in _rows)
        {
            row.SetChipText(MappingRow.ValueOf(MouseMapSource.Instance, id));
            row.SetModeGlyphs(MappingRow.BehaviorOf(MouseMapSource.Instance, id), DoublePressOf(id));
        }
        ShowSteering();
    }

    // What a button's double press sends, or null when it has none: the
    // row's D (see ModeGlyphs).
    private static string? DoublePressOf(string id)
    {
        string doubleId = Gestures.IdFor(id, Gesture.Double);
        return MouseMapSource.Instance.IsMapped(doubleId) ? MappingRow.ValueOf(MouseMapSource.Instance, doubleId) : null;
    }

    private void AddGroupLabel(string text)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 4) };
        label.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        Rows.Children.Add(label);
    }
}
