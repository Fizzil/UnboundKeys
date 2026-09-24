using System;
using System.Collections.Generic;
using System.Windows.Shapes;

namespace UnboundKeys.Wpf;

// Hotspot ids are MouseCatalog's button ids, so the diagram and the row
// list talk about the same thing without a lookup table between them.
// x1 is the rear thumb button ("Mouse Button 4", Back) and x2 the front
// one ("Mouse Button 5", Forward), matching how Windows numbers them.
public partial class MouseDiagram
{
    private readonly Dictionary<string, Shape> _hotspots;

    public event Action<string?>? HotspotHovered;
    public event Action<string>? HotspotClicked;

    public MouseDiagram()
    {
        InitializeComponent();

        _hotspots = new Dictionary<string, Shape>
        {
            ["right"] = RightButton,
            ["middle"] = WheelMiddle,
            ["wheelup"] = WheelUp,
            ["wheeldown"] = WheelDown,
            ["x1"] = SideBack,
            ["x2"] = SideForward,
        };

        foreach (var (id, shape) in _hotspots)
        {
            SetLook(shape, highlighted: false);
            shape.MouseEnter += (_, _) => HotspotHovered?.Invoke(id);
            shape.MouseLeave += (_, _) => HotspotHovered?.Invoke(null);
            shape.MouseLeftButtonUp += (_, _) => HotspotClicked?.Invoke(id);
        }
    }

    // Lights exactly one hotspot (or none, for null).
    public void Highlight(string? id)
    {
        foreach (var (hotspotId, shape) in _hotspots)
            SetLook(shape, highlighted: hotspotId == id);
    }

    private static void SetLook(Shape shape, bool highlighted)
    {
        shape.SetResourceReference(Shape.FillProperty, highlighted ? "AccentBrush" : "ElevatedBrush");
        shape.Opacity = highlighted ? 0.6 : 1.0;
    }
}
