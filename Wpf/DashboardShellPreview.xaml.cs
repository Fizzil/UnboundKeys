using System.Windows;

namespace UnboundKeys.Wpf;

// The one remaining preview harness: the real DashboardShell inside a
// window shaped like the shipped one will be. Built here in code rather
// than in the XAML because DashboardShell's constructor is internal.
public partial class DashboardShellPreview
{
    public DashboardShellPreview()
    {
        InitializeComponent();

        var shell = new DashboardShell();
        shell.MinimizeRequested += () => WindowState = WindowState.Minimized;
        shell.CloseRequested += Close;
        Root.Children.Add(shell);

        FadeMode.Changed += () => Opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;
        Opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;
    }
}
