using System.Windows;

namespace UnboundKeys.Themes;

// Throwaway preview confirming the real NoActivateWindow class (not the
// scratch spike project) behaves the same way — see StyleGallery.xaml.cs
// for the general "why a preview file exists" note.
public partial class NoActivatePreview
{
    private int _clickCount;

    public NoActivatePreview()
    {
        InitializeComponent();
    }

    private void ClickMeButton_Click(object sender, RoutedEventArgs e)
    {
        _clickCount++;
        ClickMeButton.Content = $"Click count: {_clickCount}";
    }
}
