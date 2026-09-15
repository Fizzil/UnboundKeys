namespace VoicePress;

// The Profiles tab: "Add Profile" is always first; clicking it expands an
// inline naming row. Existing profiles list below it — Default first
// (never deletable) — each a name button (click to switch to it) plus a
// delete "X" for the rest.
internal sealed class ProfilesTab : IDashboardTab
{
    public const string TabId = "__profiles__";
    public string Id => TabId;
    public string Label => "Profile";

    public Control BuildContent(DashboardTabContext ctx)
    {
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
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };
        card.Controls.Add(list);

        var existingNames = new HashSet<string>(Settings.LoadProfileNames());
        var profileRows = new List<TableLayoutPanel>();

        var addProfileButton = Theme.MakeListButton("Add Profile");
        bool namingExpanded = false;

        // No border box at all now — instead the field just shows its own
        // ghost/placeholder text ("your profile") in the accent color until
        // clicked, the same idea as a normal placeholder, then swaps to an
        // empty editable field. The field and its confirm button are sized
        // to about half the row's width instead of stretching full-width,
        // so the row reads as a small compact strip rather than a big bar
        // with a lot of dead space around a tiny bit of text.
        const string NamePlaceholder = "Profile name...";
        // A single-line TextBox has a well-known WinForms quirk: it ignores
        // whatever height Dock=Fill gives it and snaps back to its own
        // preferred (font-based) height, staying pinned to the top of that
        // space — which is why it was sitting at the top of the row with
        // blank space below it. Wrapping it in a plain Panel (which doesn't
        // have that quirk) and manually centering it inside that panel on
        // every resize fixes it.
        var nameBox = new TextBox
        {
            Margin = new Padding(0),
            BackColor = Theme.Current.Button,
            ForeColor = Theme.Current.Accent,
            BorderStyle = BorderStyle.None,
            // A single-line TextBox's height always tracks its font size —
            // there's no separate height property that sticks — so making
            // it ~20% taller means a ~20% bigger font (10 -> 12).
            Font = new Font("Segoe UI", 12f),
            Text = NamePlaceholder,
            TextAlign = HorizontalAlignment.Center,
        };
        nameBox.GotFocus += (_, _) =>
        {
            if (nameBox.Text == NamePlaceholder)
                nameBox.Text = "";
        };
        nameBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(nameBox.Text))
                nameBox.Text = NamePlaceholder;
        };
        var nameBoxHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Theme.Current.Button,
        };
        nameBoxHost.Controls.Add(nameBox);
        nameBoxHost.Layout += (_, _) =>
        {
            nameBox.Width = nameBoxHost.ClientSize.Width;
            nameBox.Left = 0;
            // A plain 50/50 split still reads as sitting a bit low, since
            // the textbox's own reported height carries a little extra
            // room below the text for descenders — giving the space above
            // a smaller share (35%) than below (65%) corrects for that.
            int emptySpace = nameBoxHost.ClientSize.Height - nameBox.Height;
            nameBox.Top = Math.Max(0, (int)(emptySpace * 0.35));
        };
        var confirmButton = Theme.MakeTinyButton("✓");
        // Same idea as the field above: a plain centered checkmark glyph
        // still reads as sitting a bit low (the character's own metrics
        // leave more visual space above it than below), so padding the
        // bottom of its content area nudges the centered glyph upward —
        // roughly 30% of the row's own height (ItemHeight).
        confirmButton.Padding = new Padding(0, 0, 0, (int)(ctx.ItemHeight * 0.3));

        var namingRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 1, 0, 1),
            ColumnCount = 3,
            RowCount = 1,
            // The same gray as every other button's background (not the
            // card's black), so the blank strips either side of the field
            // read as that button's own padding rather than a gap in it —
            // the whole row looks like one button with the field embedded.
            BackColor = Theme.Current.Button,
        };
        // A blank strip on the left matching the confirm button's width on
        // the right, so the field sits centered between them — its own text
        // is also center-aligned, so it lines up with "Add Profile"'s
        // centered text in the row above/below it.
        namingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15)); // blank — matches the button's width
        namingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); // field, centered
        namingRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15)); // confirm button, flush right
        namingRow.Controls.Add(nameBoxHost, 1, 0);
        namingRow.Controls.Add(confirmButton, 2, 0);

        void RebuildProfilesList()
        {
            list.SuspendLayout();
            list.Controls.Clear();
            list.RowStyles.Clear();

            var rows = new List<Control> { addProfileButton };
            if (namingExpanded)
                rows.Add(namingRow);
            rows.AddRange(profileRows);

            list.RowCount = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, ctx.ItemHeight));
                list.Controls.Add(rows[i], 0, i);

                // Same fix as the word cards' own list: each row's 1px
                // bottom margin is a spacer to the row after it, so the
                // last row needs it zeroed instead of leaving a 1px sliver
                // of black exposed below it, at the card's true bottom edge.
                var m = rows[i].Margin;
                rows[i].Margin = new Padding(m.Left, m.Top, m.Right, i == rows.Count - 1 ? 0 : 1);
            }
            list.ResumeLayout(true);
            ctx.ClearFocus();

            foreach (var row in profileRows)
                if (row.Controls.Count > 0 && row.Controls[0] is Button nameButton)
                    Theme.SetToggleAppearance(nameButton, nameButton.Text == KeyMap.ActiveProfile);

            ctx.ReportHeight(ctx.ItemHeight * rows.Count);
        }

        ctx.OnProfileSwitched(RebuildProfilesList);

        TableLayoutPanel AddProfileRow(string name)
        {
            bool deletable = name != Settings.DefaultProfileName;

            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 1),
                ColumnCount = deletable ? 2 : 1,
                RowCount = 1,
                BackColor = Theme.Current.Background,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, deletable ? 80 : 100));
            if (deletable)
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            var nameButton = Theme.MakeListButton(name);
            nameButton.Click += (_, _) =>
            {
                // Picking a profile while the naming row is still open (from
                // an earlier "Add Profile" tap) should close it, same as
                // tapping "Add Profile" again would — RebuildProfilesList
                // runs as part of the shell's own highlight refresh.
                namingExpanded = false;
                ctx.SwitchToProfile(name);
            };
            row.Controls.Add(nameButton, 0, 0);

            if (deletable)
            {
                // Tap once to arm (lights up), tap again within 2 seconds to
                // actually delete — same "confirm via a second tap" language
                // as the rest of the app, since this is destructive.
                var deleteButton = Theme.MakeTinyButton("✕");
                bool armed = false;
                var armTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                armTimer.Tick += (_, _) =>
                {
                    armed = false;
                    armTimer.Stop();
                    Theme.SetToggleAppearance(deleteButton, false);
                };
                deleteButton.Click += (_, _) =>
                {
                    if (!armed)
                    {
                        armed = true;
                        Theme.SetToggleAppearance(deleteButton, true);
                        armTimer.Stop();
                        armTimer.Start();
                        return;
                    }

                    armTimer.Stop();
                    armTimer.Dispose();
                    Settings.DeleteProfile(name);
                    existingNames.Remove(name);
                    profileRows.Remove(row);
                    row.Dispose();

                    bool wasActive = KeyMap.ActiveProfile == name;
                    RebuildProfilesList();
                    if (wasActive)
                        ctx.SwitchToProfile(Settings.DefaultProfileName);
                };
                row.Controls.Add(deleteButton, 1, 0);
            }

            profileRows.Add(row);
            return row;
        }

        void TryCreateProfile()
        {
            var name = nameBox.Text == NamePlaceholder ? "" : nameBox.Text.Trim();
            if (string.IsNullOrEmpty(name) || existingNames.Contains(name))
                return;

            var freshWords = new Dictionary<string, ushort>(KeyMap.DefaultWords, StringComparer.OrdinalIgnoreCase);
            var freshExtraWords = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
            var freshBehaviors = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in KeyMap.RemappableWords)
            {
                freshExtraWords[word] = new List<ushort>();
                freshBehaviors[word] = new KeyBehavior();
            }
            Settings.CreateProfileIfMissing(name, freshWords, freshExtraWords, freshBehaviors);

            existingNames.Add(name);
            AddProfileRow(name);
            namingExpanded = false;
            RebuildProfilesList();
        }

        addProfileButton.Click += (_, _) =>
        {
            namingExpanded = !namingExpanded;
            if (namingExpanded)
                nameBox.Text = NamePlaceholder;
            RebuildProfilesList();
        };
        confirmButton.Click += (_, _) => TryCreateProfile();
        nameBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                TryCreateProfile();
            }
        };

        foreach (var name in existingNames)
            AddProfileRow(name);
        RebuildProfilesList();

        return card;
    }
}
