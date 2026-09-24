using System;
using System.Collections.Generic;
using System.Windows;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// The left rail knows nothing about pages, profiles, or what Listening/
// Fade actually do — it raises events and takes state to display, and
// DashboardShell wires both sides. Tag="True" on a section button is
// the open page (see NavRailButtonStyle); on a toggle it's "on".
public partial class NavRail
{
    private readonly Dictionary<Button, DashboardSection> _sectionOf;

    public event Action<DashboardSection>? SectionSelected;
    public event Action? ProfileChipClicked;
    public event Action? ListeningToggled;
    public event Action? FadeToggled;

    public NavRail()
    {
        InitializeComponent();

        _sectionOf = new Dictionary<Button, DashboardSection>
        {
            [MouseButton] = DashboardSection.Mouse,
            [VoiceButton] = DashboardSection.Voice,
            [KeyboardButton] = DashboardSection.Keyboard,
            [SettingsButton] = DashboardSection.Settings,
            [HelpButton] = DashboardSection.Help,
        };

        var version = typeof(NavRail).Assembly.GetName().Version;
        VersionText.Text = version == null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    public void SetActive(DashboardSection section)
    {
        foreach (var (button, s) in _sectionOf)
            button.Tag = s == section;
    }

    public void SetProfileName(string name) => ProfileNameText.Text = name;
    public void SetProfileFlyoutOpen(bool open) => ProfileChip.Tag = open;
    public void SetListening(bool listening) => ListeningToggle.Tag = listening;
    public void SetFade(bool on) => FadeToggle.Tag = on;

    private void Nav_Click(object sender, RoutedEventArgs e) => SectionSelected?.Invoke(_sectionOf[(Button)sender]);
    private void ProfileChip_Click(object sender, RoutedEventArgs e) => ProfileChipClicked?.Invoke();
    private void ListeningToggle_Click(object sender, RoutedEventArgs e) => ListeningToggled?.Invoke();
    private void FadeToggle_Click(object sender, RoutedEventArgs e) => FadeToggled?.Invoke();
}
