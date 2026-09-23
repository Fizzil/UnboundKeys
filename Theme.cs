namespace UnboundKeys;

// The dashboard's black/red visual style. Colors live in one swappable
// object (Theme.Current) rather than as scattered constants — swapping
// the whole palette (see the named presets and SetByName below) means
// assigning a new ThemeColors here instead of hunting through every file
// that uses a color.
internal sealed record ThemeColors(Color Background, Color Button, Color Accent, Color Hover);

// The shared button styling and small formatting helpers every card/tab
// uses, so none of them need to duplicate this or depend on DashboardForm
// for it.
internal static class Theme
{
    // Background/Button stay identical across every preset below —
    // switching color only ever changes Accent/Hover, so the black
    // dashboard backdrop never shifts, just the one color it's built
    // around. Hover is each Accent scaled down to roughly a quarter
    // brightness, matching Red's own original ratio.
    public static readonly ThemeColors Red = new(
        Background: Color.Black,
        Button: Color.FromArgb(18, 18, 18),
        Accent: Color.FromArgb(230, 70, 30),
        Hover: Color.FromArgb(60, 20, 12));

    public static readonly ThemeColors Green = new(
        Background: Color.Black,
        Button: Color.FromArgb(18, 18, 18),
        Accent: Color.FromArgb(60, 200, 90),
        Hover: Color.FromArgb(15, 55, 25));

    public static readonly ThemeColors Blue = new(
        Background: Color.Black,
        Button: Color.FromArgb(18, 18, 18),
        Accent: Color.FromArgb(60, 140, 230),
        Hover: Color.FromArgb(15, 40, 70));

    public static ThemeColors Current { get; set; } = Red;

    // Looked up by name (see ThemeMode) rather than switched on directly
    // at each call site, so a new color option only ever means adding one
    // more case here.
    public static ThemeColors ByName(string name) => name switch
    {
        "Green" => Green,
        "Blue" => Blue,
        _ => Red,
    };

    public static Button MakeTinyButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            FlatStyle = FlatStyle.Flat,
            BackColor = Current.Button,
            ForeColor = Current.Accent,
            Font = new Font("Segoe UI", 12f),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Current.Hover;
        // Without this, WinForms falls back to its own computed press color
        // for a flat button, which reads as a jarring white flash against
        // this dark theme — using the accent color instead makes a click
        // read as a brief orange flash, matching the rest of the theme.
        button.FlatAppearance.MouseDownBackColor = Current.Accent;
        ClearFocusAfterClick(button);
        return button;
    }

    // The standard look for a full-width item in a card's list (Key, Repeat,
    // Hold, Infinite, Reset All) — so all six list items look the same, as
    // opposed to some being buttons and some being checkboxes.
    public static Button MakeListButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            // Vertical-only: these stack in a single column, so left/right
            // margin only insets them from the card's edges (unwanted, since
            // the card should line up flush with the tab row's width) —
            // top/bottom still gives the thin gap between stacked buttons.
            Margin = new Padding(0, 1, 0, 1),
            FlatStyle = FlatStyle.Flat,
            BackColor = Current.Button,
            ForeColor = Current.Accent,
            Font = new Font("Segoe UI", 12f),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Current.Hover;
        button.FlatAppearance.MouseDownBackColor = Current.Accent;
        ClearFocusAfterClick(button);
        return button;
    }

    // Windows draws a focus rectangle around a button once it's been
    // clicked — on our flat, borderless buttons that shows up as a stray
    // accent-colored outline (e.g. around "Numbers" after clicking it, or
    // around a category's first key). Moving focus off the button right
    // after the click hides it, since only the currently-focused control
    // gets that outline.
    public static void ClearFocusAfterClick(Button button)
    {
        button.Click += (_, _) =>
        {
            var form = button.FindForm();
            if (form != null)
                form.ActiveControl = null;
        };
    }

    // Toggle buttons (Key, Repeat, Hold, Infinite) show their on/off state by
    // inverting their colors when on, since they don't have a checkbox glyph.
    // The hover color also has to change with it — otherwise resting the
    // mouse on an already-selected button shows the dim hover shade instead
    // of staying lit up, since FlatAppearance.MouseOverBackColor always wins
    // over BackColor while the cursor is over the button.
    public static void SetToggleAppearance(Button button, bool on)
    {
        button.BackColor = on ? Current.Accent : Current.Button;
        button.ForeColor = on ? Current.Background : Current.Accent;
        button.FlatAppearance.MouseOverBackColor = on ? Current.Accent : Current.Hover;
        button.FlatAppearance.MouseDownBackColor = on ? Current.Accent : Current.Hover;
    }

    // A thinner alternative for buttons you click through repeatedly — the
    // numbered/mouse-button sub-tabs — where a solid color fill on the
    // selected one read as too heavy. A 2px accent-colored line along the
    // bottom edge marks it instead; everything else about the button's
    // normal (unselected) look stays as-is. Call EnableTabUnderline once,
    // at creation, then SetTabSelected each time its state should change.
    private const int TabUnderlineHeight = 2;

    public static void EnableTabUnderline(Button button)
    {
        button.Paint += (_, e) =>
        {
            if (button.Tag is true)
            {
                using var brush = new SolidBrush(Current.Accent);
                e.Graphics.FillRectangle(brush, 0, button.Height - TabUnderlineHeight, button.Width, TabUnderlineHeight);
            }
        };
    }

    public static void SetTabSelected(Button button, bool selected)
    {
        button.Tag = selected;
        button.Invalidate();
    }

    // Invariant culture so this always reads "0.0s" — without it, Windows
    // regions that use a comma for decimals (as this machine apparently does)
    // would render it as "0,0s".
    public static string FormatDuration(double seconds) =>
        seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";

    // A "✕" button that requires two taps to actually do anything
    // destructive: tap once to arm (lights up), tap again within 2 seconds
    // to confirm — otherwise it disarms itself. Used anywhere removing
    // something (a profile, an extra key) can't easily be undone. Defaults
    // to the small square style (Profiles' delete "✕"); pass MakeListButton
    // for a full-width row instead (an extra key's own delete row).
    public static Button MakeConfirmDeleteButton(Action onConfirmed, Func<string, Button>? buttonFactory = null)
    {
        var button = (buttonFactory ?? MakeTinyButton)("✕");
        bool armed = false;
        var armTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        armTimer.Tick += (_, _) =>
        {
            armed = false;
            armTimer.Stop();
            SetToggleAppearance(button, false);
        };
        button.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                SetToggleAppearance(button, true);
                armTimer.Stop();
                armTimer.Start();
                return;
            }

            armTimer.Stop();
            armTimer.Dispose();
            onConfirmed();
        };
        return button;
    }
}
