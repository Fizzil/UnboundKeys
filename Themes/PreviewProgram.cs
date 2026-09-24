using System;

namespace UnboundKeys.Themes;

// Temporary alternate entry point for previewing WPF work in progress
// while the real app's StartupObject (Program.cs) stays pointed at the
// WinForms UI until the migration's final cutover (see the plan doc,
// Phase 8). Only ever run by manually flipping UnboundKeys.csproj's
// <StartupObject> to this class for a local preview, then flipping it
// back — never committed with StartupObject pointed here.
//
// "Application" is ambiguous once a project has both UseWindowsForms and
// UseWPF true (each framework's implicit usings adds its own), so it's
// spelled out fully here rather than relying on a using directive.
internal static class PreviewProgram
{
    [STAThread]
    private static void Main()
    {
        var app = new System.Windows.Application();
        // A UserControl's own StaticResource lookups only see its own
        // file's resources plus Application.Resources — not whatever
        // window ends up hosting it later (that's how DynamicResource
        // differs: it also follows the live visual tree). Merging the
        // theme dictionaries here, once, at the Application level is what
        // makes a hosted control like RemapCard resolve its styles at
        // all — the real app's eventual App.xaml (Phase 9) will do the
        // same for the active color theme.
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("Themes/Theme.Red.xaml", UriKind.Relative) });
        app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri("Themes/Theme.Controls.xaml", UriKind.Relative) });
        app.Run(new UnboundKeys.Wpf.DashboardShellPreview());
    }
}
