namespace VoicePress;

// The right-click dashboard: one window with a numbered tab strip (1-10)
// across the top — click a tab to switch which word's card is showing below
// it. Each card is a title (the word) over a bullet list of attributes: Key
// (click to open a menu of every key on an on-screen keyboard), Repeat, Hold,
// a duration readout with +1/+0.1/reset controls, an Infinite checkbox
// (checking it makes the word toggle the hold/repeat on and off — say it once
// to start, say it again to stop — instead of running for the fixed
// duration), and last, a button that resets the whole card.
public sealed class DashboardForm : Form
{
    private static readonly Color BackgroundColor = Color.Black;
    private static readonly Color ButtonColor = Color.FromArgb(18, 18, 18);
    private static readonly Color AccentColor = Color.FromArgb(230, 70, 30);
    private static readonly Color HoverColor = Color.FromArgb(60, 20, 12);

    private const int TabStripHeight = 60;
    private const int OuterPadding = 6;
    private const int PressTagWidth = 100;
    private const int CardWidth = 370;
    private const int BaseCardHeight = 324;
    private const int ItemHeight = BaseCardHeight / 4;
    private const int AccordionHeight = 56;

    // How far the visible top of the window (the Press tag / tab row) sits
    // below this window's own top edge, because of the outer padding —
    // OverlayForm uses this to line the dashboard's visible top up with the
    // overlay icon's, instead of the invisible window edge.
    public const int TopInset = OuterPadding;

    private readonly Dictionary<string, Control> _cards = new();
    private readonly Dictionary<string, Button> _tabButtons = new();

    // How many extra pixels of accordion content (Key's expanded category
    // rows, Repeat/Hold's timing row) the given word's card currently has
    // open — both can be open at once, so this is a running total, not
    // just on/off.
    private readonly Dictionary<string, int> _extraHeight = new();
    private string? _selectedWord;

    // The currently-open category key-list popup (from any card's Key
    // accordion) — shared across cards since only one card is visible, and
    // only one popup should ever be open, at a time.
    private Form? _openCategoryPopup;

    // Each card's own "reset everything about this card" logic, so the
    // triple-tap "Reset All" easter egg can run every card's reset at once —
    // including ones that aren't currently visible.
    private readonly Dictionary<string, Action> _resetCardActions = new();

    static DashboardForm()
    {
        // Applies to every menu the dashboard pops up (including submenus),
        // so the key-picker dropdowns match the black-and-red theme too.
        ToolStripManager.Renderer = new ToolStripProfessionalRenderer(new DarkRedColorTable());
    }

