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
        app.Run(new UnboundKeys.Wpf.CategoryKeyPopupPreview());
    }
}
