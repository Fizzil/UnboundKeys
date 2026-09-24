using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnboundKeys.Themes;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// WPF replacement for DashboardForm.cs (WinForms), restructured rather
// than ported: a Discord-style rail of sections instead of a row of
// tabs, and the remap editor as a page you go into and Back out of
// instead of a floating popup. Navigation is deliberately one level deep
// — a section's list page, or the editor for one mapping on top of it —
// so it's three methods (ShowSection / OpenEditor / CloseEditor), not a
// navigation stack. Positioning next to the overlay icon is still the
// overlay's job (Stage C); this control just fills its window.
public partial class DashboardShell
{
    // The top strip — the rail's name plate and the page header — is the
    // drag handle for whatever window this lives in (there's no title
    // bar); its buttons are excluded so they still click.
    private const double DragStripHeight = 52;

    private readonly Dictionary<DashboardSection, FrameworkElement> _pages = new();
    private readonly KeyboardPage _keyboardPage;
    private DashboardSection _section = DashboardSection.Mouse;
    private FrameworkElement? _editor;
    private VirtualKeyboardWindow? _keyboard;

    public event Action? MinimizeRequested;
    public event Action? CloseRequested;
    public event Action? QuitRequested;
    // The keyboard Menu key: the window shows itself.
    public event Action? ShowRequested;

    internal DashboardShell()
    {
        // The preview harness merges Red unconditionally; the real
        // starting point is whatever the active profile saved.
        ThemeSwapper.Apply(ThemeMode.Current);
        InitializeComponent();

        var mousePage = new MousePage();
        mousePage.EditRequested += OpenEditor;
        _pages[DashboardSection.Mouse] = mousePage;
        var voicePage = new VoicePage();
        voicePage.EditRequested += OpenEditor;
        _pages[DashboardSection.Voice] = voicePage;
        _keyboardPage = new KeyboardPage();
        _keyboardPage.EditRequested += OpenEditor;
        _keyboardPage.ShowKeyboardRequested += ToggleKeyboard;
        _keyboardPage.ScaleSelected += scale => _keyboard?.ApplyScale(scale);
        _pages[DashboardSection.Keyboard] = _keyboardPage;
        var settingsPage = new SettingsPage();
        settingsPage.ProfileSelected += SwitchToProfile;
        settingsPage.ResetAllRequested += ResetAllMappings;
        settingsPage.QuitRequested += () => QuitRequested?.Invoke();
        _pages[DashboardSection.Settings] = settingsPage;
        _pages[DashboardSection.Help] = new HelpPage();

        Rail.SectionSelected += ShowSection;
        Rail.ProfileChipClicked += ToggleProfileFlyout;
        Rail.ListeningToggled += ListeningMode.Toggle;
        Rail.FadeToggled += FadeMode.Toggle;
        ListeningMode.Changed += () => Rail.SetListening(!ListeningMode.IsPaused);
        FadeMode.Changed += () => Rail.SetFade(FadeMode.IsOn);
        // Covers a swatch click and a profile switch alike — ThemeMode
        // raises Changed for both.
        ThemeMode.Changed += () => ThemeSwapper.Apply(ThemeMode.Current);

        Rail.SetProfileName(KeyMap.ActiveProfile);
        Rail.SetListening(!ListeningMode.IsPaused);
        Rail.SetFade(FadeMode.IsOn);
        ShowSection(DashboardSection.Mouse);
    }

    // Always refreshes the page it shows — cheap (chip text only), and it
    // means a page is never stale after an editor, a profile switch or a
    // Reset All, whichever route led here.
    private void ShowSection(DashboardSection section)
    {
        CloseProfileFlyout();
        _section = section;
        _editor = null;
        Rail.SetActive(section);
        BackButton.Visibility = Visibility.Collapsed;
        if (_pages[section] is IDashboardPage page)
        {
            page.Refresh();
            PageTitle.Text = page.Title;
        }
        else
        {
            PageTitle.Text = section.ToString();
        }
        PageHost.Content = _pages[section];
        Body.ScrollToTop();
        // Every route here follows something that may have changed a key's
        // mapping (an editor closing, a profile switch, Reset All).
        _keyboard?.RefreshCustomizedIndicators();
    }

