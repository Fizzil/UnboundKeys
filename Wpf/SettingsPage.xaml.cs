using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnboundKeys.Themes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace UnboundKeys.Wpf;

// The profile logic here is ProfilesTab.cs's (WinForms), minus the typed
// name box: new profiles are auto-named and renamed through the
// LetterGrid. A profile is a game; clicking its row opens or closes its
// sub-profiles (a class, a loadout), which add, rename and delete the
// same way, and clicking a sub-profile is what switches. Switching is
// delegated to the shell (ProfileSelected, SubProfileSelected) rather
// than done here, since the rail's profile flyout switches too and one
// place must own the KeyMap → MouseMap → VirtualKeyMap → ThemeMode order.
public partial class SettingsPage : IDashboardPage
{
    private const int MaxProfiles = 10;
    private const int MaxSubProfiles = 10;
    private bool _profilesOpen;

    // Which profiles show their sub-profiles. The active one opens by
    // itself whenever it changes; the rest are whatever was clicked.
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastActiveGame;

    // The three presets' accent colors, same values as Theme.cs — kept
    // here as plain colors because only the ACTIVE theme's AccentBrush
    // exists as a resource at any moment.
    private static readonly (string Name, Color Swatch)[] Themes =
    {
        ("Red", Color.FromRgb(230, 70, 30)),
        ("Green", Color.FromRgb(60, 200, 90)),
        ("Blue", Color.FromRgb(60, 140, 230)),
    };

    private readonly Dictionary<string, Button> _swatches = new();

    // What the letter grid is renaming: a profile, or (when SubOf is set)
    // one of that profile's sub-profiles.
    private (string Name, string? SubOf)? _renaming;

    public event Action<string>? ProfileSelected;
    // (profile, sub-profile) — the profile may not be the active one.
    public event Action<string, string>? SubProfileSelected;
    public event Action? ResetAllRequested;
    public event Action? QuitRequested;

    public string Title => "Settings";

    private const string StartupHintText =
        "Starts UnboundKeys when you sign in, already running as administrator, so there is no permission prompt.";

    public SettingsPage()
    {
        InitializeComponent();

        foreach (var (name, color) in Themes)
        {
            string themeName = name;
            var swatch = new Button { Background = new SolidColorBrush(color), ToolTip = themeName };
            swatch.SetResourceReference(StyleProperty, "SwatchButtonStyle");
            swatch.Click += (_, _) => ThemeMode.SwitchTo(themeName);
            _swatches[themeName] = swatch;
            Swatches.Children.Add(swatch);
        }
        ThemeMode.Changed += RefreshSwatches;
        RefreshSwatches();

        NameGrid.Done += OnNameDone;
        NameGrid.Cancelled += EndRename;
        ConfirmDeleteBehavior.AttachTo(ResetAllButton, () => ResetAllRequested?.Invoke());
        ConfirmDeleteBehavior.AttachTo(QuitButton, () => QuitRequested?.Invoke());
        // The new version is running; this one leaves the same way Quit does.
        Updates.Launched += () => QuitRequested?.Invoke();

        StartupHint.Text = StartupHintText;
        ProfilesHeader.Click += (_, _) => SetProfilesOpen(!_profilesOpen);
        SetProfilesOpen(false);
        StartWithWindowsToggle.Tag = Settings.LoadStartWithWindows();
        AutoStartPausedToggle.Tag = Settings.LoadAutoStartPaused();
        AutoStartPausedToggle.IsEnabled = Settings.LoadStartWithWindows();
        StartWithWindowsToggle.Click += (_, _) => ToggleStartWithWindows();
        AutoStartPausedToggle.Click += (_, _) =>
        {
            bool paused = !(AutoStartPausedToggle.Tag is true);
            AutoStartPausedToggle.Tag = paused;
            Settings.SaveStartup(Settings.LoadStartWithWindows(), paused);
        };

        var version = typeof(SettingsPage).Assembly.GetName().Version;
        string title = version == null
            ? "UnboundKeys — built by Fizzil"
            : $"UnboundKeys v{version.Major}.{version.Minor}.{version.Build} — built by Fizzil";
        AboutText.Text = title + "\nWord suggestions use the OpenSubtitles-based FrequencyWords list by Hermit Dave (CC BY-SA 4.0).";

        RebuildProfileList();
    }

