namespace VoicePress;

// One word's card — Key (click to expand a list of key categories, hover a
// category to pop out its individual keys), Repeat, Hold, Reset (with the
// "Reset All" easter egg on a triple tap), and the shared +1/+0.1/reset/
// Infinite timing row that appears under whichever of Repeat/Hold is on.
internal sealed class WordCardTab : IDashboardTab
{
    private readonly string _word;

    public WordCardTab(string word) => _word = word;

    public string Id => _word;

    // The digit shown on the tab itself ("1".."10") — its position in the
    // fixed word list, not the spoken word ("one".."ten") used as Id.
    public string Label => (Array.IndexOf(KeyMap.RemappableWords, _word) + 1).ToString();

    private static string KeyLabelFor(string word) => $"Key: {KeyCatalog.DisplayNameFor(KeyMap.Words[word])}";

    public Control BuildContent(DashboardTabContext ctx)
    {
        var word = _word;
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
            BackColor = Theme.Current.Background,
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Accent);
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
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Current.Background,
        };

        // 1. Key — click to expand an accordion of key categories (Letters,
        // Numbers, Function Keys, ...) as full-width rows stacked vertically
        // underneath, exactly like Repeat/Hold's own rows; click Key again to
        // collapse it. Clicking a category pops out a list of that
        // category's individual keys to the left of the card.
        var keyButton = Theme.MakeListButton(KeyLabelFor(word));
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
            var categoryButton = Theme.MakeListButton(category);
            categoryButton.MouseEnter += (_, _) =>
            {
                ctx.ShowCategoryPopup(categoryButton, keys, entry =>
                {
                    KeyMap.Rebind(word, entry.VkCode);
                    keyButton.Text = KeyLabelFor(word);
                });
            };
            categoryButtons.Add(categoryButton);
        }

        // 2. Repeat — a toggle button: click to turn on/off, lit up when on.
        // Turning it on also opens the timing dropdown (see below); turning
        // it off closes it again.
        bool repeatOn = behavior.Repeat;
        var repeatButton = Theme.MakeListButton("Repeat");
        Theme.SetToggleAppearance(repeatButton, repeatOn);

        // 3. Hold — same toggle-button treatment, same shared dropdown.
        bool holdOn = behavior.Hold;
        var holdButton = Theme.MakeListButton("Hold");
        Theme.SetToggleAppearance(holdButton, holdOn);

        // 4. Reset — resets everything about this card: the assigned key,
        // Repeat/Hold, and the duration. Tap it three times quickly for the
        // "Reset All" easter egg: an accordion row (see RebuildList) with a
        // button that resets every card, not just this one.
        var resetCardButton = Theme.MakeListButton("Reset");
        bool resetAllExpanded = false;
        var resetAllButton = Theme.MakeListButton("Reset All");

        // The timing row: +1 / +0.1 / reset / Infinite, plus the running
        // total. It's inserted right after whichever of Repeat/Hold is on
        // (see RebuildList), and applies regardless of which of the two that
        // is, since the duration and Infinite settings aren't specific to
        // one mode.
        var durationLabel = new Label
        {
            Text = Theme.FormatDuration(duration),
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.Current.Accent,
            BackColor = Theme.Current.Button,
            Font = new Font("Segoe UI", 12f),
        };
        var plusOne = Theme.MakeTinyButton("1");
        var plusTenth = Theme.MakeTinyButton(".1");
        var resetDurationButton = Theme.MakeTinyButton("↻");
        resetDurationButton.Font = new Font("Segoe UI", 14f);
        resetDurationButton.Padding = new Padding(0, 0, 0, 9);
        bool infiniteOn = behavior.Infinite;
        var infiniteButton = Theme.MakeTinyButton("∞");
        infiniteButton.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
        Theme.SetToggleAppearance(infiniteButton, infiniteOn);

        var timingPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(1),
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Theme.Current.Button,
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
        // add height (via ReportHeight, which grows the window) rather than
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
                int height = rows[i] == timingPanel ? ctx.AccordionHeight : ctx.ItemHeight;
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                list.Controls.Add(rows[i], 0, i);

                // Every row has a 1px bottom margin as a spacer to whatever
                // comes after it — on the last row there's nothing after it,
                // so that margin just leaves a 1px sliver of the card's own
                // black background exposed beneath it, right at the card's
                // true bottom edge. Recomputed fresh every rebuild (rather
                // than only ever patching whichever row is last) since these
                // are the same reused controls across rebuilds — one that
                // used to be last but isn't anymore needs its normal 1px
                // margin back, not whatever it was last left at.
                var m = rows[i].Margin;
                rows[i].Margin = new Padding(m.Left, m.Top, m.Right, i == rows.Count - 1 ? 0 : 1);
            }
            list.ResumeLayout(true);
            ctx.ClearFocus();

            int extraHeight = (keyExpanded ? categoryButtons.Count * ctx.ItemHeight : 0)
                + (repeatOn || holdOn ? ctx.AccordionHeight : 0)
                + (resetAllExpanded ? ctx.ItemHeight : 0);
            ctx.ReportHeight(ctx.BaseCardHeight + extraHeight);
        }

        keyButton.Click += (_, _) =>
        {
            ctx.CloseCategoryPopup();
            keyExpanded = !keyExpanded;
            Theme.SetToggleAppearance(keyButton, keyExpanded);
            RebuildList();
        };

        void SaveBehavior() => KeyMap.SetBehavior(word, repeatOn, holdOn, duration, infiniteOn);

        // Editing the timer in any way — adding time or resetting it — means
        // you're moving away from infinite mode: turn the Infinite toggle
        // itself off (not just release whatever's currently engaged), so the
        // dashboard and the actual behavior agree with each other again.
        void DisengageInfinite()
        {
            if (infiniteOn)
            {
                infiniteOn = false;
                Theme.SetToggleAppearance(infiniteButton, false);
            }
            KeyExecutor.ForceRelease(word);
        }

        // Repeat and Hold each get their own independent timer/Infinite
        // setup — switching from one to the other resets it back to default
        // rather than carrying over whatever was configured for the mode
        // you're leaving.
        void ResetTimingForModeSwitch()
        {
            duration = 0.0;
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
        }

        // Repeat and Hold are mutually exclusive — tapping the key repeatedly
        // and holding it down don't mean anything combined, so turning one on
        // turns the other off.
        repeatButton.Click += (_, _) =>
        {
            repeatOn = !repeatOn;
            if (repeatOn)
            {
                holdOn = false;
                Theme.SetToggleAppearance(holdButton, false);
                ResetTimingForModeSwitch();
            }
            Theme.SetToggleAppearance(repeatButton, repeatOn);
            SaveBehavior();
            RebuildList();
        };
        holdButton.Click += (_, _) =>
        {
            holdOn = !holdOn;
            if (holdOn)
            {
                repeatOn = false;
                Theme.SetToggleAppearance(repeatButton, false);
                ResetTimingForModeSwitch();
            }
            Theme.SetToggleAppearance(holdButton, holdOn);
            SaveBehavior();
            RebuildList();
        };
        infiniteButton.Click += (_, _) =>
        {
            infiniteOn = !infiniteOn;
            Theme.SetToggleAppearance(infiniteButton, infiniteOn);
            // Infinite ignores the duration entirely, so turning it on clears
            // whatever timer value was set — it'd otherwise look like a
            // leftover duration that doesn't actually do anything anymore.
            if (infiniteOn)
            {
                duration = 0.0;
                durationLabel.Text = Theme.FormatDuration(duration);
            }
            SaveBehavior();
        };

        plusOne.Click += (_, _) =>
        {
            duration = Math.Round(duration + 1.0, 1);
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        plusTenth.Click += (_, _) =>
        {
            duration = Math.Round(duration + 0.1, 1);
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        resetDurationButton.Click += (_, _) =>
        {
            duration = 0.0;
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        void ResetCard()
        {
            ctx.CloseCategoryPopup();
            KeyMap.ResetToDefault(word);
            keyButton.Text = KeyLabelFor(word);
            duration = 0.0;
            durationLabel.Text = Theme.FormatDuration(duration);
            repeatOn = false;
            holdOn = false;
            infiniteOn = false;
            keyExpanded = false;
            resetAllExpanded = false;
            Theme.SetToggleAppearance(repeatButton, false);
            Theme.SetToggleAppearance(holdButton, false);
            Theme.SetToggleAppearance(infiniteButton, false);
            Theme.SetToggleAppearance(keyButton, false);
            Theme.SetToggleAppearance(resetCardButton, false);
            RebuildList();
        }
        ctx.RegisterResetAction(ResetCard);

        resetAllButton.Click += (_, _) => ctx.ResetAllCards();

        // Easter eggs on Reset — two independent sliding time windows, both
        // checked on every click:
        //  - 3 taps within 1.5s expands a "Reset All" row underneath (an
        //    accordion, same as Key/Repeat/Hold) with a button that resets
        //    every card, not just this one.
        //  - 2 taps within 1s brings back the Press tag if it's currently
        //    imploded (see the shell's own tap-counter for Press itself).
        int resetTapCount = 0;
        var resetTapTimer = new System.Windows.Forms.Timer { Interval = 600 };
        resetTapTimer.Tick += (_, _) =>
        {
            resetTapCount = 0;
            resetTapTimer.Stop();
        };
        var resetReappearClickTimes = new List<DateTime>();
        resetCardButton.Click += (_, _) =>
        {
            resetTapCount++;
            resetTapTimer.Stop();
            resetTapTimer.Start();

            // Lighting the button up right away, before the resize that
            // RebuildList triggers, seems to be what was masking the flash
            // for Key — doing the same here for the same reason.
            Theme.SetToggleAppearance(resetCardButton, true);
            ResetCard();

            if (resetTapCount == 3)
            {
                resetAllExpanded = true;
                Theme.SetToggleAppearance(resetCardButton, true);
                RebuildList();
            }

            if (ctx.PressTagEasterEggEnabled)
            {
                var now = DateTime.UtcNow;
                resetReappearClickTimes.Add(now);
                resetReappearClickTimes.RemoveAll(t => (now - t).TotalSeconds > 1.0);
                if (resetReappearClickTimes.Count >= 2)
                {
                    resetReappearClickTimes.Clear();
                    ctx.ShowPressTagIfHidden();
                }
            }
        };

        RebuildList();

        card.Controls.Add(list);

        // Profile switching rebuilds these cards from scratch, disposing the
        // old ones — without this, the tap-counter Timer above would keep
        // ticking in the background forever with nothing left to act on.
        card.Disposed += (_, _) => resetTapTimer.Dispose();

        return card;
    }
}
