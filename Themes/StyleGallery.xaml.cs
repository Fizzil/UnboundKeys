using System.Windows;

namespace UnboundKeys.Themes;

// A throwaway preview window for eyeballing Theme.Controls.xaml's styles
// against real content, the same way the app itself gets rebuilt and
// relaunched after every visual change. Not wired into the real app —
// launched only via PreviewProgram.cs while StartupObject is temporarily
// pointed at it; safe to delete once Phase 2 is done and its styles are
// in real use elsewhere.
public partial class StyleGallery : Window
{
    public StyleGallery()
    {
        InitializeComponent();
    }
}
