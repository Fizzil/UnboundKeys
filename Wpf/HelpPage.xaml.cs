using System.Windows;
using System.Windows.Controls;
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

    public HelpPage()
    {
        InitializeComponent();

        AddHeader("VOICE", topMargin: 4);
        AddLine("\"press\" and a number", "is all it listens for, one to ten. Ordinary talk is ignored, and nothing leaves your PC.");
        AddLine($"\"press {VoiceEngine.StopWord}\"", "releases every key being held or repeated.");
        AddLine($"\"press {VoiceEngine.MenuWord}\"", "brings this dashboard back.");
        AddLine($"\"press {VoiceEngine.FadeWord}\"", "turns Fade on or off.");
        AddLine("Listening switch", "pauses the voice keys only. Mouse buttons and both keyboards keep working.");

        AddHeader("MODES", topMargin: 16);
        AddLine("Tap", "presses the keys once.");
        AddLine("Repeat", "taps them again and again for the duration. With several keys it cycles through them one at a time.");
        AddLine("Hold", "keeps them pressed for the duration. With several keys it holds them all together, as a combo.");
        AddLine("Infinite", "runs until you say the word or press the button again.");
        AddLine("Say or press it again", "to stop a Repeat or Hold early, Infinite or not.");

        AddHeader("SAFETY NETS", topMargin: 16);
        AddLine($"\"press {VoiceEngine.StopWord}\"", "lets go of everything, whatever is mapped.");
        AddLine("Caps, twice quickly", "on the on-screen keyboard releases everything and lifts Fade. A real Caps Lock key works the same way.");
        AddLine("Switching windows", "releases everything too, so alt-tab or a new browser tab never leaves a key stuck.");
        AddLine("Left Click", "is never remapped, so you can always click.");

        AddHeader("ON-SCREEN KEYBOARD", topMargin: 16);
        AddLine("Shift, Ctrl, Alt, Win", "stick: click one, then the key to combine it with.");
        AddLine("Hold a key", "to repeat it.");
        AddLine("Word suggestions", "finish the word when clicked, and learn the words you type.");
        AddLine("Menu, Fade, Mini, Maxi", "bring this dashboard back, dim it, collapse it to a strip, bring it back. Drag it by the grip on its right edge.");
        AddLine("Remapping a key", "happens on the Keyboard page. A remapped key is caught on a real keyboard too.");

        AddHeader("GOOD TO KNOW", topMargin: 16);
        AddLine("Closing this window", "hides it. UnboundKeys keeps running in the tray by the clock; click the icon there, press Menu on the on-screen keyboard, or say \"press menu\". Quit is in Settings.");
        AddLine("The permission prompt", "appears because UnboundKeys runs as administrator, so its key presses reach games that run elevated.");
        AddLine("Profiles", "are complete sets of mappings, each with its own theme. Switch from the chip at the bottom left.");
        AddLine("Everything is saved", @"in %AppData%\UnboundKeys. The full manual is in the GitHub repository.");
    }

    // Nothing on this page changes underneath it.
    public void Refresh() { }

    private void AddHeader(string text, double topMargin)
    {
        var header = new TextBlock { Text = text, Margin = new Thickness(0, topMargin, 0, 4) };
        header.SetResourceReference(StyleProperty, "SectionHeaderStyle");
        Sections.Children.Add(header);
    }

    private void AddLine(string lead, string explanation)
    {
        var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 3, 12, 3) };
        var leadRun = new Run(lead);
        leadRun.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        line.Inlines.Add(leadRun);
        line.Inlines.Add(new Run("  " + explanation));
        line.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        Sections.Children.Add(line);
    }
}
