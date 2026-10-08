using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using System.Windows.Documents;

namespace UnboundKeys.Wpf;

// A one-page digest of MANUAL.md for reading inside the app: each line is
// a lead in the primary text color followed by its explanation in grey,
// the same shape as the Voice page's "always available" lines. Nothing
// here is interactive — it's the page you open when you can't remember
// which words work or how to let go of a stuck key.
public partial class HelpPage : IDashboardPage
{
    public string Title => "Help";

    // The section the next AddLine goes into (see AddHeader).
    private StackPanel? _body;

    public HelpPage()
    {
        InitializeComponent();

        AddHeader("VOICE", topMargin: 0);
        AddLine("\"press\" and a number", "is all it listens for, one to ten. Ordinary talk is ignored, and nothing leaves your PC.");
        AddLine($"\"press {VoiceEngine.StopWord}\"", "releases every key being held or repeated.");
        AddLine($"\"press {VoiceEngine.MenuWord}\"", "shows this dashboard, or hides it again.");
        AddLine($"\"press {VoiceEngine.FadeWord}\"", "turns Fade on or off.");
        AddLine("Listening switch", "pauses the voice keys only. Mouse buttons and both keyboards keep working.");
        AddLine("Heard, on the Voice page", "shows what the microphone just heard, and marks the command it sent.");

        AddHeader("MOUSE BUTTONS", topMargin: 6);
        AddLine("Three things per button", "at the top of a button's editor: Single press, Double press and Long press, each a mapping of its own. Each way of pressing starts its mapping and stops it again, and starting one stops the others. The wheel has the first two.");
        AddLine("Timing", "with a double press set, a single press lands a moment later, once no second press is coming; with a long press set, a single press lands when you let go, and half a second held is a long press.");

        AddHeader("REMAPPING MODES", topMargin: 6);
        AddLine("Tap", "presses all the keys together, once. Right for shortcuts like Ctrl + X.");
        AddLine("Repeat", "presses the keys one at a time, ten times a second: Key 1, Key 2 and so on, then back to Key 1, for the duration. Never together, so use Tap for shortcuts.");
        AddLine("Rotation", "about ten times a second, presses Key 1, Key 2, Key 3 and so on in a quick row. The game uses the first one that's ready and ignores the rest, so your most important ability goes on Key 1: a one-button rotation with no cooldown numbers to enter.");
        AddLine("Hold", "holds all the keys down together for the duration, then lets go. Repeats while held, like a key held on a real keyboard, so a held Backspace deletes a run.");
        AddLine("Infinite", "keeps pressing the keys repeatedly, or holding them, until you say the word or press the button again.");
        AddLine("Say or press it again", "to stop a Repeat or Hold early, Infinite or not.");
        AddLine("Infinite pause", "in the editor, under Infinite: pauses every infinite repeat for the time you set (1.0 s to start) while this key fires, then they resume. Then set the key's Mode to Repeat or Hold for the same time, so it actually presses the key during the pause: Repeat for an instant ability, Hold for a cast or a channel.");

        AddLine("The small signs beside a mapping", "say how it is pressed: R for Repeat, a circling arrow for Rotation, H for Hold, ∞ for Infinite, P for Infinite pause. A plain Tap shows none. Hover one for its name; on the Keyboard page, hover a lit key.");

        AddHeader("SAFETY NETS", topMargin: 6);
        AddLine("Stop, on the rail", "lets go of everything, whatever is mapped, with one click.");
        AddLine($"\"press {VoiceEngine.StopWord}\"", "does the same by voice.");
        AddLine("Caps, twice quickly", "on the on-screen keyboard, or a real Caps Lock key, releases everything, lifts Fade, and shows or hides this dashboard.");
        AddLine("Switching windows", "releases everything too, so alt-tab or a new browser tab never leaves a key stuck.");
        AddLine("Left Click", "is never remapped, so you can always click.");

        AddHeader("ON-SCREEN KEYBOARD", topMargin: 6);
        AddLine("Shift, Ctrl, Alt, Win", "stick: click one, then the key to combine it with.");
        AddLine("Hold a key", "to repeat it.");
        AddLine("Word suggestions", "finish the word when clicked. With \"Smart predictive text\" on (Keyboard page, off by default) your own words come first; \"Open the list\" lets you read and edit them, \"Clear the list\" forgets them.");
        AddLine("Menu, Fade, Mini, Maxi", "show or hide this dashboard, dim it, collapse it to a strip, bring it back. Drag it by the grip on its right edge.");
        AddLine("Remapping a key", "happens on the Keyboard page. A remapped key is caught on a real keyboard too.");

        AddHeader("GOOD TO KNOW", topMargin: 6);
        AddLine("Closing this window", "hides it. UnboundKeys keeps running in the tray by the clock; click the icon there, press Menu on the on-screen keyboard, double-tap Caps Lock on a real keyboard, or say \"press menu\". Quit is at the bottom of the rail, two clicks to confirm (and in Settings).");
        AddLine("The permission prompt", "appears because UnboundKeys runs as administrator, so its key presses reach games that run elevated. Start with Windows, in Settings, starts it that way at sign-in with no prompt.");
        AddLine("Profiles", "are games, each with a theme and up to ten sub-profiles for classes or loadouts. Switch either from the chip at the bottom left. Rename one in Settings with the on-screen name keyboard: hold a key to repeat it, click Shift for a capital.");
        AddLine("Reset, in Settings", "has two buttons, each asking \"Are you sure?\" first. Reset profile puts the active profile's mappings back to default. Reset app deletes everything the app keeps and starts it again as new; that cannot be undone.");
        AddLine("Action logger", "in Settings, off by default, is used for debugging and for building new features. While it is on, the app notes its starts, errors and the changes you make here; while it is off, nothing is written.");
        AddLine("Updates", "are checked only when you click Check for updates in Settings, and can be installed from there. A shortcut you made to the app is pointed at the newest version each time it starts.");
        // In bold (Fizzil): what the app keeps on the PC should be plain to see.
        AddLine("Everything is saved in", @"%AppData%\UnboundKeys: your profiles and mappings in settings.json (with a backup beside it), the words kept by ""Smart predictive text"" in learned-words.txt if you switch that on, and the action logger's notes in log.txt if you switch that on. The full manual is in the GitHub repository.", bold: true);
    }

    // Nothing on this page changes underneath it.
    public void Refresh() { }

    // Each section folds under its heading, chevron at the far right, the
    // same row as Settings uses (Fizzil); closed until clicked.
    private void AddHeader(string text, double topMargin)
    {
        var body = new StackPanel();
        Sections.Children.Add(new FoldSection { Title = text, Content = body, Margin = new Thickness(0, topMargin, 0, 0) });
        _body = body;
    }

    private void AddLine(string lead, string explanation, bool bold = false)
    {
        var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 3, 12, 3) };
        var leadRun = new Run(lead);
        if (bold)
            leadRun.FontWeight = FontWeights.Bold;
        leadRun.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        line.Inlines.Add(leadRun);
        line.Inlines.Add(new Run("  " + explanation));
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _body?.Children.Add(line);
    }
}
