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
    private readonly Dictionary<Button, FrameworkElement> _panelByButton;
    private readonly ProfilesTab _profilesTab = new();
    private Button? _activePrimeButton;

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
        ProfilePanel.Content = _profilesTab;
        _profilesTab.ProfileSwitched += RefreshVoiceProfileHighlight;
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

    // Matches DashboardForm's own TogglePrimeTab (WinForms): clicking the
    // already-open tab collapses everything; clicking a different one
    // switches straight to it.
    private void PrimeTab_Click(object sender, RoutedEventArgs e)
    {
        var clicked = (Button)sender;
        bool wasOpen = _activePrimeButton == clicked;

        if (_activePrimeButton != null)
        {
            _activePrimeButton.Tag = false;
            _panelByButton[_activePrimeButton].Visibility = Visibility.Collapsed;
        }

        _activePrimeButton = wasOpen ? null : clicked;

        if (_activePrimeButton != null)
        {
            _activePrimeButton.Tag = true;
            _panelByButton[_activePrimeButton].Visibility = Visibility.Visible;
        }
    }
}
