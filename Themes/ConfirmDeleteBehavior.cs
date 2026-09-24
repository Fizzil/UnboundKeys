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
internal static class ConfirmDeleteBehavior
{
    public static void AttachTo(Button button, Action onConfirmed)
    {
        bool armed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            armed = false;
            timer.Stop();
            button.Tag = false;
        };
        button.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                button.Tag = true;
                timer.Stop();
                timer.Start();
                return;
            }

            timer.Stop();
            onConfirmed();
        };
    }
}