    public void Refresh()
    {
        EndRename();
        RebuildProfileList();
        RefreshSwatches();
    }

    private void RefreshSwatches()
    {
        foreach (var (name, swatch) in _swatches)
            swatch.Tag = name == ThemeMode.Current;
    }

    // The list folds away under its heading (Fizzil: it took too much
    // room); closed, the heading carries a one-line summary instead.
    private void SetProfilesOpen(bool open)
    {
        _profilesOpen = open;
        ProfilesBody.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ProfilesSummary.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        ProfilesChevron.Text = ((char)(open ? 0xE70D : 0xE76C)).ToString();
    }

    // Every profile as a row; beneath each open one, its sub-profiles
    // indented, then "+ Add sub-profile"; "+ Add Profile" at the end.
    private void RebuildProfileList()
    {
        ProfileList.Children.Clear();

        string activeGame = KeyMap.ActiveProfile;
        if (_lastActiveGame != activeGame)
        {
            _expanded.Add(activeGame);
            _lastActiveGame = activeGame;
        }

        var names = Settings.LoadProfileNames();
        var activeSubs = Settings.LoadSubProfileNames(activeGame);
        string activeSub = Settings.LoadActiveSubProfile(activeGame);
        ProfilesSummary.Text = activeSubs.Count > 1
            ? $"{names.Count} profile{Plural(names.Count)}, {activeGame} · {activeSub} active"
            : $"{names.Count} profile{Plural(names.Count)}, {activeGame} active";

        foreach (var name in names)
        {
            bool active = name == activeGame;
            bool open = _expanded.Contains(name);
            ProfileList.Children.Add(BuildProfileRow(name, active, open));
            if (!open)
                continue;
            var subs = Settings.LoadSubProfileNames(name);
            string currentSub = Settings.LoadActiveSubProfile(name);
            foreach (var sub in subs)
                ProfileList.Children.Add(BuildSubProfileRow(name, sub, active && sub == currentSub, deletable: subs.Count > 1));
            if (subs.Count < MaxSubProfiles)
                ProfileList.Children.Add(AddButton("+  Add sub-profile", leftMargin: 28, () => CreateSubProfile(name)));
        }

        if (names.Count < MaxProfiles)
            ProfileList.Children.Add(AddButton("+  Add Profile", leftMargin: 8, CreateProfile));
    }

    private static string Plural(int count) => count == 1 ? "" : "s";

    private static Button AddButton(string text, double leftMargin, Action onClick)
    {
        var add = new Button
        {
            Content = text,
            Height = 40,
            Margin = new Thickness(leftMargin, 4, 8, 0),
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };
        add.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        add.Click += (_, _) => onClick();
        return add;
    }

    // The name as a rail-style row (pill + bright text on the active one)
    // with a chevron: clicking it opens or closes the sub-profiles. Then
    // Rename and a two-tap ✕ — neither for Default, which is always there
    // to fall back to.
    private Grid BuildProfileRow(string name, bool active, bool open)
    {
        var row = ThreeColumnRow(height: 48, leftMargin: 0);

        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        if (active)
            content.Children.Add(ActiveMarker());

        var nameButton = new Button { Content = content, Tag = active };
        nameButton.SetResourceReference(StyleProperty, "NavRailButtonStyle");
        nameButton.Click += (_, _) =>
        {
            ToggleExpanded(name);
        };
        row.Children.Add(nameButton);

        // The chevron in its own column at the far right, so every profile
        // row shows it in the same place; it toggles just like the name.
        var chevronButton = new Button { Width = 36, Height = 36, Content = Chevron(open) };
        chevronButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        chevronButton.Click += (_, _) => ToggleExpanded(name);
        Grid.SetColumn(chevronButton, 3);
        row.Children.Add(chevronButton);

        if (name != Settings.DefaultProfileName)
        {
            row.Children.Add(RenameButton(() => BeginRename(name, subOf: null)));
            row.Children.Add(DeleteButton(() =>
            {
                bool wasActive = name == KeyMap.ActiveProfile;
                Settings.DeleteProfile(name);
                _expanded.Remove(name);
                if (wasActive)
                    ProfileSelected?.Invoke(Settings.DefaultProfileName);
                else
                    RebuildProfileList();
            }));
        }

        return row;
    }

