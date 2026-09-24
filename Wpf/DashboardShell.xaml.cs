using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Wpf;

// WPF port of DashboardForm.cs's tab-switching shell (WinForms) — minus
// the collapsing-drawer-anchored-to-the-icon positioning mechanics
// (mirroring, WM_SETREDRAW flicker suppression, the fixed group's
// on-screen-edge flipping). That's all specifically about staying glued
// to OverlayForm's draggable icon, which doesn't exist yet — see the
// plan doc's Phase 8. This shell owns the tabs and the content underneath
// them; a fixed-size window for now, closer to a conventional tabbed
// window (matching the Windows folder-properties reference Fizzil
// pointed at) than the original's extend-off-the-icon behavior. Once
// OverlayForm exists, it can reposition this shell the same way it
// already will VirtualKeyboardForm's WPF port — this class doesn't need
// to know anything about that itself.
public partial class DashboardShell
{
    private readonly Dictionary<string, RemapCard> _voiceCards = new();
    private readonly Dictionary<string, Button> _mouseRowValueButtons = new();
    private readonly Dictionary<Button, FrameworkElement> _panelByButton;
    private readonly ProfilesTab _profilesTab = new();
    private Button? _activePrimeButton;
    private Window? _openMousePopup;
    private string? _openMousePopupButtonId;

    internal DashboardShell()
    {
        InitializeComponent();

        _panelByButton = new Dictionary<Button, FrameworkElement>
        {
            [KeyboardTabButton] = KeyboardPanel,
            [MouseTabButton] = MousePanel,
            [VoiceTabButton] = VoicePanel,
            [ProfileTabButton] = ProfilePanel,
            [FadeTabButton] = FadePanel,
        };

        BuildVoiceTab();
        BuildMouseTab();
        ProfilePanel.Content = _profilesTab;
        _profilesTab.ProfileSwitched += () =>
        {
            RefreshVoiceProfileHighlight();
            RefreshMouseProfileHighlight();
        };

        OpenPrimeTab(MouseTabButton);
    }

    // Ten sub-tabs (10..1, left to right — matches what Fizzil's already
    // seen in the WinForms app) each showing that word's RemapCard.
    // Matches DashboardForm's own word-tab loop (WinForms), just built in
    // reverse label order to land in the same visual order without
    // needing a separate re-sort step.
    private void BuildVoiceTab()
    {
        var words = KeyMap.RemappableWords; // ["one".."ten"], label = index + 1

        for (int i = words.Length - 1; i >= 0; i--)
        {
            string word = words[i];
            string label = (i + 1).ToString();

            var card = new RemapCard(KeyMapSource.Instance, word) { Visibility = Visibility.Collapsed };
            _voiceCards[word] = card;
            VoiceCardHost.Children.Add(card);

            var tabButton = new Button
            {
                Content = label,
                Style = (System.Windows.Style)FindResource("ListButtonStyle"),
            };
            tabButton.Click += (_, _) => SelectVoiceWord(word, tabButton);
            VoiceSubTabRow.Children.Add(tabButton);
        }

        // Select whichever word's the natural first one (word "one") by
        // default when the tab first opens.
        SelectVoiceWord(words[0], (Button)VoiceSubTabRow.Children[VoiceSubTabRow.Children.Count - 1]);
    }

    private void SelectVoiceWord(string word, Button tabButton)
    {
        foreach (var card in _voiceCards.Values)
            card.Visibility = Visibility.Collapsed;
        _voiceCards[word].Visibility = Visibility.Visible;

        foreach (Button b in VoiceSubTabRow.Children)
            b.Tag = false;
        tabButton.Tag = true;
    }

    // Repeat/Hold's underline and other per-card visual state already
    // live inside each RemapCard and don't need touching on a profile
    // switch — but every card was built against the profile active when
    // BuildVoiceTab ran, so switching profiles means every card's data is
    // now stale. Simplest fix: throw them away and rebuild, same as
    // DashboardForm's own SwitchToProfile does (WinForms) — a fresh,
    // correctly-profiled card per word rather than trying to refresh one
    // in place.
    private void RefreshVoiceProfileHighlight()
    {
        string? selectedWord = null;
        foreach (var (word, card) in _voiceCards)
            if (card.Visibility == Visibility.Visible)
                selectedWord = word;

        VoiceCardHost.Children.Clear();
        _voiceCards.Clear();

        var words = KeyMap.RemappableWords;
        for (int i = words.Length - 1; i >= 0; i--)
        {
            string word = words[i];
            var card = new RemapCard(KeyMapSource.Instance, word) { Visibility = Visibility.Collapsed };
            _voiceCards[word] = card;
            VoiceCardHost.Children.Add(card);
        }

        string toSelect = selectedWord != null && _voiceCards.ContainsKey(selectedWord) ? selectedWord : words[0];
        _voiceCards[toSelect].Visibility = Visibility.Visible;
    }

