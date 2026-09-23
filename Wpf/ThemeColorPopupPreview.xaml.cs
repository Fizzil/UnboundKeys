using System.Windows;

namespace UnboundKeys.Wpf;

// Throwaway preview standing in for the overlay icon's 4-rapid-right-click
// gesture (which doesn't exist until OverlayForm is ported — Phase 8) so
// ThemeColorPopup's positioning and swatch-selection behavior can be
// verified against a real anchor now. Clicking a swatch calls the real
// ThemeMode.SwitchTo — same production code path the real app uses — so
// it does actually change (and save) the app's active theme color, same
// as clicking through the picker in the real app would; nothing
// destructive, just a color preference, easily changed back the same way.
public partial class ThemeColorPopupPreview
{
    private Window? _openPopup;

    public ThemeColorPopupPreview()
    {
        InitializeComponent();
    }

    private void IconStandIn_Click(object sender, RoutedEventArgs e)
    {
        _openPopup?.Close();
        _openPopup = ThemeColorPopup.Show(IconStandIn, IconStandIn.Height, name =>
        {
            ThemeMode.SwitchTo(name);
            ResultText.Text = $"Active theme is now: {name}";
        });
    }
}