    // A sub-profile: indented, a little shorter, the same controls, and
    // the click that actually switches (to another profile too, if it
    // belongs to one). Any sub-profile can be renamed; the last one cannot
    // be deleted (a profile always keeps one).
    private Grid BuildSubProfileRow(string game, string sub, bool active, bool deletable)
    {
        var row = ThreeColumnRow(height: 42, leftMargin: 20);

        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = sub, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        if (active)
            content.Children.Add(ActiveMarker());

        var nameButton = new Button { Content = content, Tag = active };
        nameButton.SetResourceReference(StyleProperty, "NavRailButtonStyle");
        nameButton.Click += (_, _) =>
        {
            if (!active)
                SubProfileSelected?.Invoke(game, sub);
        };
        row.Children.Add(nameButton);

        row.Children.Add(RenameButton(() => BeginRename(sub, subOf: game)));
        if (deletable)
        {
            row.Children.Add(DeleteButton(() =>
            {
                if (!Settings.DeleteSubProfile(game, sub))
                    return;
                if (active)
                    SubProfileSelected?.Invoke(game, Settings.LoadActiveSubProfile(game));
                else
                    RebuildProfileList();
            }));
        }

        return row;
    }

    private void ToggleExpanded(string name)
    {
        if (!_expanded.Remove(name))
            _expanded.Add(name);
        RebuildProfileList();
    }

