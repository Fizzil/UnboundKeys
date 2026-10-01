using System;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Themes;

// WPF port of Theme.cs's MakeConfirmDeleteButton (WinForms) — tap once to
// arm (the button's style shows it via Tag — RemoveButtonStyle turns
// into "Remove?", ToggleFillButtonStyle fills accent), tap again within
// 4 seconds to actually do the destructive thing; otherwise it disarms
// itself. Four seconds, not the WinForms two: enough to read the armed
// label and decide, without being a race. A plain static helper rather
// than an attached property like HoverFadeBehavior, since this needs a
// per-button Click handler and timer wired up explicitly.
//
// armedLabel, when given, is what the button says while armed ("Are you
// sure?", Fizzil's wording, as on the rail's Quit); its own label comes
// back when it disarms.
internal static class ConfirmDeleteBehavior
{
    // A double click is one decision, not two: the confirming click must
    // come at least this long after the arming one, or it is ignored. With
    // Reset app behind one of these, a slip of the mouse must not be a yes.
    private const int MinConfirmDelayMs = 400;

    public static void AttachTo(Button button, Action onConfirmed, string? armedLabel = null)
    {
        bool armed = false;
        object? restLabel = null;
        DateTime armedAt = DateTime.MinValue;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };

        void Disarm()
        {
            timer.Stop();
            armed = false;
            button.Tag = false;
            if (armedLabel != null && restLabel != null)
                button.Content = restLabel;
        }

        timer.Tick += (_, _) => Disarm();
        button.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                armedAt = DateTime.UtcNow;
                if (armedLabel != null)
                {
                    restLabel = button.Content;
                    button.Content = armedLabel;
                }
                button.Tag = true;
                timer.Stop();
                timer.Start();
                return;
            }

            if ((DateTime.UtcNow - armedAt).TotalMilliseconds < MinConfirmDelayMs)
                return;

            // Disarmed again before acting: a button that outlives its own
            // action (Reset profile, Clear the list) would otherwise stay
            // armed and do it again on a single click.
            Disarm();
            onConfirmed();
        };
    }
}
