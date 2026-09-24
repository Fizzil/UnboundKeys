using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UnboundKeys.Themes;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Key = System.Windows.Input.Key;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;

namespace UnboundKeys.Wpf;

// WPF port of ProfilesTab.cs (WinForms). Standalone for now — no
// DashboardForm shell yet to coordinate rebuilding every other open card
// on a profile switch, so this drives the real KeyMap/MouseMap/
// VirtualKeyMap/ThemeMode.SwitchProfile sequence itself (same order
// DashboardForm.SwitchToProfile uses) and raises ProfileSwitched for
// whatever's hosting it to react to.
public partial class ProfilesTab
{
    private const int MaxProfiles = 10;
    private const string NamePlaceholder = "Profile name...";
    private const int MaxNameLength = 15;

    private readonly HashSet<string> _existingNames;
    private bool _namingExpanded;

    public event Action? ProfileSwitched;

    internal ProfilesTab()
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(Settings.LoadProfileNames());
        RebuildList();
    }

    // Rebuilt from scratch on every change — same "clear and re-add"
    // pattern as RemapCard, matching ProfilesTab.cs's own
    // RebuildProfilesList (WinForms).
    private void RebuildList()
    {
        ProfileList.Children.Clear();
        bool atProfileCap = _existingNames.Count >= MaxProfiles;

        if (!atProfileCap)
        {
            var addProfileButton = new Button
            {
                Content = "Add Profile",
                Height = 40,
                Margin = new Thickness(0, 0, 0, 1),
                Style = (System.Windows.Style)FindResource("ListButtonStyle"),
            };
            addProfileButton.Click += (_, _) =>
            {
                _namingExpanded = !_namingExpanded;
                RebuildList();
            };
            ProfileList.Children.Add(addProfileButton);

            if (_namingExpanded)
                ProfileList.Children.Add(BuildNamingRow());
        }

        foreach (var name in _existingNames)
            ProfileList.Children.Add(BuildProfileRow(name));
    }

    // No border box — the field shows its own ghost/placeholder text
    // until clicked, same idea as ProfilesTab.cs's own nameBox (WinForms).
    // WPF's TextBox centers vertically inside a Height'd Grid cell on its
    // own, so the WinForms version's nameBoxHost/manual-centering
    // workaround (a single-line TextBox there ignores Dock=Fill and
    // snaps to its own preferred height) isn't needed here at all.
    private Grid BuildNamingRow()
    {
        var row = new Grid
        {
            Height = 40,
            Margin = new Thickness(0, 0, 0, 1),
            Background = (Brush)FindResource("ButtonBrush"),
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15, GridUnitType.Star) });

        var nameBox = new TextBox
        {
            Text = NamePlaceholder,
            Foreground = (Brush)FindResource("AccentBrush"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 16,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLength = MaxNameLength,
        };
        nameBox.GotFocus += (_, _) =>
        {
            if (nameBox.Text == NamePlaceholder)
                nameBox.Text = "";
        };
        nameBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(nameBox.Text))
                nameBox.Text = NamePlaceholder;
        };
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                TryCreateProfile(nameBox.Text);
            }
        };
        Grid.SetColumn(nameBox, 1);
        row.Children.Add(nameBox);

        var confirmButton = new Button
        {
            Content = "✓",
            Style = (System.Windows.Style)FindResource("TinyButtonStyle"),
        };
        confirmButton.Click += (_, _) => TryCreateProfile(nameBox.Text);
        Grid.SetColumn(confirmButton, 2);
        row.Children.Add(confirmButton);

        return row;
    }

    // A name button (click to switch to it) plus, for every profile but
    // Default, an always-visible two-tap confirm-delete "✕". Matches
    // ProfilesTab.cs's own AddProfileRow (WinForms).
    private Grid BuildProfileRow(string name)
    {
        bool deletable = name != Settings.DefaultProfileName;

        var row = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 1) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(deletable ? 80 : 100, GridUnitType.Star) });
        if (deletable)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20, GridUnitType.Star) });

        var nameButton = new Button
        {
            Content = name,
            Tag = name == KeyMap.ActiveProfile,
            Style = (System.Windows.Style)FindResource("ListButtonStyle"),
        };
        nameButton.Click += (_, _) =>
        {
            // Picking a profile while the naming row is still open (from
            // an earlier "Add Profile" tap) should close it, same as
            // tapping "Add Profile" again would.
            _namingExpanded = false;
            SwitchToProfile(name);
        };
        Grid.SetColumn(nameButton, 0);
        row.Children.Add(nameButton);

        if (deletable)
        {
            var deleteButton = new Button
            {
                Content = "✕",
                Style = (System.Windows.Style)FindResource("ToggleFillButtonStyle"),
            };
            ConfirmDeleteBehavior.AttachTo(deleteButton, () =>
            {
                Settings.DeleteProfile(name);
                _existingNames.Remove(name);

                bool wasActive = KeyMap.ActiveProfile == name;
                RebuildList();
                if (wasActive)
                    SwitchToProfile(Settings.DefaultProfileName);
            });
            Grid.SetColumn(deleteButton, 1);
            row.Children.Add(deleteButton);
        }

        return row;
    }

    // Same KeyMap -> MouseMap -> VirtualKeyMap -> ThemeMode order as
    // DashboardForm.SwitchToProfile (WinForms) — KeyMap's own
    // SwitchProfile calls KeyExecutor.ReleaseAll() before touching
    // anything, so this order matters: it must happen before Mouse/
    // VirtualKeyMap's own SwitchProfile calls, same reasoning as there.
    private void SwitchToProfile(string profileName)
    {
        KeyMap.SwitchProfile(profileName);
        MouseMap.SwitchProfile(profileName);
        VirtualKeyMap.SwitchProfile(profileName);
        ThemeMode.SwitchProfile(profileName);
        RebuildList();
        ProfileSwitched?.Invoke();
    }

    // Matches ProfilesTab.cs's own TryCreateProfile (WinForms) exactly —
    // same fresh-defaults seeding for words/mouse buttons.
    private void TryCreateProfile(string enteredText)
    {
        if (_existingNames.Count >= MaxProfiles)
            return;

        var name = enteredText == NamePlaceholder ? "" : enteredText.Trim();
        if (string.IsNullOrEmpty(name) || _existingNames.Contains(name))
            return;

        var freshWords = new Dictionary<string, ushort>(KeyMap.DefaultWords, StringComparer.OrdinalIgnoreCase);
        var freshExtraWords = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        var freshBehaviors = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in KeyMap.RemappableWords)
        {
            freshExtraWords[word] = new List<ushort>();
            freshBehaviors[word] = new KeyBehavior();
        }

        // Mouse buttons and physical keys start unmapped in every new
        // profile, same as a freshly-installed UnboundKeys — there's no
        // equivalent of a word's "natural" default key for either.
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

        _existingNames.Add(name);
        _namingExpanded = false;
        RebuildList();
    }
}
