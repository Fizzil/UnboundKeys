namespace VoicePress;

// One mouse button's card — same shape and behavior as WordCardTab (Key,
// Add Key, Repeat, Hold, Reset, the shared timing row), just driven by
// MouseMap instead of KeyMap. The one real difference: a button starts with
// no key at all ("Not Mapped") rather than a natural default, since unlike a
// spoken word there's no obvious key a mouse button should send until you
// pick one — see MouseMap's own comment for why.
internal sealed class MouseButtonCardTab : IDashboardTab
{
    private readonly string _buttonId;

    public MouseButtonCardTab(string buttonId) => _buttonId = buttonId;

    public string Id => _buttonId;

    // Short text for the tab button itself (the strip only has room for a
    // handful of pixels per button) — the full name shows as a tooltip,
    // wired up where the button is actually created (DashboardForm).
    private static readonly Dictionary<string, string> ShortLabels = new()
    {
        ["right"] = "RM",
        ["middle"] = "MM",
        ["x1"] = "M4",
        ["x2"] = "M5",
        ["wheelup"] = "W↑",
        ["wheeldown"] = "W↓",
    };

    public string Label => ShortLabels.TryGetValue(_buttonId, out var s) ? s : _buttonId;

    private static string KeyLabelFor(string id) =>
        MouseMap.Enabled[id] ? $"Key 1: {KeyCatalog.DisplayNameFor(MouseMap.Words[id])}" : "Key 1: Not Mapped";

    private static string ExtraKeyLabel(int slotNumber, ushort vk) => $"Key {slotNumber}: {KeyCatalog.DisplayNameFor(vk)}";

    public Control BuildContent(DashboardTabContext ctx)
    {
        var id = _buttonId;
        var behavior = MouseMap.Behaviors[id];
        double duration = behavior.DurationSeconds;

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

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Theme.Current.Background,
        };

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

        var keyButton = Theme.MakeListButton(KeyLabelFor(id));
        Theme.EnableTabUnderline(keyButton);
        bool keyExpanded = false;
        var categoryButtons = BuildCategoryButtons(entry =>
        {
            MouseMap.Rebind(id, entry.VkCode);
            keyButton.Text = KeyLabelFor(id);
        });

        var addKeyButton = Theme.MakeListButton("Add Key");
        var extraKeyRows = new List<Control>();
        var extraKeyExpanded = new List<bool>(new bool[MouseMap.ExtraWords[id].Count]);

        void CollapseAllKeys()
        {
            keyExpanded = false;
            Theme.SetTabSelected(keyButton, false);
            for (int idx = 0; idx < extraKeyExpanded.Count; idx++)
                extraKeyExpanded[idx] = false;
        }

        bool repeatOn = behavior.Repeat;
        var repeatButton = Theme.MakeListButton("Repeat");
        Theme.EnableTabUnderline(repeatButton);
        Theme.SetTabSelected(repeatButton, repeatOn);

        bool holdOn = behavior.Hold;
        var holdButton = Theme.MakeListButton("Hold");
        Theme.EnableTabUnderline(holdButton);
        Theme.SetTabSelected(holdButton, holdOn);

        var resetCardButton = Theme.MakeListButton("Reset");
        bool resetAllExpanded = false;
        var resetAllButton = Theme.MakeListButton("Reset All");

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

        bool useCustomRepeatIntervals = behavior.UseCustomRepeatIntervals;
        var repeatIntervalButton = Theme.MakeListButton("Repeat Interval");
        Theme.EnableTabUnderline(repeatIntervalButton);
        Theme.SetTabSelected(repeatIntervalButton, useCustomRepeatIntervals);

        int totalKeyCount = 1 + MouseMap.ExtraWords[id].Count;
        List<double> keyIntervalSeconds = behavior.RepeatKeyIntervalsSeconds.Count == totalKeyCount
            ? new List<double>(behavior.RepeatKeyIntervalsSeconds)
            : new List<double>(new double[totalKeyCount]);

        int selectedKIndex = -1;

        var keyIntervalRowPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 0, 1),
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };

        void RebuildKeyIntervalRow()
        {
            keyIntervalRowPanel.Controls.Clear();
            keyIntervalRowPanel.ColumnStyles.Clear();
            keyIntervalRowPanel.ColumnCount = Math.Max(keyIntervalSeconds.Count * 2, 1);
            float colWidth = 100f / (keyIntervalSeconds.Count * 2);

            for (int idx = 0; idx < keyIntervalSeconds.Count; idx++)
            {
                int kIndex = idx;
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

        void RebuildExtraKeyRows()
        {
            extraKeyRows.Clear();
            var extras = MouseMap.ExtraWords[id];
            for (int i = 0; i < extras.Count; i++)
            {
                int slotIndex = i;

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
                    var deleteButton = Theme.MakeListButton("✕");
                    deleteButton.Click += (_, _) =>
                    {
                        MouseMap.RemoveExtraKey(id, slotIndex);
                        extraKeyExpanded.RemoveAt(slotIndex);

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
                        MouseMap.SetExtraKey(id, slotIndex, entry.VkCode);
                        RebuildExtraKeyRows();
                        RebuildList();
                    });
                    extraKeyRows.AddRange(extraCategoryButtons);
                }
            }
        }
        RebuildExtraKeyRows();

        void RebuildList()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            list.RowStyles.Clear();

            bool atExtraKeyCap = MouseMap.ExtraWords[id].Count >= 2;

            bool canCustomizeRepeatInterval = repeatOn && MouseMap.ExtraWords[id].Count >= 1;
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
            ctx.CloseCategoryPopup();
            // Unlike a word (which always has a real key already), an
            // unmapped button has nothing sensible to copy for the first
            // extra key — fall back to "A" just so there's some value to
            // start from; picking a real one is the very next step anyway.
            ushort seed = MouseMap.Enabled[id] ? MouseMap.Words[id] : (ushort)0x41;
            MouseMap.AddExtraKey(id, seed);
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

        void SaveBehavior() => MouseMap.SetBehavior(id, repeatOn, holdOn, duration, infiniteOn, useCustomRepeatIntervals, keyIntervalSeconds);

        void DisengageInfinite()
        {
            if (infiniteOn)
            {
                infiniteOn = false;
                Theme.SetTabSelected(infiniteButton, false);
            }
            KeyExecutor.ForceRelease(id);
        }

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
            if (infiniteOn)
            {
                duration = 0.0;
                durationLabel.Text = Theme.FormatDuration(duration);
            }
            SaveBehavior();
        };

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
            MouseMap.ResetToDefault(id); // also clears extra keys and disables the button
            keyButton.Text = KeyLabelFor(id);
            extraKeyExpanded.Clear();
            RebuildExtraKeyRows();
            duration = 0.0;
            durationLabel.Text = Theme.FormatDuration(duration);
            keyIntervalSeconds.Clear();
            keyIntervalSeconds.Add(0.0);
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

        card.Disposed += (_, _) => resetTapTimer.Dispose();

        return card;
    }
}
