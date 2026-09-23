using System;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace UnboundKeys.Themes;

// WPF port of Theme.cs's MakeConfirmDeleteButton (WinForms) — tap once to
// arm (lights up via ToggleFillButtonStyle's Tag), tap again within 2
// seconds to actually do the destructive thing; otherwise it disarms
// itself. A plain static helper rather than an attached property like
// HoverFadeBehavior, since this needs a per-button Click handler and
// timer wired up explicitly, not just a Loaded-time template lookup.
internal static class ConfirmDeleteBehavior
{
    public static void AttachTo(Button button, Action onConfirmed)
    {
        bool armed = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
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