    // All six buttons as compact rows, stacked vertically — full name on
    // the left, current mapping on the right — matching the X-Mouse
    // Button Control reference Fizzil pointed at exactly: a vertical list
    // of summary rows, not the full remap card expanded inline for every
    // button at once. Clicking a row's value opens that button's full
    // RemapCard in a popup instead (see OpenMouseButtonPopup) — same
    // "compact row expands into a floating card" idea as
    // VirtualKeyRemapPopup will be for the keyboard, Phase 7.
    // Right/Middle/Button4/Button5 are physical buttons; Wheel Up/Down are
    // scroll actions — two genuinely different kinds of input that used to
    // render as one undifferentiated column of rows. Discord's own
    // settings pages group related rows under a small muted uppercase
    // label rather than leaving everything in one flat list, so this
    // splits the same way here — grouped by array position (MouseCatalog's
    // own ordering already puts the four buttons before the two wheel
    // entries) rather than adding a Category field to ButtonInfo for what
    // is, for now, a fixed two-group split.
    private void BuildMouseTab()
    {
        _mouseRowValueButtons.Clear();
        AddMouseGroupLabel("BUTTONS");
        foreach (var button in MouseCatalog.Buttons)
        {
            if (button.Id == "wheelup")
                AddMouseGroupLabel("SCROLL WHEEL");
            MouseCardHost.Children.Add(BuildMouseButtonRow(button));
        }
    }

    private void AddMouseGroupLabel(string text)
    {
        MouseCardHost.Children.Add(new TextBlock
        {
            Text = text,
            Style = (System.Windows.Style)FindResource("SectionHeaderStyle"),
            Margin = new Thickness(0, 8, 0, 4),
        });
    }

    private Grid BuildMouseButtonRow(MouseCatalog.ButtonInfo button)
    {
        // Taller than the other rows in this app (56 vs the usual 40) —
        // with only six rows and a fixed-size content area to fill,
        // condensed rows just left a lot of dead space below them
        // (Fizzil's own feedback) — but plain default font size (not
        // bumped up): the actual fix for the label/value feeling cramped
        // together was the row itself stretching to the real window
        // width (see DashboardShell.xaml's own comment), not bigger text.
        var row = new Grid { Height = 56, Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // A hairline under each row — dark-mode/settings-panel research
        // (Fizzil's own request to look into this) points at a real
        // divider line, not just margin gap, for making a label column
        // scannable. Low-opacity Muted rather than full brightness, so it
        // reads as a subtle seam, not another bright accent element.
        var divider = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = (System.Windows.Media.Brush)FindResource("MutedBrush"),
            Opacity = 0.35,
        };
        Grid.SetColumnSpan(divider, 2);
        row.Children.Add(divider);

        // Plain text, no background fill — LabelDisplayStyle's own
        // ButtonBrush fill made sense next to RemapCard's equally-filled
        // ValueButtonStyle (Key row), where they read as one continuous
        // bar; here the value side is a small bordered chip with no fill
        // (see OutlineButtonStyle below), so a filled label would look
        // mismatched sitting next to it. Overridden per-instance rather
        // than changing LabelDisplayStyle itself, which RemapCard's Key
        // row still relies on.
        // FontSize/Weight matched to the tab strip's own text (13, plain)
        // rather than LabelDisplayStyle's default 16 — Fizzil's own
        // feedback: it read as too bright/bold at full size sitting next
        // to the value chip. TextSecondaryBrush (not the style's own
        // TextPrimaryBrush) matches the chip's own neutral secondary
        // text, so the row reads as one coherent line rather than a loud
        // label next to a quiet value.
        var labelText = new TextBlock
        {
            Text = button.Label,
            Style = (System.Windows.Style)FindResource("LabelDisplayStyle"),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            FontSize = 13,
            FontWeight = System.Windows.FontWeights.Normal,
        };
        Grid.SetColumn(labelText, 0);
        row.Children.Add(labelText);

        // OutlineButtonStyle, not ValueButtonStyle — Fizzil's own
        // feedback: ValueButtonStyle's full-cell fill read as "one large
        // gray highlight" rather than a neat little button. A fixed
        // Width (not sized to its own text) so every row's chip lines up
        // at the same size regardless of whether the mapping is short
        // ("A") or long ("Not Mapped") — matching the X-Mouse Button
        // Control reference's own uniform dropdown column, rather than
        // each one shrinking/growing to fit its own content.
        var (_, value) = SplitKeyLabel(MouseMapSource.Instance.KeyLabelFor(button.Id));
        var valueButton = new Button
        {
            Content = value,
            Width = 150,
            Style = (System.Windows.Style)FindResource("OutlineButtonStyle"),
        };
        valueButton.Click += (_, _) => ToggleMouseButtonPopup(button, valueButton);
        Grid.SetColumn(valueButton, 1);
        row.Children.Add(valueButton);

        _mouseRowValueButtons[button.Id] = valueButton;
        return row;
    }