    // Windows erases a control's background to the default (white) just
    // before repainting it — normally invisible because it happens between
    // frames, but resizing this irregularly-shaped (Region-set) window during
    // an accordion expand/collapse makes that blank frame visible as a flash.
    // Telling Windows "already handled" skips that erase; our own painting
    // (solid BackColor + the card borders) covers the same area anyway.
    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x14;
        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    public DashboardForm()
    {
        // No caption bar or close button — this is a lightweight popup you
        // dismiss by right-clicking the overlay icon again, not a normal window.
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = BackgroundColor;
        DoubleBuffered = true; // cuts down on the flash when the key menu closes

        ClientSize = new Size(
            PressTagWidth + CardWidth + OuterPadding * 2,
            TabStripHeight + BaseCardHeight + OuterPadding * 2);

        // A 2x2 grid: "Press" sits over an empty bottom-left cell, tabs sit
        // over the card content on the right. The window's Region (set
        // below) then cuts away that empty bottom-left cell entirely, so it
        // reads as an L-shaped flag — "Press" sticking out top-left — rather
        // than a solid rectangle with a blank patch under the tag.
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(OuterPadding),
            BackColor = BackgroundColor,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PressTagWidth));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, TabStripHeight));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pressLabel = new Label
        {
            Text = "Press",
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = ButtonColor,
            ForeColor = AccentColor,
            Font = new Font("Segoe UI", 12f),
        };

        var words = KeyMap.RemappableWords;
        var tabStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = words.Length,
            RowCount = 1,
            BackColor = BackgroundColor,
        };
        for (int i = 0; i < words.Length; i++)
            tabStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / words.Length));

        // All ten cards live in the same spot, stacked on top of each other —
        // only the selected one is visible at a time.
        var contentArea = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };

        for (int i = 0; i < words.Length; i++)
        {
            var word = words[i];

            var tabButton = MakeTinyButton((i + 1).ToString());
            tabButton.Margin = new Padding(0);
            tabButton.Padding = new Padding(0);
            tabButton.Font = new Font("Segoe UI", 9f);
            tabButton.Click += (_, _) => SelectTab(word);
            tabStrip.Controls.Add(tabButton, i, 0);
            _tabButtons[word] = tabButton;

            var card = MakeCard(word);
            card.Visible = false;
            contentArea.Controls.Add(card);
            _cards[word] = card;
        }

        outer.Controls.Add(pressLabel, 0, 0);
        outer.Controls.Add(tabStrip, 1, 0);
        outer.Controls.Add(contentArea, 1, 1);
        Controls.Add(outer);

        RecomputeRegion();
        SelectTab(words[0]);
    }

    // Cut the empty bottom-left cell out of the window's shape entirely, so
    // nothing renders there — it's not just blank/black, it's gone. Called
    // again whenever the window's height changes (expanding/collapsing an
    // accordion), since the shape depends on ClientSize.
    private void RecomputeRegion()
    {
        var shape = new Region(new Rectangle(Point.Empty, ClientSize));
        shape.Exclude(new Rectangle(
            0,
            TabStripHeight + OuterPadding,
            PressTagWidth + OuterPadding,
            ClientSize.Height - TabStripHeight - OuterPadding));
        Region = shape;
    }

    private void SelectTab(string word)
    {
        _openCategoryPopup?.Close();
        _openCategoryPopup = null;

        foreach (var (w, card) in _cards)
            card.Visible = w == word;
        foreach (var (w, button) in _tabButtons)
            SetToggleAppearance(button, w == word);

        _selectedWord = word;
        AdjustHeight();
    }

    // Grows/shrinks the window to fit however many accordion rows the
    // selected card currently has open — so Key/Repeat/Hold/Reset always stay
    // the same size, and each accordion adds height rather than taking it
    // from them.
    private void AdjustHeight()
    {
        int extraHeight = _selectedWord != null && _extraHeight.TryGetValue(_selectedWord, out var value)
            ? value
            : 0;

        int cardHeight = BaseCardHeight + extraHeight;
        var newSize = new Size(ClientSize.Width, TabStripHeight + cardHeight + OuterPadding * 2);
        if (ClientSize == newSize)
            return;

        SuspendLayout();
        ClientSize = newSize;
        RecomputeRegion();
        ResumeLayout(true);

        // Without this, resizing can leave a stray leftover fragment of the
        // card's red border (drawn at its old height) visible in the newly
        // exposed area — a full repaint clears it. Update() forces that
        // repaint to happen immediately instead of waiting for the next
        // message-loop pass, which narrows the window where a blank/white
        // frame could show through during a bigger resize (e.g. Reset
        // collapsing several accordions worth of height at once).
        Invalidate(true);
        Update();
    }

    private Control MakeCard(string word)
    {
        var behavior = KeyMap.Behaviors[word];
        double duration = behavior.DurationSeconds;

        // The card: one red-bordered frame around everything (key, checkboxes,
        // duration, infinite) so it reads as a single unit, not a bordered key
        // button sitting above loose, unframed controls.
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = BackgroundColor,
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(AccentColor);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        // The list itself: Key, Repeat, Hold, Reset, each getting an equal
        // share of the card. When Repeat or Hold is on, a timing row is
        // inserted right after it (see RebuildList below) — an accordion:
        // it's the same width as every other row (the list is one column),
        // and everything after it shifts down to make room, all within the
        // same fixed card height.
        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = BackgroundColor,
        };

        // 1. Key — click to expand an accordion of key categories (Letters,
        // Numbers, Function Keys, ...) as full-width rows stacked vertically
        // underneath, exactly like Repeat/Hold's own rows; click Key again to
        // collapse it. Clicking a category pops out a list of that
        // category's individual keys to the left of the card.
        var keyButton = MakeListButton(KeyLabelFor(word));
        bool keyExpanded = false;

        // Only one category's key list should be open at a time. This uses a
        // plain borderless window rather than a ContextMenuStrip — a native
        // menu puts Windows into a special "menu tracking" mode the instant
        // it opens, which blocks MouseEnter from reaching sibling controls
        // (the other category buttons) until it closes. A regular window
        // doesn't have that restriction, so hovering across categories works.
        var categoryButtons = new List<Control>();
        foreach (var (category, keys) in KeyCatalog.Groups)
        {
            var categoryButton = MakeListButton(category);
            categoryButton.MouseEnter += (_, _) =>
            {
                _openCategoryPopup?.Close();
                _openCategoryPopup = ShowCategoryKeys(categoryButton, word, keys, keyButton);
            };
            categoryButtons.Add(categoryButton);
        }

        // 2. Repeat — a toggle button: click to turn on/off, lit up when on.
        // Turning it on also opens the timing dropdown (see below); turning
        // it off closes it again.
        bool repeatOn = behavior.Repeat;
        var repeatButton = MakeListButton("Repeat");
        SetToggleAppearance(repeatButton, repeatOn);

        // 3. Hold — same toggle-button treatment, same shared dropdown.
        bool holdOn = behavior.Hold;
        var holdButton = MakeListButton("Hold");
        SetToggleAppearance(holdButton, holdOn);

        // 4. Reset — resets everything about this card: the assigned key,
        // Repeat/Hold, and the duration. Tap it three times quickly for the
        // "Reset All" easter egg: an accordion row (see RebuildList) with a
        // button that resets every card, not just this one.
        var resetCardButton = MakeListButton("Reset");
        bool resetAllExpanded = false;
        var resetAllButton = MakeListButton("Reset All");

        // The timing row: +1 / +0.1 / reset / Infinite, plus the running
        // total. It's inserted right after whichever of Repeat/Hold is on
        // (see RebuildList), and applies regardless of which of the two that
        // is, since the duration and Infinite settings aren't specific to
        // one mode.
        var durationLabel = new Label
        {
            Text = FormatDuration(duration),
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = AccentColor,
            BackColor = ButtonColor,
            Font = new Font("Segoe UI", 12f),
        };
        var plusOne = MakeTinyButton("1");
        var plusTenth = MakeTinyButton(".1");
        var resetDurationButton = MakeTinyButton("↻");
        resetDurationButton.Font = new Font("Segoe UI", 14f);
        resetDurationButton.Padding = new Padding(0, 0, 0, 9);
        bool infiniteOn = behavior.Infinite;
        var infiniteButton = MakeTinyButton("∞");
        infiniteButton.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
        SetToggleAppearance(infiniteButton, infiniteOn);

        var timingPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            ColumnCount = 5,
            RowCount = 1,
            BackColor = ButtonColor,
        };
        timingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        timingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        timingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        timingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        timingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        timingPanel.Controls.Add(plusOne, 0, 0);
        timingPanel.Controls.Add(plusTenth, 1, 0);
        timingPanel.Controls.Add(resetDurationButton, 2, 0);
        timingPanel.Controls.Add(infiniteButton, 3, 0);
        timingPanel.Controls.Add(durationLabel, 4, 0);

        // Rebuilds which rows the list has and in what order: Key's category
        // row and the timing row each get inserted right after the button
        // that opened them (both are accordions — everything below shifts
        // down — and both can be open at once). Key/Repeat/Hold/Reset always
        // use the same fixed height (ItemHeight) regardless — the accordions
        // add height (via AdjustHeight, which grows the window) rather than
        // shrinking them to fit.
        void RebuildList()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            list.RowStyles.Clear();

            var rows = new List<Control> { keyButton };
            if (keyExpanded)
                rows.AddRange(categoryButtons);
            rows.Add(repeatButton);
            if (repeatOn)
                rows.Add(timingPanel);
            rows.Add(holdButton);
            if (holdOn)
                rows.Add(timingPanel);
            rows.Add(resetCardButton);
            if (resetAllExpanded)
                rows.Add(resetAllButton);

            list.RowCount = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                int height = rows[i] == timingPanel ? AccordionHeight : ItemHeight;
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                list.Controls.Add(rows[i], 0, i);
            }
            list.ResumeLayout(true);
            ActiveControl = null;

            int extraHeight = (keyExpanded ? categoryButtons.Count * ItemHeight : 0)
                + (repeatOn || holdOn ? AccordionHeight : 0)
                + (resetAllExpanded ? ItemHeight : 0);
            _extraHeight[word] = extraHeight;
            if (word == _selectedWord)
                AdjustHeight();
        }

        keyButton.Click += (_, _) =>
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;
            keyExpanded = !keyExpanded;
            SetToggleAppearance(keyButton, keyExpanded);
            RebuildList();
        };

        void SaveBehavior() => KeyMap.SetBehavior(word, repeatOn, holdOn, duration, infiniteOn);

        // Repeat and Hold are mutually exclusive — tapping the key repeatedly
        // and holding it down don't mean anything combined, so turning one on
        // turns the other off.
        repeatButton.Click += (_, _) =>
        {
            repeatOn = !repeatOn;
            if (repeatOn)
            {
                holdOn = false;
                SetToggleAppearance(holdButton, false);
            }
            SetToggleAppearance(repeatButton, repeatOn);
            SaveBehavior();
            RebuildList();
        };
        holdButton.Click += (_, _) =>
        {
            holdOn = !holdOn;
            if (holdOn)
            {
                repeatOn = false;
                SetToggleAppearance(repeatButton, false);
            }
            SetToggleAppearance(holdButton, holdOn);
            SaveBehavior();
            RebuildList();
        };
        infiniteButton.Click += (_, _) =>
        {
            infiniteOn = !infiniteOn;
            SetToggleAppearance(infiniteButton, infiniteOn);
            // Infinite ignores the duration entirely, so turning it on clears
            // whatever timer value was set — it'd otherwise look like a
            // leftover duration that doesn't actually do anything anymore.
            if (infiniteOn)
            {
                duration = 0.0;
                durationLabel.Text = FormatDuration(duration);
            }
            SaveBehavior();
        };
        plusOne.Click += (_, _) =>
        {
            duration = Math.Round(duration + 1.0, 1);
            durationLabel.Text = FormatDuration(duration);
            SaveBehavior();
        };
        plusTenth.Click += (_, _) =>
        {
            duration = Math.Round(duration + 0.1, 1);
            durationLabel.Text = FormatDuration(duration);
            SaveBehavior();
        };
        resetDurationButton.Click += (_, _) =>
        {
            duration = 0.0;
            durationLabel.Text = FormatDuration(duration);
            SaveBehavior();
        };
        void ResetCard()
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;
            KeyMap.ResetToDefault(word);
            keyButton.Text = KeyLabelFor(word);
            duration = 0.0;
            durationLabel.Text = FormatDuration(duration);
            repeatOn = false;
            holdOn = false;
            infiniteOn = false;
            keyExpanded = false;
            resetAllExpanded = false;
            SetToggleAppearance(repeatButton, false);
            SetToggleAppearance(holdButton, false);
            SetToggleAppearance(infiniteButton, false);
            SetToggleAppearance(keyButton, false);
            SetToggleAppearance(resetCardButton, false);
            RebuildList();
        }
        _resetCardActions[word] = ResetCard;

        resetAllButton.Click += (_, _) =>
        {
            foreach (var reset in _resetCardActions.Values)
                reset();
        };

        // Easter egg: tap Reset three times quickly to expand a "Reset All"
        // row underneath — an accordion, same as Key/Repeat/Hold — with a
        // button that resets every card, not just this one.
        int resetTapCount = 0;
        var resetTapTimer = new System.Windows.Forms.Timer { Interval = 600 };
        resetTapTimer.Tick += (_, _) =>
        {
            resetTapCount = 0;
            resetTapTimer.Stop();
        };
        resetCardButton.Click += (_, _) =>
        {
            resetTapCount++;
            resetTapTimer.Stop();
            resetTapTimer.Start();

            // Lighting the button up right away, before the resize that
            // RebuildList triggers, seems to be what was masking the flash
            // for Key — doing the same here for the same reason.
            SetToggleAppearance(resetCardButton, true);
            ResetCard();

            if (resetTapCount >= 3)
            {
                resetTapCount = 0;
                resetTapTimer.Stop();
                resetAllExpanded = true;
                SetToggleAppearance(resetCardButton, true);
                RebuildList();
            }
        };

        RebuildList();

        card.Controls.Add(list);

        return card;
    }

    private Button MakeTinyButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            FlatStyle = FlatStyle.Flat,
            BackColor = ButtonColor,
            ForeColor = AccentColor,
            Font = new Font("Segoe UI", 12f),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = HoverColor;
        ClearFocusAfterClick(button);
        return button;
    }

    // The standard look for a full-width item in a card's list (Key, Repeat,
    // Hold, Infinite, Reset All) — so all six list items look the same, as
    // opposed to some being buttons and some being checkboxes.
    private Button MakeListButton(string text)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            FlatStyle = FlatStyle.Flat,
            BackColor = ButtonColor,
            ForeColor = AccentColor,
            Font = new Font("Segoe UI", 12f),
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = HoverColor;
        ClearFocusAfterClick(button);
        return button;
    }

    // Windows draws a focus rectangle around a button once it's been
    // clicked — on our flat, borderless buttons that shows up as a stray
    // accent-colored outline (e.g. around "Numbers" after clicking it, or
    // around a category's first key). Moving focus off the button right
    // after the click hides it, since only the currently-focused control
    // gets that outline.
    private static void ClearFocusAfterClick(Button button)
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
    private void SetToggleAppearance(Button button, bool on)
    {
        button.BackColor = on ? AccentColor : ButtonColor;
        button.ForeColor = on ? BackgroundColor : AccentColor;
        button.FlatAppearance.MouseOverBackColor = on ? AccentColor : HoverColor;
        button.FlatAppearance.MouseDownBackColor = on ? AccentColor : HoverColor;
    }

    // Invariant culture so this always reads "0.0s" — without it, Windows
    // regions that use a comma for decimals (as this machine apparently does)
    // would render it as "0,0s".
    private static string FormatDuration(double seconds) =>
        seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";

    private static string KeyLabelFor(string word) => $"Key: {KeyCatalog.DisplayNameFor(KeyMap.Words[word])}";

    // A list of one category's individual keys — opened by hovering a
    // category button in Key's accordion. Pops out to the left of the card,
    // at the same width as the card, matching the Key/Repeat rows' styling.
    // A plain window rather than a menu, so hovering across categories keeps
    // working (see the comment where this is called) — and it never takes
    // window activation away from the dashboard (see NonActivatingForm),
    // so clicking Key to collapse everything works on the first click
    // instead of the first click just re-activating the dashboard.
    //
    // No scrollbar even for the longest category (Letters, 26 keys) — this
    // just sizes the popup tall enough to show every key at once, since a
    // themed scrollbar isn't worth building for one edge case and the
    // built-in one clashes badly with the black/red look.
    private Form ShowCategoryKeys(Control anchor, string word, KeyCatalog.Entry[] keys, Control keyButton)
    {
        const int rowHeight = 34;

        var popup = new NonActivatingForm
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = ButtonColor,
            ClientSize = new Size(CardWidth, keys.Length * rowHeight),
        };

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = keys.Length,
            BackColor = ButtonColor,
        };
        for (int i = 0; i < keys.Length; i++)
        {
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));

            var entry = keys[i];
            var item = MakeListButton(entry.DisplayName);
            item.Font = new Font("Segoe UI", 10f);
            item.Click += (_, _) =>
            {
                KeyMap.Rebind(word, entry.VkCode);
                keyButton.Text = KeyLabelFor(word);
                popup.Close();
            };
            list.Controls.Add(item, 0, i);
        }

        popup.Controls.Add(list);

        const int gapFromCard = 6;
        var anchorScreenPoint = anchor.PointToScreen(Point.Empty);
        popup.Location = new Point(anchorScreenPoint.X - popup.Width - gapFromCard, anchorScreenPoint.Y);
        popup.Show(this);
        popup.ActiveControl = null; // otherwise the first key shows a focus outline immediately
        return popup;
    }

    // A borderless popup that never takes window activation, so it can't
    // steal focus away from the dashboard the way a normal Form would.
    private sealed class NonActivatingForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;
    }

    // Recolors menu hover/selection/border chrome to match the black-and-red
    // theme (the BackColor/ForeColor set on the menu items only covers the
    // text and idle background, not the built-in blue hover highlight).
    private sealed class DarkRedColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => HoverColor;
        public override Color MenuItemSelectedGradientBegin => HoverColor;
        public override Color MenuItemSelectedGradientEnd => HoverColor;
        public override Color MenuItemBorder => HoverColor;
        public override Color MenuBorder => ButtonColor;
        public override Color ToolStripBorder => ButtonColor;
        public override Color ImageMarginGradientBegin => ButtonColor;
        public override Color ImageMarginGradientMiddle => ButtonColor;
        public override Color ImageMarginGradientEnd => ButtonColor;
        public override Color SeparatorDark => AccentColor;
        public override Color SeparatorLight => AccentColor;
    }
}
