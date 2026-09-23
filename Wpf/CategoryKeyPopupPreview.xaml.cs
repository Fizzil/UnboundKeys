using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// Throwaway preview standing in for a real Key card's category row (which
// doesn't exist yet — that's Phase 6) so CategoryKeyPopup's positioning
// and selection behavior can be verified against a real anchor button
// now. See StyleGallery.xaml.cs for the general "why a preview file
// exists" note.
public partial class CategoryKeyPopupPreview
{
    private Window? _openPopup;

    public CategoryKeyPopupPreview()
    {
        InitializeComponent();
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        _openPopup?.Close();

        var button = (Button)sender;
        var category = (string)button.Content;
        var keys = System.Array.Find(KeyCatalog.Groups, g => g.Category == category).Keys;

        _openPopup = CategoryKeyPopup.Show(button, Width, keys, entry =>
        {
            ResultText.Text = $"Picked: {entry.DisplayName} (vk {entry.VkCode})";
        });
    }
}
