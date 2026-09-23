using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
// A handful of names (Button, Brush, Point) exist in both
// System.Windows(.Controls/.Media) and WinForms' System.Windows.Forms/
// System.Drawing, ambiguous now that the project has both frameworks
// loaded (see UnboundKeys.csproj's own comment on UseWPF). Pinning them
// explicitly here is simpler than fully-qualifying every use below.
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;

namespace UnboundKeys.Wpf;

// WPF port of CategoryKeyPopup.cs (WinForms) — the floating list of one
// key category's individual keys, opened by hovering a category button in
// a word card's Key accordion. Temporarily namespaced UnboundKeys.Wpf,
// not UnboundKeys like the rest of the app, purely so this can coexist
// with the still-active WinForms class of the same name during the
// migration (see the plan doc) — collapses back to the plain name once
// that one is deleted at Phase 8.
//
// Built on NoActivateWindow (Phase 3) instead of a plain Window, for the
// same reason as the WinForms original: it must never steal activation
// from the dashboard/game while it's open.
internal static class CategoryKeyPopup
{
    private const double RowHeight = 34;

    // No scrollbar even for the longest category (Letters, 26 keys) —
    // same reasoning as the WinForms original: sizes the popup tall
    // enough to show every key at once rather than building a themed
    // scrollbar for one edge case.
    public static NoActivateWindow Show(FrameworkElement anchor, double width, KeyCatalog.Entry[] keys, Action<KeyCatalog.Entry> onKeySelected)
    {
        var popup = new NoActivateWindow
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Width = width,
            Height = keys.Length * RowHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.Manual,
        };
        popup.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Theme.Red.xaml", UriKind.Relative) });
        popup.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Theme.Controls.xaml", UriKind.Relative) });
        popup.Background = (Brush)popup.FindResource("ButtonBrush");

        var list = new StackPanel();
        foreach (var entry in keys)
        {
            var item = new Button
            {
                Content = entry.DisplayName,
                Height = RowHeight,
                FontSize = 13,
                Style = (Style)popup.FindResource("ListButtonStyle"),
                Focusable = false, // no focus-rectangle blip on the first item — same fix as ActiveControl=null did in WinForms
            };
            item.Click += (_, _) =>
            {
                onKeySelected(entry);
                popup.Close();
            };
            list.Children.Add(item);
        }
        popup.Content = list;

        Reposition(popup, anchor);
        popup.Show();
        return popup;
    }

    // Positions a category popup relative to whichever category button
    // opened it — to the left of the anchor, not overlapping it (same
    // reasoning as the WinForms original). Called both for initial
    // placement and to keep it glued to its card while the dashboard
    // itself moves, once that's wired up in Phase 6.
    //
    // PointToScreen returns device (physical) pixels, but Window.Left/Top
    // are device-independent units — at 100% display scaling these are
    // the same number, but not above it, so this converts through the
    // anchor's own DPI transform rather than assuming 1:1. Worth getting
    // right here, early, since the keyboard's pixel-precise layout
    // (Phase 7) will care about this same conversion.
    public static void Reposition(Window popup, FrameworkElement anchor)
    {
        var anchorDevicePoint = anchor.PointToScreen(new Point(0, 0));
        var source = PresentationSource.FromVisual(anchor);
        double scale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double x = anchorDevicePoint.X * scale;
        double y = anchorDevicePoint.Y * scale;

        popup.Left = x - popup.Width;
        popup.Top = y;
    }
}
