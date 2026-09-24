using System.Windows.Controls;

namespace UnboundKeys.Wpf;

public partial class VoiceDiagram
{
    public VoiceDiagram()
    {
        InitializeComponent();
    }

    // What the drawn key says — the hovered row's mapping.
    public void SetKey(string text) => KeyLabel.Text = text;

    // Lit while a row is hovered: the waves come up to full strength and
    // the key's label takes the accent, as if it's being pressed right now.
    public void SetSpeaking(bool speaking)
    {
        Waves.Opacity = speaking ? 1.0 : 0.45;
        KeyLabel.SetResourceReference(TextBlock.ForegroundProperty, speaking ? "AccentBrush" : "TextPrimaryBrush");
    }
}
