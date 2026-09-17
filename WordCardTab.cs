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

    private static string KeyLabelFor(string word) => $"Key 1: {KeyCatalog.DisplayNameFor(KeyMap.Words[word])}";

    // Extra keys are numbered from 2 (the primary key is "Key 1"), so the
    // first extra reads "Key 2: ...", the second "Key 3: ...". Recomputed
    // fresh from each row's live position every rebuild, so removing "Key 2"
    // automatically renumbers "Key 3" down to "Key 2" rather than leaving a
    // gap.
    private static string ExtraKeyLabel(int slotNumber, ushort vk) => $"Key {slotNumber}: {KeyCatalog.DisplayNameFor(vk)}";

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

        // A row of key-category buttons (Letters, Numbers, Function Keys,
        // ...) — hovering one pops out a list of its individual keys to the
        // left of the card. Shared by both Key (rebinds the main key) and
        // Add Key (adds an extra one), so the two accordions behave
        // identically apart from what picking a key actually does.
        //
        // Only one category's key list should be open at a time. This uses a
        // plain borderless window rather than a ContextMenuStrip — a native
        // menu puts Windows into a special "menu tracking" mode the instant
        // it opens, which blocks MouseEnter from reaching sibling controls
        // (the other category buttons) until it closes. A regular window
        // doesn't have that restriction, so hovering across categories works.
        List<Control> BuildCategoryButtons(Action<KeyCatalog.Entry> onSelect)
        {
            var buttons = new List<Control>();
            foreach (var (category, keys) in KeyCatalog.Groups)
            {
                var categoryButton = Theme.MakeListButton(category);
                categoryButton.MouseEnter += (_, _) => ctx.ShowCategoryPopup(categoryButton, keys, onSelect);
                buttons.Add(categoryButton);
            }
            return buttons;
        }

        // 2. Key 1 — click to expand the category accordion above; click Key
        // again to collapse it. Picking a key rebinds the card's main key.
        var keyButton = Theme.MakeListButton(KeyLabelFor(word));
        Theme.EnableTabUnderline(keyButton);
        bool keyExpanded = false;
        var categoryButtons = BuildCategoryButtons(entry =>
        {
            KeyMap.Rebind(word, entry.VkCode);
            keyButton.Text = KeyLabelFor(word);
        });

        // 1. Add Key — adds one more key that fires alongside the main one
        // as a combo, instead of replacing it. Clicking it just adds a new
        // row (starting as a copy of the main key) directly below Key 1 and
        // any earlier extras — Key 1 stays on top, extras stack downward
        // from it in order, ending just above Repeat — it doesn't open a
        // category picker itself; the new row does that, the same way the
        // main Key row already does. Only shown while there's room for
        // another (up to two extras, three keys total); disappears at the
        // cap the same way Profiles never shows a delete "✕" on Default.
        // (Built further down, once RebuildList's other dependencies —
        // Repeat/Hold/Reset/the timing row — all exist; it closes over
        // RebuildList too, same as this one does.)
        var addKeyButton = Theme.MakeListButton("Add Key");
        var extraKeyRows = new List<Control>();
        // Parallel to KeyMap.ExtraWords[word] — seeded with one "false" per
        // already-saved extra key, since a word can already have extras from
        // a previous session by the time its card is first built.
        var extraKeyExpanded = new List<bool>(new bool[KeyMap.ExtraWords[word].Count]);

        // Only one key's category dropdown should be open at a time — Key
        // 1's or any extra's. Called before opening a different one, so the
        // previously open dropdown (whichever key it belonged to) collapses
        // first.
        void CollapseAllKeys()
        {
            keyExpanded = false;
            Theme.SetTabSelected(keyButton, false);
            for (int idx = 0; idx < extraKeyExpanded.Count; idx++)
                extraKeyExpanded[idx] = false;
        }

        // 2. Repeat — a toggle button: click to turn on/off, lit up when on.
        // Turning it on also opens the timing dropdown (see below); turning
        // it off closes it again.
        bool repeatOn = behavior.Repeat;
        var repeatButton = Theme.MakeListButton("Repeat");
        Theme.EnableTabUnderline(repeatButton);
        Theme.SetTabSelected(repeatButton, repeatOn);

        // 3. Hold — same toggle-button treatment, same shared dropdown.
        bool holdOn = behavior.Hold;
        var holdButton = Theme.MakeListButton("Hold");
        Theme.EnableTabUnderline(holdButton);
        Theme.SetTabSelected(holdButton, holdOn);

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
        Theme.EnableTabUnderline(infiniteButton);
        Theme.SetTabSelected(infiniteButton, infiniteOn);

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

        // Repeat Interval: only shown while Repeat is on for a word with 2+
        // keys (see RebuildList) — a plain toggle button, no value of its
        // own. Turning it on reveals a row below it with one "K1"/"K2"/...
        // button per key (see RebuildKeyIntervalRow) — K1's own gap before
        // K2, K2's before K3, and so on, wrapping back to K1 — each
        // independently selectable so +1/+0.1/reset (from the timing row
        // above) target that key's gap instead of the main duration. ∞
        // keeps doing what it already does regardless of what's selected,
        // since it's a whole-word setting, not specific to one duration.
        // Turning this off makes the custom gaps stop applying entirely —
        // repeats fall back to the plain fixed gap, same as any 1-key word.
        bool useCustomRepeatIntervals = behavior.UseCustomRepeatIntervals;
        var repeatIntervalButton = Theme.MakeListButton("Repeat Interval");
        Theme.EnableTabUnderline(repeatIntervalButton);
        Theme.SetTabSelected(repeatIntervalButton, useCustomRepeatIntervals);

        // One gap value per key (index 0 = K1, the primary key). Guarded
        // against stale/mismatched saved data — e.g. an old profile saved
        // before this feature existed — by rebuilding to the right length
        // rather than trusting it blindly.
        int totalKeyCount = 1 + KeyMap.ExtraWords[word].Count;
        List<double> keyIntervalSeconds = behavior.RepeatKeyIntervalsSeconds.Count == totalKeyCount
            ? new List<double>(behavior.RepeatKeyIntervalsSeconds)
            : new List<double>(new double[totalKeyCount]);

        // -1 = no key selected -> +1/+0.1/reset target the main duration.
        int selectedKIndex = -1;

        var keyIntervalRowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 0, 1),
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };

        // Rebuilt whenever a key is added/removed (the number of K buttons
        // changes) or a different K is selected (to refresh which one is
        // lit up) — mirrors the pattern used for the extra key rows above.
        void RebuildKeyIntervalRow()
        {
            keyIntervalRowPanel.Controls.Clear();
            keyIntervalRowPanel.ColumnStyles.Clear();
            keyIntervalRowPanel.ColumnCount = Math.Max(keyIntervalSeconds.Count * 2, 1);
            float colWidth = 100f / (keyIntervalSeconds.Count * 2);

            for (int idx = 0; idx < keyIntervalSeconds.Count; idx++)
            {
                int kIndex = idx; // captured per-button, not the loop variable
                keyIntervalRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, colWidth));
                keyIntervalRowPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, colWidth));

                var kButton = Theme.MakeTinyButton($"K{kIndex + 1}");
                Theme.EnableTabUnderline(kButton);
                Theme.SetTabSelected(kButton, selectedKIndex == kIndex);
                kButton.Click += (_, _) =>
                {
                    selectedKIndex = selectedKIndex == kIndex ? -1 : kIndex;
                    RebuildKeyIntervalRow();
                };

                var kLabel = new Label
                {
                    Text = Theme.FormatDuration(keyIntervalSeconds[kIndex]),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(1),
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Theme.Current.Accent,
                    BackColor = Theme.Current.Button,
                    Font = new Font("Segoe UI", 11f),
                };

                keyIntervalRowPanel.Controls.Add(kButton, idx * 2, 0);
                keyIntervalRowPanel.Controls.Add(kLabel, idx * 2 + 1, 0);
            }
        }
        RebuildKeyIntervalRow();

        // Rebuilt fresh from KeyMap.ExtraWords[word] every time one is added,
        // removed, or expanded/collapsed, so the "Key 2"/"Key 3" numbering
        // always matches actual position rather than going stale after a
        // delete. Declared here rather than up by Add Key's other fields
        // because each row's own category picker closes over RebuildList
        // (below), which itself closes over Repeat/Hold/Reset/the timing row
        // above — same reasoning as the main Key/categoryButtons pairing.
        void RebuildExtraKeyRows()
        {
            extraKeyRows.Clear();
            var extras = KeyMap.ExtraWords[word];
            for (int i = 0; i < extras.Count; i++)
            {
                int slotIndex = i; // captured per-row, not the loop variable

                // A plain full-width label, same as the primary Key button —
                // clicking it expands/collapses this row's own category
                // picker, exactly like Key does. The "✕" to remove this key
                // lives inside that dropdown instead of on the row itself
                // (see below), so only the two added keys ever show one.
                var label = Theme.MakeListButton(ExtraKeyLabel(slotIndex + 2, extras[slotIndex]));
                Theme.EnableTabUnderline(label);
                Theme.SetTabSelected(label, extraKeyExpanded[slotIndex]);
                label.Click += (_, _) =>
                {
                    ctx.CloseCategoryPopup();
                    bool opening = !extraKeyExpanded[slotIndex];
                    CollapseAllKeys();
                    extraKeyExpanded[slotIndex] = opening;
                    RebuildExtraKeyRows();
                    RebuildList();
                };
                extraKeyRows.Add(label);

                if (extraKeyExpanded[slotIndex])
                {
                    // The "✕" is the first row of the dropdown, above the
                    // category buttons — full-width, one click and it's
                    // gone (no arm/confirm here, unlike Reset or a profile
                    // delete — the key's own row is already a deliberate,
                    // out-of-the-way place to find this button).
                    var deleteButton = Theme.MakeListButton("✕");
                    deleteButton.Click += (_, _) =>
                    {
                        KeyMap.RemoveExtraKey(word, slotIndex);
                        extraKeyExpanded.RemoveAt(slotIndex);

                        // Keep the repeat-interval gaps in sync: slot 0 is
                        // always the primary key, so an extra at slotIndex
                        // is K-slot (slotIndex + 1).
                        int removedKIndex = slotIndex + 1;
                        if (removedKIndex < keyIntervalSeconds.Count)
                            keyIntervalSeconds.RemoveAt(removedKIndex);
                        if (selectedKIndex == removedKIndex)
                            selectedKIndex = -1;
                        else if (selectedKIndex > removedKIndex)
                            selectedKIndex--;

                        RebuildExtraKeyRows();
                        RebuildKeyIntervalRow();
                        RebuildList();
                    };
                    extraKeyRows.Add(deleteButton);

                    var extraCategoryButtons = BuildCategoryButtons(entry =>
                    {
                        KeyMap.SetExtraKey(word, slotIndex, entry.VkCode);
                        RebuildExtraKeyRows();
                        RebuildList();
                    });
                    extraKeyRows.AddRange(extraCategoryButtons);
                }
            }
        }
        RebuildExtraKeyRows();

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

            bool atExtraKeyCap = KeyMap.ExtraWords[word].Count >= 2;

            // A 2+ key word (at least one extra added) gets a repeat
            // interval toggle to tune — a 1-key word has nothing between
            // taps worth spacing out. If the toggle disappears (Repeat
            // turned off, or the word drops back to 1 key) while it was on,
            // switch it back off and drop the K selection so +1/+0.1/reset
            // don't silently target a hidden control.
            bool canCustomizeRepeatInterval = repeatOn && KeyMap.ExtraWords[word].Count >= 1;
            if (!canCustomizeRepeatInterval && useCustomRepeatIntervals)
            {
                useCustomRepeatIntervals = false;
                Theme.SetTabSelected(repeatIntervalButton, false);
            }
            if (!canCustomizeRepeatInterval)
                selectedKIndex = -1;
            bool showKeyIntervalRow = canCustomizeRepeatInterval && useCustomRepeatIntervals;

            var rows = new List<Control>();
            if (!atExtraKeyCap)
                rows.Add(addKeyButton);
            rows.Add(keyButton);
            if (keyExpanded)
                rows.AddRange(categoryButtons);
            rows.AddRange(extraKeyRows);
            rows.Add(repeatButton);
            if (repeatOn)
            {
                rows.Add(timingPanel);
                if (canCustomizeRepeatInterval)
                {
                    rows.Add(repeatIntervalButton);
                    if (showKeyIntervalRow)
                        rows.Add(keyIntervalRowPanel);
                }
            }
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

            int extraHeight = (!atExtraKeyCap ? ctx.ItemHeight : 0)
                + (keyExpanded ? categoryButtons.Count * ctx.ItemHeight : 0)
                + extraKeyRows.Count * ctx.ItemHeight
                + (repeatOn || holdOn ? ctx.AccordionHeight : 0)
                + (canCustomizeRepeatInterval ? ctx.ItemHeight : 0)
                + (showKeyIntervalRow ? ctx.ItemHeight : 0)
                + (resetAllExpanded ? ctx.ItemHeight : 0);
            ctx.ReportHeight(ctx.BaseCardHeight + extraHeight);
        }

        keyButton.Click += (_, _) =>
        {
            ctx.CloseCategoryPopup();
            bool opening = !keyExpanded;
            CollapseAllKeys();
            keyExpanded = opening;
            Theme.SetTabSelected(keyButton, keyExpanded);
            RebuildExtraKeyRows();
            RebuildList();
        };

        addKeyButton.Click += (_, _) =>
        {
            // Starts as a copy of the main key — not because that's a
            // meaningful default, just because a slot needs some value, and
            // clicking it to pick a real one (like the main Key row) is the
            // very next thing you'd do anyway.
            ctx.CloseCategoryPopup();
            KeyMap.AddExtraKey(word, KeyMap.Words[word]);
            extraKeyExpanded.Add(false);
            keyIntervalSeconds.Add(0.0);
            RebuildExtraKeyRows();
            RebuildKeyIntervalRow();
            RebuildList();
        };

        repeatIntervalButton.Click += (_, _) =>
        {
            useCustomRepeatIntervals = !useCustomRepeatIntervals;
            Theme.SetTabSelected(repeatIntervalButton, useCustomRepeatIntervals);
            if (!useCustomRepeatIntervals)
                selectedKIndex = -1;
            SaveBehavior();
            RebuildList();
        };

        void SaveBehavior() => KeyMap.SetBehavior(word, repeatOn, holdOn, duration, infiniteOn, useCustomRepeatIntervals, keyIntervalSeconds);

        // Editing the timer in any way — adding time or resetting it — means
        // you're moving away from infinite mode: turn the Infinite toggle
        // itself off (not just release whatever's currently engaged), so the
        // dashboard and the actual behavior agree with each other again.
        void DisengageInfinite()
        {
            if (infiniteOn)
            {
                infiniteOn = false;
                Theme.SetTabSelected(infiniteButton, false);
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
            for (int idx = 0; idx < keyIntervalSeconds.Count; idx++)
                keyIntervalSeconds[idx] = 0.0;
            useCustomRepeatIntervals = false;
            selectedKIndex = -1;
            Theme.SetTabSelected(repeatIntervalButton, false);
            RebuildKeyIntervalRow();
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
                Theme.SetTabSelected(holdButton, false);
                ResetTimingForModeSwitch();
            }
            Theme.SetTabSelected(repeatButton, repeatOn);
            SaveBehavior();
            RebuildList();
        };
        holdButton.Click += (_, _) =>
        {
            holdOn = !holdOn;
            if (holdOn)
            {
                repeatOn = false;
                Theme.SetTabSelected(repeatButton, false);
                ResetTimingForModeSwitch();
            }
            Theme.SetTabSelected(holdButton, holdOn);
            SaveBehavior();
            RebuildList();
        };
        infiniteButton.Click += (_, _) =>
        {
            infiniteOn = !infiniteOn;
            Theme.SetTabSelected(infiniteButton, infiniteOn);
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

        // +1/+0.1/reset target whichever K is selected in the repeat
        // interval row instead of the main duration — editing a key's gap
        // isn't "moving away from infinite mode" the way editing the main
        // duration is, so DisengageInfinite is skipped for it.
        plusOne.Click += (_, _) =>
        {
            if (selectedKIndex >= 0)
            {
                keyIntervalSeconds[selectedKIndex] = Math.Round(keyIntervalSeconds[selectedKIndex] + 1.0, 1);
                RebuildKeyIntervalRow();
                SaveBehavior();
                return;
            }
            duration = Math.Round(duration + 1.0, 1);
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        plusTenth.Click += (_, _) =>
        {
            if (selectedKIndex >= 0)
            {
                keyIntervalSeconds[selectedKIndex] = Math.Round(keyIntervalSeconds[selectedKIndex] + 0.1, 1);
                RebuildKeyIntervalRow();
                SaveBehavior();
                return;
            }
            duration = Math.Round(duration + 0.1, 1);
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        // Reset targets exactly whichever one K is selected, same as
        // +1/+0.1 — just that key's own gap. With the Repeat Interval row
        // visible but no particular K selected, it resets every key's gap
        // at once instead. Otherwise it's just the main duration.
        resetDurationButton.Click += (_, _) =>
        {
            if (selectedKIndex >= 0)
            {
                keyIntervalSeconds[selectedKIndex] = 0.0;
                RebuildKeyIntervalRow();
                SaveBehavior();
                return;
            }
            if (useCustomRepeatIntervals)
            {
                for (int idx = 0; idx < keyIntervalSeconds.Count; idx++)
                    keyIntervalSeconds[idx] = 0.0;
                RebuildKeyIntervalRow();
                SaveBehavior();
                return;
            }
            duration = 0.0;
            durationLabel.Text = Theme.FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        void ResetCard()
        {
            ctx.CloseCategoryPopup();
            KeyMap.ResetToDefault(word); // also clears this word's extra keys
            keyButton.Text = KeyLabelFor(word);
            extraKeyExpanded.Clear();
            RebuildExtraKeyRows();
            duration = 0.0;
            durationLabel.Text = Theme.FormatDuration(duration);
            keyIntervalSeconds.Clear();
            keyIntervalSeconds.Add(0.0); // only the primary key remains after reset
            selectedKIndex = -1;
            useCustomRepeatIntervals = false;
            RebuildKeyIntervalRow();
            repeatOn = false;
            holdOn = false;
            infiniteOn = false;
            keyExpanded = false;
            resetAllExpanded = false;
            Theme.SetTabSelected(repeatButton, false);
            Theme.SetTabSelected(holdButton, false);
            Theme.SetTabSelected(infiniteButton, false);
            Theme.SetTabSelected(keyButton, false);
            Theme.SetToggleAppearance(resetCardButton, false);
            Theme.SetTabSelected(repeatIntervalButton, false);
            RebuildList();
        }
        ctx.RegisterResetAction(ResetCard);

        resetAllButton.Click += (_, _) => ctx.ResetAllCards();

        // Easter egg on Reset: 3 taps within 1.5s expands a "Reset All" row
        // underneath (an accordion, same as Key/Repeat/Hold) with a button
        // that resets every card, not just this one.
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
            Theme.SetToggleAppearance(resetCardButton, true);
            ResetCard();

            if (resetTapCount == 3)
            {
                resetAllExpanded = true;
                Theme.SetToggleAppearance(resetCardButton, true);
                RebuildList();
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