    private static Grid ThreeColumnRow(double height, double leftMargin)
    {
        var row = new Grid { Height = height, Margin = new Thickness(leftMargin, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // Column 3: the chevron on a profile row, an empty spacer on a
        // sub-profile row, so Rename and the cross line up on both levels.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        return row;
    }

    private static TextBlock ActiveMarker()
    {
        var marker = new TextBlock { Text = "Active", FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        marker.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return marker;
    }

    private static TextBlock Chevron(bool open)
    {
        var chevron = new TextBlock
        {
            Text = ((char)(open ? 0xE70D : 0xE76C)).ToString(),
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return chevron;
    }

    private static Button RenameButton(Action onClick)
    {
        var rename = new Button { Content = "Rename", Width = 80, Height = 36, Margin = new Thickness(0, 0, 4, 0) };
        rename.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        rename.Click += (_, _) => onClick();
        Grid.SetColumn(rename, 1);
        return rename;
    }

    private static Button DeleteButton(Action onConfirmed)
    {
        var delete = new Button();
        delete.SetResourceReference(StyleProperty, "RemoveButtonStyle");
        ConfirmDeleteBehavior.AttachTo(delete, onConfirmed);
        Grid.SetColumn(delete, 2);
        return delete;
    }

    // "Profile 1", "Profile 2", ... — the first number not already in use,
    // so deleting Profile 2 and adding again gives Profile 2 back rather
    // than skipping to 4. Rename is one click away for a real name.
    private static string NextFreeName(List<string> names, string prefix)
    {
        for (int n = 1; ; n++)
        {
            string candidate = $"{prefix} {n}";
            if (!names.Exists(existing => existing.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    // Same fresh-defaults seeding as ProfilesTab.cs's TryCreateProfile
    // (WinForms): words at their natural keys, mouse buttons unmapped. The
    // new profile gets one sub-profile, Default, holding them, and opens.
    private void CreateProfile()
    {
        var names = Settings.LoadProfileNames();
        if (names.Count >= MaxProfiles)
            return;
        string name = NextFreeName(names, "Profile");

        var freshWords = new Dictionary<string, ushort>(KeyMap.DefaultWords, StringComparer.OrdinalIgnoreCase);
        var freshExtraWords = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        var freshBehaviors = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in KeyMap.RemappableWords)
        {
            freshExtraWords[word] = new List<ushort>();
            freshBehaviors[word] = new KeyBehavior();
        }

        var freshMouseEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var freshMouseWords = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase);
        var freshMouseExtraWords = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        var freshMouseBehaviors = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var button in MouseCatalog.Buttons)
        {
            freshMouseEnabled[button.Id] = false;
            freshMouseWords[button.Id] = 0;
            freshMouseExtraWords[button.Id] = new List<ushort>();
            freshMouseBehaviors[button.Id] = new KeyBehavior();
        }

        Settings.CreateProfileIfMissing(
            name, freshWords, freshExtraWords, freshBehaviors,
            freshMouseEnabled, freshMouseWords, freshMouseExtraWords, freshMouseBehaviors);

        _expanded.Add(name);
        RebuildProfileList();
    }

    // A copy of that profile's current sub-profile (Fizzil's choice), and
    // you land on it straight away, ready to change the few keys that
    // differ.
    private void CreateSubProfile(string game)
    {
        var subs = Settings.LoadSubProfileNames(game);
        if (subs.Count >= MaxSubProfiles)
            return;
        string name = NextFreeName(subs, "Sub-profile");
        if (Settings.CreateSubProfile(game, name))
            SubProfileSelected?.Invoke(game, name);
    }

    private static bool IsNameFree(string candidate, string except, string? subOf)
    {
        var names = subOf == null ? Settings.LoadProfileNames() : Settings.LoadSubProfileNames(subOf);
        foreach (var existing in names)
            if (existing != except && existing.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private void BeginRename(string name, string? subOf)
    {
        _renaming = (name, subOf);
        ProfileList.Visibility = Visibility.Collapsed;
        NameGrid.Visibility = Visibility.Visible;
        NameGrid.Begin(name, candidate => IsNameFree(candidate, except: name, subOf));
    }

    private void OnNameDone(string newName)
    {
        var renaming = _renaming;
        EndRename();
        if (renaming is not { } r)
            return;
        string trimmed = newName.Trim();

        if (r.SubOf == null)
        {
            if (!Settings.RenameProfile(r.Name, trimmed))
                return;
            if (_expanded.Remove(r.Name))
                _expanded.Add(trimmed);
            // The active profile's name is what every save is keyed by (see
            // Settings.RenameProfile), so the rename has to be followed by a
            // real switch to the new name; any other profile just needs the
            // list redrawn.
            if (r.Name == KeyMap.ActiveProfile)
                ProfileSelected?.Invoke(trimmed);
            else
                RebuildProfileList();
        }
        else
        {
            bool wasActive = r.SubOf == KeyMap.ActiveProfile && r.Name == Settings.LoadActiveSubProfile(r.SubOf);
            if (!Settings.RenameSubProfile(r.SubOf, r.Name, trimmed))
                return;
            // A sub-profile's name is only ever shown, so a redraw would do;
            // the switch is what also refreshes the rail chip.
            if (wasActive)
                SubProfileSelected?.Invoke(r.SubOf, trimmed);
            else
                RebuildProfileList();
        }
    }

    private void EndRename()
    {
        _renaming = null;
        NameGrid.Visibility = Visibility.Collapsed;
        ProfileList.Visibility = Visibility.Visible;
    }

    // Flips the setting and the scheduled task together. If Task Scheduler
    // refuses, the switch stays where it was and the hint says why.
    private void ToggleStartWithWindows()
    {
        bool on = !(StartWithWindowsToggle.Tag is true);
        try
        {
            if (on)
                StartupTask.Register();
            else
                StartupTask.Unregister();
        }
        catch (Exception ex)
        {
            StartupHint.Text = "Could not change the startup task: " + ex.Message;
            return;
        }
        StartWithWindowsToggle.Tag = on;
        AutoStartPausedToggle.IsEnabled = on;
        Settings.SaveStartup(on, Settings.LoadAutoStartPaused());
        StartupHint.Text = StartupHintText;
    }
}
