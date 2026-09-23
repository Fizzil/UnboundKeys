namespace UnboundKeys.Wpf;

// Throwaway preview hosting one real RemapCard, bound to an actual voice
// word via the same KeyMapSource the WinForms dashboard uses — so this
// exercises the real Rebind/ResetToDefault code paths (and really does
// change the saved key for word "1"), not fake preview data. See
// StyleGallery.xaml.cs for the general "why a preview file exists" note.
public partial class RemapCardPreview
{
    public RemapCardPreview()
    {
        InitializeComponent();
        Root.Children.Add(new RemapCard(KeyMapSource.Instance, "one"));
    }
}
