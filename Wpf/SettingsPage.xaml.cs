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
// LetterGrid. Switching is delegated to the shell
// (ProfileSelected) rather than done here, since the rail's profile
// flyout switches too and one place must own the KeyMap → MouseMap →
// VirtualKeyMap → ThemeMode order.
public partial class SettingsPage : IDashboardPage
{
    private const int MaxProfiles = 10;

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
    private string? _renaming;

    public event Action<string>? ProfileSelected;
    public event Action? ResetAllRequested;

    public string Title => "Settings";

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

        var version = typeof(SettingsPage).Assembly.GetName().Version;
        AboutText.Text = version == null
            ? "UnboundKeys — built by Fizzil"
            : $"UnboundKeys v{version.Major}.{version.Minor}.{version.Build} — built by Fizzil";

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

    private void RebuildProfileList()
    {
        ProfileList.Children.Clear();

        var names = Settings.LoadProfileNames();
        foreach (var name in names)
            ProfileList.Children.Add(BuildProfileRow(name, name == KeyMap.ActiveProfile));

        if (names.Count < MaxProfiles)
        {
            var add = new Button
            {
                Content = "+  Add Profile",
                Height = 40,
                Margin = new Thickness(8, 4, 8, 0),
                Padding = new Thickness(12, 0, 12, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            };
            add.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            add.Click += (_, _) => CreateProfile();
            ProfileList.Children.Add(add);
        }
    }

    // The name as a rail-style row (pill + bright text on the active one),
    // then Rename and a two-tap ✕ — neither for Default, which is always
    // there to fall back to.
    private Grid BuildProfileRow(string name, bool active)
    {
        var row = new Grid { Height = 48 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var content = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        if (active)
        {
            var marker = new TextBlock { Text = "Active", FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            marker.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            content.Children.Add(marker);
        }

        var nameButton = new Button { Content = content, Tag = active };
        nameButton.SetResourceReference(StyleProperty, "NavRailButtonStyle");
        nameButton.Click += (_, _) =>
        {
            if (!active)
                ProfileSelected?.Invoke(name);
        };
        row.Children.Add(nameButton);

        if (name != Settings.DefaultProfileName)
        {
            var rename = new Button { Content = "Rename", Width = 80, Height = 36, Margin = new Thickness(0, 0, 4, 0) };
            rename.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            rename.Click += (_, _) => BeginRename(name);
            Grid.SetColumn(rename, 1);
            row.Children.Add(rename);

            var delete = new Button();
            delete.SetResourceReference(StyleProperty, "RemoveButtonStyle");
            ConfirmDeleteBehavior.AttachTo(delete, () =>
            {
                bool wasActive = name == KeyMap.ActiveProfile;
                Settings.DeleteProfile(name);
                if (wasActive)
                    ProfileSelected?.Invoke(Settings.DefaultProfileName);
                else
                    RebuildProfileList();
            });
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
        }

        return row;
    }

    // "Profile 1", "Profile 2", ... — the first number not already in use,
    // so deleting Profile 2 and adding again gives Profile 2 back rather
    // than skipping to 4. Rename is one click away for a real name.
    private static string NextFreeName(List<string> names)
    {
        for (int n = 1; ; n++)
        {
            string candidate = $"Profile {n}";
            if (!names.Exists(existing => existing.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    // Same fresh-defaults seeding as ProfilesTab.cs's TryCreateProfile
    // (WinForms): words at their natural keys, mouse buttons unmapped.
    private void CreateProfile()
    {
        var names = Settings.LoadProfileNames();
        if (names.Count >= MaxProfiles)
            return;
        string name = NextFreeName(names);

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

        RebuildProfileList();
    }

    private static bool IsNameFree(string candidate, string? except)
    {
        foreach (var existing in Settings.LoadProfileNames())
            if (existing != except && existing.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private void BeginRename(string name)
    {
        _renaming = name;
        ProfileList.Visibility = Visibility.Collapsed;
        NameGrid.Visibility = Visibility.Visible;
        NameGrid.Begin(name, candidate => IsNameFree(candidate, except: name));
    }

    private void OnNameDone(string newName)
    {
        string? oldName = _renaming;
        EndRename();
        if (oldName == null || !Settings.RenameProfile(oldName, newName))
            return;

        // The active profile's name is what every save is keyed by (see
        // Settings.RenameProfile), so the rename has to be followed by a
        // real switch to the new name; any other profile just needs the
        // list redrawn.
        if (oldName == KeyMap.ActiveProfile)
            ProfileSelected?.Invoke(newName.Trim());
        else
            RebuildProfileList();
    }

    private void EndRename()
    {
        _renaming = null;
        NameGrid.Visibility = Visibility.Collapsed;
        ProfileList.Visibility = Visibility.Visible;
    }
}
