using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;

namespace UnboundKeys.Wpf;

// WPF port of ThemeColorPopup.cs (WinForms) — the color picker opened by
// four rapid right-clicks on the overlay icon (ported later, see the plan
// doc — OverlayForm moved to the final phase specifically because it
// opens this). Same behavior as the original: only ever shows the two
// colors that AREN'T currently active, Red first if it's one of them;
// clicking a swatch previews that color live and rebuilds the two rows
// rather than closing.
//
// Reuses the existing WinForms OverlayForm.BuildSkullImage for the actual
// recolor-the-skull-PNG logic (GDI+ color-keying) rather than
// reimplementing that image processing a second time in WPF — it's
// internal to the same assembly, so this is a plain cross-namespace call,
// bridged to a WPF-displayable BitmapSource via interop. Worth revisiting
// once OverlayForm itself is ported (Phase 8) and that logic has a native
// WPF home to call instead.
internal static class ThemeColorPopup
{
    private static readonly string[] AllNames = { "Red", "Green", "Blue" };

    public static NoActivateWindow Show(FrameworkElement anchor, double size, Action<string> onColorSelected)
    {
        var popup = new NoActivateWindow
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            Width = size,
            // Always exactly two rows — always two inactive colors,
            // whichever they currently are — so the picker's own size
            // never needs to change, just which names fill its two rows.
            Height = 2 * size,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        popup.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Theme.Red.xaml", UriKind.Relative) });
        popup.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Theme.Controls.xaml", UriKind.Relative) });
        popup.Background = (Brush)popup.FindResource("BackgroundBrush");

        var list = new StackPanel();
        popup.Content = list;

        void RebuildRows()
        {
            list.Children.Clear();
            foreach (var name in InactiveNamesInOrder())
            {
                var swatch = new Image
                {
                    Source = ToBitmapSource((System.Drawing.Bitmap)OverlayForm.BuildSkullImage(name)),
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Fill,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                swatch.MouseLeftButtonUp += (_, _) =>
                {
                    onColorSelected(name);
                    RebuildRows();
                };
                list.Children.Add(swatch);
            }
        }
        RebuildRows();

        Reposition(popup, anchor);
        popup.Show();
        return popup;
    }

    // Directly below the anchor (the overlay icon), flush against its left
    // edge — matching widths exactly is what makes this read as a stack
    // rather than a floating menu. Called both for initial placement and
    // to keep it glued to the icon while it's being dragged (once
    // OverlayForm's drag handling exists — see RepositionColorPopup
    // there, Phase 8).
    public static void Reposition(Window popup, FrameworkElement anchor)
    {
        var anchorDevicePoint = anchor.PointToScreen(new Point(0, 0));
        var source = PresentationSource.FromVisual(anchor);
        double scale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double x = anchorDevicePoint.X * scale;
        double y = anchorDevicePoint.Y * scale;

        popup.Left = x;
        popup.Top = y + anchor.ActualHeight;
    }

    private static List<string> InactiveNamesInOrder()
    {
        var inactive = new List<string>();
        foreach (var name in AllNames)
            if (name != ThemeMode.Current)
                inactive.Add(name);

        // Red first if it's one of the two inactive colors — otherwise
        // the loop above already left Green before Blue.
        if (inactive.Remove("Red"))
            inactive.Insert(0, "Red");

        return inactive;
    }

    private static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            // GDI+ hands out a raw GDI handle here that .NET doesn't track
            // or free on its own — leaving this out leaks one GDI object
            // every time a swatch row rebuilds (every click while the
            // picker's open).
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
