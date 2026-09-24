namespace UnboundKeys.Wpf;

// Throwaway preview hosting one real ProfilesTab. See StyleGallery.xaml.cs
// for the general "why a preview file exists" note.
public partial class ProfilesTabPreview
{
    public ProfilesTabPreview()
    {
        InitializeComponent();
        Root.Children.Add(new ProfilesTab());
    }
}
