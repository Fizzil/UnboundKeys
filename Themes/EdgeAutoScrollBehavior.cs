using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace UnboundKeys.Themes;

// Hovering within a strip near a ScrollViewer's top/bottom edge scrolls
// it — the app's one scrolling model, because a remapped scroll wheel is
// swallowed by the mouse hook even inside these windows, and a scrollbar
// is both a small target and out of place in this theme (Fizzil's own
// feedback on both). Attached property so any ScrollViewer opts in from
// XAML (themes:EdgeAutoScrollBehavior.Enable="True") or code.
internal static class EdgeAutoScrollBehavior
{
    private const double EdgeZone = 28;
    private const double ScrollStep = 6;

    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(EdgeAutoScrollBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);
    public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scroll && e.NewValue is true)
            Wire(scroll);
    }

    private static void Wire(ScrollViewer scroll)
    {
        int direction = 0; // -1 = scrolling up, 0 = not scrolling, 1 = scrolling down

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            if (direction < 0)
                scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset - ScrollStep));
            else if (direction > 0)
                scroll.ScrollToVerticalOffset(Math.Min(scroll.ScrollableHeight, scroll.VerticalOffset + ScrollStep));
        };

        scroll.PreviewMouseMove += (_, e) =>
        {
            // A page body and a key list inside it can both have this on;
            // only the innermost ScrollViewer under the mouse should react,
            // or hovering the key list's bottom edge would also drag the
            // whole page along.
            if (e.OriginalSource is DependencyObject origin && NearestScrollViewer(origin) != scroll)
            {
                direction = 0;
                timer.Stop();
                return;
            }

            double y = e.GetPosition(scroll).Y;
            double h = scroll.ActualHeight;

            direction = h <= 0 ? 0
                : y < EdgeZone && scroll.VerticalOffset > 0 ? -1
                : y > h - EdgeZone && scroll.VerticalOffset < scroll.ScrollableHeight ? 1
                : 0;

            if (direction != 0)
                timer.Start();
            else
                timer.Stop();
        };

        scroll.MouseLeave += (_, _) =>
        {
            direction = 0;
            timer.Stop();
        };
    }

    private static ScrollViewer? NearestScrollViewer(DependencyObject start)
    {
        for (var node = start; node != null; node = Parent(node))
            if (node is ScrollViewer found)
                return found;
        return null;
    }

    // Text runs and the like aren't Visuals, so they need the logical tree
    // to get back up to the element that owns them.
    private static DependencyObject? Parent(DependencyObject node) =>
        node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
