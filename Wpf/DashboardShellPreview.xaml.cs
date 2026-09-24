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
        Root.Children.Insert(0, new DashboardShell());

        FadeMode.Changed += () => ApplyWindowOpacity(FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