    // The dashboard owns the on-screen keyboard window: one at a time,
    // shown and hidden from the Keyboard page's switch (and, in Stage C,
    // from the overlay icon too). Reopens in whichever of Mini/Maxi it was
    // last left in.
    private void ToggleKeyboard()
    {
        if (_keyboard != null)
        {
            _keyboard.Close();
            return;
        }

        var (_, _, mini) = Settings.LoadKeyboardPlacement();
        var keyboard = new VirtualKeyboardWindow(mini);
        keyboard.Closed += (_, _) =>
        {
            _keyboard = null;
            _keyboardPage.SetKeyboardShown(false);
        };
        keyboard.PlaceNear(Window.GetWindow(this));
        _keyboard = keyboard;
        keyboard.MenuRequested += () => ShowRequested?.Invoke();
        _keyboardPage.SetKeyboardShown(true);
        keyboard.Show();
    }

    // The editor page: the section's list is replaced by that one
    // mapping's RemapCard, with "‹ Back" in the header. A fresh card each
    // time — it reads its state from the source on construction, so
    // nothing can be stale.
    private void OpenEditor(IRemapSource source, string id, string title)
    {
        var card = new RemapCard(source, id);
        card.ResetAllRequested += ResetAllMappings;
        _editor = card;
        BackButton.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageHost.Content = card;
        Body.ScrollToTop();
    }

    private void CloseEditor()
    {
        if (_editor == null)
            return;
        ShowSection(_section);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => CloseEditor();

    // Same KeyMap → MouseMap → VirtualKeyMap → ThemeMode order as
    // DashboardForm.SwitchToProfile (WinForms): KeyMap's own SwitchProfile
    // releases every held key first, so it has to go before the others.
    // ThemeMode raises Changed, which re-themes the window (see the
    // constructor). Ends with ShowSection, which closes any open editor
    // (its card would otherwise be editing a profile that's no longer
    // active) and redraws the page for the new profile.
    private void SwitchToProfile(string name)
    {
        if (name == KeyMap.ActiveProfile)
            return;

        KeyMap.SwitchProfile(name);
        MouseMap.SwitchProfile(name);
        VirtualKeyMap.SwitchProfile(name);
        ThemeMode.SwitchProfile(name);
        Rail.SetProfileName(name);
        ShowSection(_section);
    }

    // Reached from Settings' Reset All and from a card's own triple-tap
    // Reset All — every mapping of every kind, then the page redrawn.
    private void ResetAllMappings()
    {
        KeyMap.ResetAll();
        MouseMap.ResetAll();
        VirtualKeyMap.ResetAll();
        ShowSection(_section);
    }

    // Rebuilt on every open — profiles are few and can change from the
    // Settings page in between.
    private void ToggleProfileFlyout()
    {
        if (FlyoutLayer.Visibility == Visibility.Visible)
        {
            CloseProfileFlyout();
            return;
        }

        FlyoutList.Children.Clear();
        foreach (var name in Settings.LoadProfileNames())
        {
            string profile = name;
            var button = new Button { Content = profile, Tag = profile == KeyMap.ActiveProfile, Height = 40, Margin = new Thickness(0, 1, 0, 1) };
            button.SetResourceReference(StyleProperty, "NavRailButtonStyle");
            button.Click += (_, _) =>
            {
                CloseProfileFlyout();
                SwitchToProfile(profile);
            };
            FlyoutList.Children.Add(button);
        }

        var manage = new Button
        {
            Content = "Manage profiles…",
            Height = 36,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        manage.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        manage.Click += (_, _) => ShowSection(DashboardSection.Settings);
        FlyoutList.Children.Add(manage);

        FlyoutLayer.Visibility = Visibility.Visible;
        Rail.SetProfileFlyoutOpen(true);
    }

    private void CloseProfileFlyout()
    {
        FlyoutLayer.Visibility = Visibility.Collapsed;
        Rail.SetProfileFlyoutOpen(false);
    }

    private void FlyoutDismiss_Click(object sender, MouseButtonEventArgs e) => CloseProfileFlyout();

    private void Minimize_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke();
    // Only the dashboard goes away (it hides — see DashboardWindow); an
    // open on-screen keyboard is a typing tool that outlives it, same as
    // in the WinForms app.
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);

        if (FlyoutLayer.Visibility == Visibility.Visible || e.GetPosition(this).Y > DragStripHeight)
            return;
        if (IsInsideButton(e.OriginalSource as DependencyObject))
            return;

        var window = Window.GetWindow(this);
        if (window == null)
            return;

        // DragMove runs the native move loop, which honors the window's
        // no-activate style — the window moves without taking focus.
        window.DragMove();
        if (window is NoActivateWindow noActivate)
            noActivate.KeepOnScreen();
        e.Handled = true;
    }

    private bool IsInsideButton(DependencyObject? node)
    {
        for (; node != null && node != this; node = ParentOf(node))
            if (node is System.Windows.Controls.Primitives.ButtonBase)
                return true;
        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
