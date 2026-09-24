namespace UnboundKeys.Wpf;

// Throwaway preview hosting the real DashboardShell. See
// StyleGallery.xaml.cs for the general "why a preview file exists" note.
public partial class DashboardShellPreview
{
    public DashboardShellPreview()
    {
        InitializeComponent();
        Root.Children.Add(new DashboardShell());
    }
}
