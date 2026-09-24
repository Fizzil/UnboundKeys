using System;
using System.Windows;

namespace UnboundKeys.Themes;

// Swaps the active color dictionary (Themes/Theme.Red|Green|Blue.xaml)
// inside Application.Resources at runtime. Everything styled through a
// DynamicResource re-resolves on its own the moment the merged dictionary
// changes; the one rule this imposes on code-behind is to bind colors
// with SetResourceReference(...) rather than copying a brush out of
// FindResource(...) once (a copy keeps the old theme's color forever).
internal static class ThemeSwapper
{
    private const string ControlsFile = "Theme.Controls.xaml";

    public static void Apply(string themeName)
    {
        // Spelled out fully: with both UseWindowsForms and UseWPF on, a
        // bare "Application" is ambiguous between the two frameworks.
        var app = System.Windows.Application.Current;
        if (app == null)
            return;

        var merged = app.Resources.MergedDictionaries;
        var wanted = new Uri($"Themes/Theme.{themeName}.xaml", UriKind.Relative);

        for (int i = 0; i < merged.Count; i++)
        {
            string? source = merged[i].Source?.OriginalString;
            if (source == null || !source.Contains("Themes/Theme.", StringComparison.OrdinalIgnoreCase) || source.EndsWith(ControlsFile, StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(source, wanted.OriginalString, StringComparison.OrdinalIgnoreCase))
                return;

            // Replaced in place (same index) so it keeps sitting under
            // Theme.Controls.xaml in lookup order, exactly as PreviewProgram
            // (and later App.xaml) merged them.
            merged[i] = new ResourceDictionary { Source = wanted };
            return;
        }

        merged.Insert(0, new ResourceDictionary { Source = wanted });
    }
}
