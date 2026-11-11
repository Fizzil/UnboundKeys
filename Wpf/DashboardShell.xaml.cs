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
// navigation stack. Help adds one remembered place, where it was opened
// from, for its ‹ Back (see DashboardBack). Positioning next to the overlay icon is still the
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
    // The open editor as a place to come back to (see DashboardBack).
    private DashboardPlace? _editorPlace;
    private readonly DashboardBack _back = new();
    private VirtualKeyboardWindow? _keyboard;

    public event Action? MinimizeRequested;
    public event Action? CloseRequested;
    public event Action? QuitRequested;
    public event Action? ResetAppRequested;
    // The keyboard Menu key: the window shows or hides itself (Fizzil: one
    // key, both ways, like the tray icon).
    public event Action? ToggleRequested;

    internal DashboardShell()
    {
        // The preview harness merges Red unconditionally; the real
        // starting point is whatever the active profile saved.
        ThemeSwapper.Apply(ThemeMode.Current);
        InitializeComponent();

        var mousePage = new MousePage();
        mousePage.EditRequested += OpenEditor;
        _pages[DashboardSection.Mouse] = mousePage;
        var steeringPage = new SteeringPage();
        steeringPage.EditRequested += OpenEditor;
        steeringPage.NavigateRequested += ShowSection;
        _pages[DashboardSection.Steering] = steeringPage;
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
        settingsPage.SubProfileSelected += SwitchToSubProfile;
        settingsPage.ResetAllRequested += ResetAllMappings;
        settingsPage.QuitRequested += () => QuitRequested?.Invoke();
        settingsPage.HelpRequested += OpenHelp;
        settingsPage.ResetAppRequested += () => ResetAppRequested?.Invoke();
        _pages[DashboardSection.Settings] = settingsPage;
        _pages[DashboardSection.Help] = new HelpPage();

        Rail.SectionSelected += ShowSection;
        Rail.ProfileChipClicked += ToggleProfileFlyout;
        Rail.ListeningToggled += ListeningMode.Toggle;
        Rail.FadeToggled += FadeMode.Toggle;
        Rail.QuitRequested += () => QuitRequested?.Invoke();
        // The rail's Stop: the same release as "press stop", off the UI thread.
        Rail.StopRequested += () => System.Threading.Tasks.Task.Run(KeyExecutor.ReleaseAll);
        ListeningMode.Changed += () => Rail.SetListening(!ListeningMode.IsPaused);
        FadeMode.Changed += () => Rail.SetFade(FadeMode.IsOn);
        // Covers a swatch click and a profile switch alike — ThemeMode
        // raises Changed for both.
        ThemeMode.Changed += () => ThemeSwapper.Apply(ThemeMode.Current);

        ShowProfileOnRail();
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
        _editorPlace = null;
        _back.Shown(section);
        Rail.SetActive(section);
        HelpChromeButton.Tag = section == DashboardSection.Help;
        // ‹ Back shows on Help when there is somewhere to go back to.
        BackButton.Visibility = section == DashboardSection.Help && _back.BeforeHelp != null
            ? Visibility.Visible
            : Visibility.Collapsed;
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
    // shown and hidden from the Keyboard page's switch. Reopens in
    // whichever of Mini/Maxi it was last left in. The switch's state is
    // remembered (Settings.KeyboardShown) so the next start can put the
    // keyboard back — saved here, on the user's own open and close, and
    // never on quit, which closes the window without meaning "hidden".
    private void ToggleKeyboard()
    {
        if (_keyboard != null)
        {
            _keyboard.Close();
            Settings.SaveKeyboardShown(false);
            return;
        }

        OpenKeyboard();
        Settings.SaveKeyboardShown(true);
    }

    // At startup: the keyboard comes back if it was open when the app was
    // last quit. True if it did (Program then leaves the dashboard hidden;
    // the keyboard's Menu key brings it up).
    public bool RestoreKeyboard()
    {
        if (_keyboard != null || !Settings.LoadKeyboardShown())
            return false;
        OpenKeyboard();
        return true;
    }

    private void OpenKeyboard()
    {
        var (_, _, mini) = Settings.LoadKeyboardPlacement();
        var keyboard = new VirtualKeyboardWindow(mini);
        keyboard.Closed += (_, _) =>
        {
            _keyboard = null;
            _keyboardPage.SetKeyboardShown(false);
        };
        keyboard.PlaceNear(Window.GetWindow(this));
        _keyboard = keyboard;
        keyboard.MenuRequested += () => ToggleRequested?.Invoke();
        _keyboardPage.SetKeyboardShown(true);
        keyboard.Show();
    }

    // The editor page: the section's list is replaced by that one
    // mapping's RemapCard, with "‹ Back" in the header. A fresh card each
    // time — it reads its state from the source on construction, so
    // nothing can be stale.
    private void OpenEditor(IRemapSource source, string id, string title)
    {
        // Logged with the mapping changes (see RemapStore.LogChange), so a
        // change nobody meant can be traced to the editor it came through.
        Log.Info($"editor opened: {title} ({id})");
        // Push up and Push down (SteeringMapSource) have one way of being
        // pressed, so they get the plain editor, as a word does: no Single
        // press / Double press.
        bool twoWays = source is MouseMapSource or VirtualKeyMapSource;
        _editor = twoWays ? BuildGestureEditor(source, id, title) : NewCard(source, id);
        _editorPlace = new DashboardPlace(_section, source, id, title);
        BackButton.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageHost.Content = _editor;
        Body.ScrollToTop();
    }

    private RemapCard NewCard(IRemapSource source, string id, bool? doublePress = null)
    {
        var card = new RemapCard(source, id, doublePress);
        return card;
    }

    // A mouse button's or a Keyboard-page key's editor (Fizzil, 2026-10-08,
    // after Helldivers 2): a row of two segments for the ways of pressing
    // it, Single press and Double press (see Gestures), each a whole
    // mapping of its own, with its card below. Since 2026-10-13 (Fizzil:
    // few people need it) the segments show only while the card's Double
    // press switch is on, which it is whenever a double press is mapped.
    // Opens on the way the clicked row, hotspot or key was for.
    private FrameworkElement BuildGestureEditor(IRemapSource source, string id, string title)
    {
        var (baseId, gesture) = Gestures.Split(id);
        bool doubleOn = gesture == Gesture.Double || source.IsMapped(Gestures.IdFor(baseId, Gesture.Double));
        var page = new StackPanel();

        var group = new Border
        {
            Margin = new Thickness(8, 0, 0, 6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Visibility = doubleOn ? Visibility.Visible : Visibility.Collapsed,
        };
        group.SetResourceReference(StyleProperty, "SegmentGroupStyle");
        var segments = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Width = 256 };
        group.Child = segments;
        page.Children.Add(group);

        var host = new ContentControl { HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
        page.Children.Add(host);

        var buttons = new Dictionary<Gesture, Button>();
        var showing = gesture;
        void Select(Gesture chosen)
        {
            showing = chosen;
            foreach (var (other, button) in buttons)
                button.Tag = other == chosen;
            string gestureId = Gestures.IdFor(baseId, chosen);
            if (Log.Enabled && gestureId != id)
                Log.Info($"editor opened: {title} ({gestureId})");
            var card = NewCard(source, gestureId, doubleOn);
            card.DoublePressChanged += OnDoublePress;
            host.Content = card;
        }

        // On: straight to the double press, Not Mapped, to be set up, with
        // the segments above it. Off (the card has cleared the double
        // press): back to the single press if that is not already showing.
        void OnDoublePress(bool on)
        {
            doubleOn = on;
            group.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on || showing == Gesture.Double)
            {
                Select(on ? Gesture.Double : Gesture.Single);
                Body.ScrollToTop();
            }
        }

        foreach (var way in Gestures.All)
        {
            var button = new Button { Content = Gestures.Label(way) };
            button.SetResourceReference(StyleProperty, "SegmentButtonStyle");
            var mine = way;
            button.Click += (_, _) => Select(mine);
            segments.Children.Add(button);
            buttons[way] = button;
        }
        Select(gesture);
        return page;
    }

    private void CloseEditor()
    {
        if (_editor == null)
            return;
        ShowSection(_section);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => GoBack();

    // Help, remembering where it was opened from (see DashboardBack): the
    // ? at the top right and Help in Settings.
    private void OpenHelp()
    {
        _back.HelpOpened(_editor != null && _editorPlace != null ? _editorPlace : new DashboardPlace(_section));
        ShowSection(DashboardSection.Help);
    }

    // Back out of an editor to its page, or out of Help to where it was
    // opened from (see DashboardBack); nothing anywhere else.
    private void GoBack()
    {
        switch (_back.Next(_editor != null, _section))
        {
            case DashboardBack.Step.CloseEditor:
                CloseEditor();
                break;
            case DashboardBack.Step.LeaveHelp:
                var to = _back.BeforeHelp!;
                ShowSection(to.Section);
                if (to.Source != null && to.Id != null)
                    OpenEditor(to.Source, to.Id, to.Title ?? "");
                break;
        }
    }

    // Right-click anywhere is Back (Fizzil, 2026-11-11): out of an editor,
    // or out of Help, the same as the "‹ Back" button, which stays for the
    // left button. On a list page it does nothing. It reaches here even
    // with the right button mapped: over UnboundKeys' own windows the
    // mouse hook lets a right-click through (see MouseInputWatcher).
    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);
        if (_back.Next(_editor != null, _section) == DashboardBack.Step.Nothing)
            return;
        GoBack();
        e.Handled = true;
    }

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

        if (Log.Enabled)
            Log.Info($"profile: {name} / {Settings.LoadActiveSubProfile(name)}");
        KeyMap.SwitchProfile(name);
        MouseMap.SwitchProfile(name);
        VirtualKeyMap.SwitchProfile(name);
        ThemeMode.SwitchProfile(name);
        ShowProfileOnRail();
        ShowSection(_section);
    }

    // The rail chip: the profile, plus its sub-profile when it has more
    // than one.
    private void ShowProfileOnRail()
    {
        string game = KeyMap.ActiveProfile;
        bool several = Settings.LoadSubProfileNames(game).Count > 1;
        Rail.SetProfileName(game, several ? Settings.LoadActiveSubProfile(game) : "");
    }

    // A switch to (profile, sub-profile). Another profile: point it at that
    // sub-profile first, then the normal profile switch loads it, theme
    // included. The same profile: Settings now resolves the name to the new
    // sub-profile, so the stores are switched to the same name and pick it
    // up; the theme stays, it belongs to the profile.
    private void SwitchToSubProfile(string game, string sub)
    {
        if (!Settings.SetActiveSubProfile(game, sub))
            return;
        if (game != KeyMap.ActiveProfile)
        {
            SwitchToProfile(game);
            return;
        }
        Log.Info($"profile: {game} / {sub}");
        KeyMap.SwitchProfile(game);
        MouseMap.SwitchProfile(game);
        VirtualKeyMap.SwitchProfile(game);
        ShowProfileOnRail();
        ShowSection(_section);
    }

    // Reached from Settings' Reset profile — every mapping of every kind,
    // then the page redrawn.
    private void ResetAllMappings()
    {
        KeyMap.ResetAll();
        MouseMap.ResetAll();
        VirtualKeyMap.ResetAll();
        ShowSection(_section);
    }

    // Rebuilt on every open — profiles are few and can change from the
    // Settings page in between.
    // Which profiles in the flyout show their sub-profiles: all closed each
    // time it opens, so the profiles read at a glance (Fizzil), then as
    // clicked while it stays open.
    private readonly HashSet<string> _flyoutExpanded = new(StringComparer.OrdinalIgnoreCase);

    private void ToggleProfileFlyout()
    {
        if (FlyoutLayer.Visibility == Visibility.Visible)
        {
            CloseProfileFlyout();
            return;
        }
        _flyoutExpanded.Clear();
        RebuildFlyout();
        FlyoutLayer.Visibility = Visibility.Visible;
        Rail.SetProfileFlyoutOpen(true);
    }

    private void RebuildFlyout()
    {
        string activeGame = KeyMap.ActiveProfile;

        FlyoutList.Children.Clear();
        foreach (var name in Settings.LoadProfileNames())
        {
            string profile = name;
            bool isActive = profile == activeGame;
            bool open = _flyoutExpanded.Contains(profile);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            var button = new Button { Content = FlyoutLabel(profile, isActive), Tag = isActive, Height = 40, Margin = new Thickness(0, 1, 0, 1) };
            button.SetResourceReference(StyleProperty, "NavRailButtonStyle");
            // The name opens or closes the sub-profiles, like Settings; a
            // sub-profile is what switches.
            button.Click += (_, _) => ToggleFlyoutProfile(profile);
            row.Children.Add(button);

            var chevron = new Button { Width = 32, Height = 32, Content = FlyoutChevron(open) };
            chevron.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            chevron.Click += (_, _) => ToggleFlyoutProfile(profile);
            Grid.SetColumn(chevron, 1);
            row.Children.Add(chevron);
            FlyoutList.Children.Add(row);

            if (!open)
                continue;
            string currentSub = Settings.LoadActiveSubProfile(profile);
            foreach (var subName in Settings.LoadSubProfileNames(profile))
            {
                string sub = subName;
                var subButton = new Button { Content = FlyoutLabel(sub, isActive && sub == currentSub), Tag = isActive && sub == currentSub, Height = 36, FontSize = 13, Margin = new Thickness(20, 1, 32, 1) };
                subButton.SetResourceReference(StyleProperty, "NavRailButtonStyle");
                subButton.Click += (_, _) =>
                {
                    CloseProfileFlyout();
                    SwitchToSubProfile(profile, sub);
                };
                FlyoutList.Children.Add(subButton);
            }
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
        manage.Click += (_, _) =>
        {
            ShowSection(DashboardSection.Settings);
            ((SettingsPage)_pages[DashboardSection.Settings]).ShowProfiles();
        };
        FlyoutList.Children.Add(manage);
    }

    private void ToggleFlyoutProfile(string profile)
    {
        if (!_flyoutExpanded.Remove(profile))
            _flyoutExpanded.Add(profile);
        RebuildFlyout();
    }

    // The name, plus a small Active beside the one in use.
    private static StackPanel FlyoutLabel(string name, bool active)
    {
        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        if (active)
        {
            var marker = new TextBlock { Text = "Active", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            marker.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            content.Children.Add(marker);
        }
        return content;
    }

    private static TextBlock FlyoutChevron(bool open)
    {
        var chevron = new TextBlock
        {
            Text = ((char)(open ? 0xE70D : 0xE76C)).ToString(),
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return chevron;
    }

    private void CloseProfileFlyout()
    {
        FlyoutLayer.Visibility = Visibility.Collapsed;
        Rail.SetProfileFlyoutOpen(false);
    }

    private void FlyoutDismiss_Click(object sender, MouseButtonEventArgs e) => CloseProfileFlyout();

    // The ? toggles (Fizzil): Help, then clicked again, back to where you
    // were.
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        if (_section == DashboardSection.Help && _editor == null)
            GoBack();
        else
            OpenHelp();
    }


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