    // Same "label: value" split every other card here uses — see
    // RemapCard.xaml.cs's own copy of this for why it's duplicated rather
    // than shared mid-migration.
    private static (string Label, string Value) SplitKeyLabel(string full)
    {
        int i = full.IndexOf(": ", System.StringComparison.Ordinal);
        return i < 0 ? (full, "") : (full[..i], full[(i + 2)..]);
    }

    // Opens (or closes, if already open for this same button) a floating
    // popup hosting that button's full RemapCard — Key, Repeat/Hold/Reset,
    // the timing row, all of it. RemapCard's own StaticResource lookups
    // resolve via Application.Resources regardless of which window hosts
    // it (see PreviewProgram.cs's own comment), so no per-popup resource
    // merging is needed here the way the WinForms-era popups needed.
    private void ToggleMouseButtonPopup(MouseCatalog.ButtonInfo button, FrameworkElement anchor)
    {
        bool wasOpenForThisButton = _openMousePopupButtonId == button.Id;
        _openMousePopup?.Close();
        _openMousePopup = null;
        _openMousePopupButtonId = null;
        if (wasOpenForThisButton)
            return;

        var card = new RemapCard(MouseMapSource.Instance, button.Id);
        var popup = new NoActivateWindow
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush"),
            Content = card,
        };

        var anchorDevicePoint = anchor.PointToScreen(new System.Windows.Point(anchor.ActualWidth, 0));
        var source = PresentationSource.FromVisual(anchor);
        double scale = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        popup.Left = anchorDevicePoint.X * scale + 8;
        popup.Top = anchorDevicePoint.Y * scale;

        // The row's own value text only reflects the card's state at the
        // moment the popup was opened — refresh it once the popup closes
        // (whether from picking a key, resetting, or just clicking the
        // row again) so it doesn't go stale.
        popup.Closed += (_, _) =>
        {
            if (_mouseRowValueButtons.TryGetValue(button.Id, out var valueButton))
            {
                var (_, value) = SplitKeyLabel(MouseMapSource.Instance.KeyLabelFor(button.Id));
                valueButton.Content = value;
            }
        };

        _openMousePopup = popup;
        _openMousePopupButtonId = button.Id;
        popup.Show();
    }

    // Same "rebuild fresh, stale after a profile switch" reasoning as
    // RefreshVoiceProfileHighlight.
    private void RefreshMouseProfileHighlight()
    {
        _openMousePopup?.Close();
        _openMousePopup = null;
        _openMousePopupButtonId = null;
        MouseCardHost.Children.Clear();
        BuildMouseTab();
    }

    // Matches DashboardForm's own TogglePrimeTab (WinForms): clicking the
    // already-open tab collapses everything; clicking a different one
    // switches straight to it.
    // Clicking the tab that's already open just stays put — no toggle-
    // closed-and-reopen (Fizzil's own feedback: the WinForms dashboard's
    // collapse-on-reclick behavior read as content randomly vanishing).
    private void PrimeTab_Click(object sender, RoutedEventArgs e)
    {
        var clicked = (Button)sender;
        if (_activePrimeButton != clicked)
            OpenPrimeTab(clicked);
    }

    // Shared by the click handler and the constructor's own default-open
    // (Mouse Keys, Fizzil's own priority — the WinForms dashboard instead
    // always started with nothing open).
    private void OpenPrimeTab(Button? button)
    {
        if (_activePrimeButton != null)
        {
            _activePrimeButton.Tag = false;
            _panelByButton[_activePrimeButton].Visibility = Visibility.Collapsed;
        }

        _activePrimeButton = button;

        if (_activePrimeButton != null)
        {
            _activePrimeButton.Tag = true;
            _panelByButton[_activePrimeButton].Visibility = Visibility.Visible;
        }
    }
}
