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
    private const int PressTagWidth = 100;
    private const int ProfileTabWidth = 70;
    private const int InitialCardWidth = 370;
    private const int BaseCardHeight = 324;
    private const int ItemHeight = BaseCardHeight / 4;
    private const int AccordionHeight = 56;

    // The Profiles tab's id in _cards/_tabButtons/etc. — distinct from any
    // real spoken word, so it can share the same generic tab machinery.
    private const string ProfilesTabId = "__profiles__";

    // The Press-tag implode/reappear easter egg is shelved for now — a
    // stray 1-2px gap keeps reappearing after the animation that we haven't
    // pinned down. The implementation (AnimatePressTag, SetPressTagWidth,
    // the tap counters below) is left in place to revisit later; this just
    // stops the two trigger points from calling it.
    private const bool PressTagEasterEggEnabled = false;

    // The card/action-bar content area's width — starts at InitialCardWidth,
    // but grows (never shrinks) if a future tab needs more room than the
    // existing tabs leave available. See AddActionBarTab/EnsureContentWidth.
    private int _contentWidth = InitialCardWidth;

    // The dashboard no longer has any outer padding — its visible content
    // starts exactly at its own window edges — so there's no offset left
    // for OverlayForm to account for when lining the two windows up.
    public const int TopInset = 0;

    // The action bar: a horizontal strip of tabs across the top (currently
    // the ten spoken-word tabs) plus the "Press" reminder in its own notch to
    // the left. New kinds of tabs — a Profiles tab, a How-To tab, etc. — can
    // be added later via AddActionBarTab without restructuring any of this;
    // each just needs a short label and a Control to show when selected.
    private TableLayoutPanel _actionBar;
    private Panel _actionBarContent;
    private TableLayoutPanel _outer;
    private Label _pressLabel;

    // The Press tag's current width, animated between 0 (imploded away) and
    // PressTagWidth (fully shown) — tap it 4 times to collapse it, tap Reset
    // 4 times to bring it back.
    private int _pressTagWidth = PressTagWidth;
    private bool _pressTagVisible = true;

    private readonly Dictionary<string, Control> _cards = new();
    private readonly Dictionary<string, Button> _tabButtons = new();

    // The full height each tab's card currently needs (not just "extra" on
    // top of a shared base) — different kinds of tabs don't all share the
    // same base shape. The word cards use BaseCardHeight plus whatever
    // accordions (Key's categories, Repeat/Hold's timing row) are open; the
    // Profiles card computes its own height from however many rows it has.
    private readonly Dictionary<string, int> _cardHeight = new();
    private string? _selectedWord;

    // Whether the currently selected tab's card is showing. Clicking a tab
    // that's already selected toggles this; clicking a different tab always
    // sets it back to true (SelectTab). Only the current tab's state matters
    // — switching away and back always re-expands, so nothing per-tab needs
    // to be remembered.
    private bool _selectedTabExpanded = true;

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

        // Starts collapsed — just the tab strip, nothing selected — rather
        // than opening straight into tab "one"'s card.
        ClientSize = new Size(
            ProfileTabWidth + _pressTagWidth + _contentWidth,
            TabStripHeight);

        // A 2x3 grid: "Profile" and "Press" each sit over their own empty
        // bottom cell, tabs sit over the card content to the right. The
        // window's Region (set below) then cuts away both those empty
        // bottom cells entirely, so it reads as an L-shaped flag — Profile
        // and Press sticking out top-left — rather than a solid rectangle
        // with a blank patch under them.
        //
        // No padding at all: every edge of the visible content sits flush
        // against the window's true edge, so nothing reads as a border
        // separate from the black/red theme itself.
        _outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0),
            BackColor = BackgroundColor,
        };
        _outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProfileTabWidth));
        _outer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _pressTagWidth));
        _outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _outer.RowStyles.Add(new RowStyle(SizeType.Absolute, TabStripHeight));
        _outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Easter egg: tap Press twice within 2 seconds to have it implode and
        // vanish from the action bar (no hover/selected look on it, unlike
        // the other buttons — it's not meant to read as a normal button).
        // Tap Reset twice within 1 second (see below) to bring it back.
        //
        // MouseDown, not Click: a Label's Click event doesn't reliably fire
        // on the second click of a fast double-click — Windows reinterprets
        // it as part of a double-click gesture instead of two separate
        // clicks. MouseDown fires for every physical press regardless.
        _pressLabel = new Label
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
        var pressClickTimes = new List<DateTime>();
        _pressLabel.MouseDown += (_, e) =>
        {
            if (!PressTagEasterEggEnabled || e.Button != MouseButtons.Left)
                return;

            var now = DateTime.UtcNow;
            pressClickTimes.Add(now);
            pressClickTimes.RemoveAll(t => (now - t).TotalSeconds > 2.0);

            if (pressClickTimes.Count >= 2)
            {
                pressClickTimes.Clear();
                if (_pressTagVisible)
                    AnimatePressTag(show: false);
            }
        };

        _actionBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 0,
            RowCount = 1,
            BackColor = BackgroundColor,
        };

        // All tabs' content lives in the same spot, stacked on top of each
        // other — only the selected one is visible at a time.
        _actionBarContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = BackgroundColor };

        // AddActionBarTab inserts at the left end of the bar, so this builds
        // the initial ten in reverse (10 down to 1) — each new insert at
        // index 0 pushes the previous ones right, leaving them in the
        // correct 1..10 left-to-right order once the loop finishes.
        var words = KeyMap.RemappableWords;
        for (int i = words.Length - 1; i >= 0; i--)
            AddActionBarTab(words[i], (i + 1).ToString(), MakeCard(words[i]));

        // Profile gets its own fixed slot to the left of Press, instead of
        // living in the action bar with the word tabs — it goes through the
        // same MakeTabButton wiring as every other tab (so switching to it,
        // highlighting it, etc. all work identically), it's just placed in
        // its own outer-grid column rather than inserted into _actionBar.
        // The numbered tabs' widths, order, and position are untouched.
        var profileButton = MakeTabButton(ProfilesTabId, "Profile", MakeProfilesCard());
        profileButton.Margin = new Padding(0);

        _outer.Controls.Add(profileButton, 0, 0);
        _outer.Controls.Add(_pressLabel, 1, 0);
        _outer.Controls.Add(_actionBar, 2, 0);
        _outer.Controls.Add(_actionBarContent, 2, 1);
        Controls.Add(_outer);

        RecomputeRegion();

        // Profile is the first control added to the form, so it's the
        // implicit default ActiveControl — clicking the separate overlay
        // icon shifts window activation away and back even though it never
        // takes real focus, and that activation blip was enough to make
        // Windows paint Profile's focus cue. Clearing ActiveControl on both
        // transitions means there's never a control left to draw one on.
        Activated += (_, _) => ActiveControl = null;
        Deactivate += (_, _) => ActiveControl = null;
    }

    // Left-to-right order of the action bar's tabs, each with its own fixed
    // (never-changing-once-set) pixel width. New tabs are inserted at index
    // 0 — the left end — so existing tabs never get resized or reordered to
    // make room; the bar (and the window) grows wider instead.
    private readonly List<(string Id, Button Button, int Width)> _actionBarTabs = new();

    // Builds one tab's button and wires up its click behavior (toggle open/
    // closed if it's already the active tab, otherwise switch to it), and
    // registers its content in the shared display area. Shared by every
    // tab regardless of where its button ends up living — the action bar,
    // or the Profile tab's own fixed slot to the left of Press.
    private Button MakeTabButton(string id, string label, Control content)
    {
        var tabButton = MakeTinyButton(label);
        tabButton.Margin = new Padding(0);
        tabButton.Padding = new Padding(0);
        tabButton.Font = new Font("Segoe UI", 9f);
        tabButton.Click += (_, _) =>
        {
            // Clicking the already-active tab toggles its card open/closed;
            // clicking a different tab always opens (SelectTab handles that).
            if (_selectedWord == id)
            {
                _selectedTabExpanded = !_selectedTabExpanded;
                _cards[id].Visible = _selectedTabExpanded;
                AdjustHeight();
            }
            else
            {
                SelectTab(id);
            }
        };
        _tabButtons[id] = tabButton;

        content.Visible = false;
        _actionBarContent.Controls.Add(content);
        _cards[id] = content;

        return tabButton;
    }

    // Adds one more tab to the action bar itself: a small button with the
    // given label, showing the given content when selected, inserted at the
    // left end of the bar. Existing tabs keep their exact size and position —
    // this is how a future How-To tab (or similar) would get added, alongside
    // the word tabs, without disturbing them.
    private void AddActionBarTab(string id, string label, Control content, int? width = null)
    {
        var tabButton = MakeTabButton(id, label, content);
        int tabWidth = width ?? InitialCardWidth / KeyMap.RemappableWords.Length;
        _actionBarTabs.Insert(0, (id, tabButton, tabWidth));
        RebuildActionBar();
    }

    // Re-lays-out the action bar's tabs in their current left-to-right order,
    // each at its own fixed width, then grows the window if the bar now
    // needs more room than the card content currently has.
    private void RebuildActionBar()
    {
        _actionBar.SuspendLayout();
        _actionBar.Controls.Clear();
        _actionBar.ColumnStyles.Clear();
        _actionBar.ColumnCount = _actionBarTabs.Count;

        int totalWidth = 0;
        for (int i = 0; i < _actionBarTabs.Count; i++)
        {
            var (_, tabButton, tabWidth) = _actionBarTabs[i];
            _actionBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, tabWidth));
            _actionBar.Controls.Add(tabButton, i, 0);
            totalWidth += tabWidth;
        }
        _actionBar.ResumeLayout(true);

        EnsureContentWidth(totalWidth);
    }

    // Grows (never shrinks) the card/action-bar content area to fit a
    // minimum width — used when the action bar's tabs need more room than
    // the window currently provides.
    private void EnsureContentWidth(int minWidth)
    {
        if (minWidth <= _contentWidth)
            return;

        _contentWidth = minWidth;
        var newSize = new Size(ProfileTabWidth + _pressTagWidth + _contentWidth, ClientSize.Height);

        SuspendLayout();
        ClientSize = newSize;
        RecomputeRegion();
        ResumeLayout(true);
        Invalidate(true);
        Update();
    }

    // Cut the empty bottom-left cell out of the window's shape entirely, so
    // nothing renders there — it's not just blank/black, it's gone. Called
    // again whenever the window's height changes (expanding/collapsing an
    // accordion), since the shape depends on ClientSize.
    private void RecomputeRegion()
    {
        var shape = new Region(new Rectangle(Point.Empty, ClientSize));

        // When the Profile tab's own card is open, it stretches back to
        // cover the notch itself (see SelectTab) rather than leaving it as
        // dead space — so there's nothing to cut away in that case.
        bool profileCardOpen = _selectedWord == ProfilesTabId && _selectedTabExpanded;
        int notchWidth = profileCardOpen ? 0 : ProfileTabWidth + _pressTagWidth;
        if (notchWidth > 0)
        {
            shape.Exclude(new Rectangle(
                0,
                TabStripHeight,
                notchWidth,
                ClientSize.Height - TabStripHeight));
        }

        Region = shape;
    }

    // Animates the Press tag's width between 0 (imploded away) and its full
    // size, growing/shrinking the window and re-cutting its L-shape at each
    // step so it reads as the notch physically shrinking into (or growing
    // out of) the main body, rather than just vanishing/appearing instantly.
    private System.Windows.Forms.Timer? _pressTagAnimationTimer;

    private void AnimatePressTag(bool show)
    {
        if (show == _pressTagVisible && _pressTagAnimationTimer == null)
            return;

        // Cancel any animation already running instead of letting a second
        // one start alongside it — two timers independently calling
        // SetPressTagWidth could stomp on each other's state.
        _pressTagAnimationTimer?.Stop();
        _pressTagAnimationTimer?.Dispose();

        if (show)
            _pressLabel.Visible = true;

        const int steps = 8;
        int stepsDone = 0;
        int startWidth = _pressTagWidth;
        int endWidth = show ? PressTagWidth : 0;

        var timer = new System.Windows.Forms.Timer { Interval = 30 }; // 2x slower than the original 15ms
        _pressTagAnimationTimer = timer;
        timer.Tick += (_, _) =>
        {
            stepsDone++;
            SetPressTagWidth(startWidth + (endWidth - startWidth) * stepsDone / steps);

            if (stepsDone >= steps)
            {
                timer.Stop();
                timer.Dispose();
                if (_pressTagAnimationTimer == timer)
                    _pressTagAnimationTimer = null;
                SetPressTagWidth(endWidth);
                _pressTagVisible = show;
                _pressLabel.Visible = show;
            }
        };
        timer.Start();
    }

    // Sets the Press tag to an exact width, keeping the tab/card area
    // visually anchored in place — the window's left edge moves instead of
    // its right edge, so shrinking the tag reads as it retracting into the
    // main body rather than the whole window sliding sideways.
    private void SetPressTagWidth(int width)
    {
        // Computed fresh from the current right edge every time (rather than
        // applying a relative delta on top of whatever the last step left
        // behind) so this is self-correcting — 8 animation steps of relative
        // adjustment could accumulate a pixel or two of drift; this can't.
        int rightEdgeX = Location.X + ClientSize.Width;

        _pressTagWidth = width;
        _outer.ColumnStyles[1].Width = width;

        int newWindowWidth = ProfileTabWidth + width + _contentWidth;

        SuspendLayout();
        Location = new Point(rightEdgeX - newWindowWidth, Location.Y);
        ClientSize = new Size(newWindowWidth, ClientSize.Height);
        RecomputeRegion();
        ResumeLayout(true);
        Invalidate(true);
    }

    private void SelectTab(string word)
    {
        _openCategoryPopup?.Close();
        _openCategoryPopup = null;

        _selectedWord = word;
        _selectedTabExpanded = true;

        // Every word card only ever drops down under the action bar (to the
        // right of the Profile/Press notch) — but the Profile tab's own
        // button lives further left, in the notch itself, so its card needs
        // to stretch back to cover that same notch area instead. Otherwise
        // it reads as a dropdown disconnected from the button that opened it.
        if (word == ProfilesTabId)
        {
            _outer.SetColumn(_actionBarContent, 0);
            _outer.SetColumnSpan(_actionBarContent, 3);
        }
        else
        {
            _outer.SetColumn(_actionBarContent, 2);
            _outer.SetColumnSpan(_actionBarContent, 1);
        }

        foreach (var (w, card) in _cards)
            card.Visible = w == word;
        foreach (var (w, button) in _tabButtons)
            SetToggleAppearance(button, w == word);

        AdjustHeight();
    }

    // Refreshes the Profiles card's own highlighting after a switch — set by
    // MakeProfilesCard, since that list lives in its own closures.
    private Action? _refreshProfilesHighlight;

    // Loads a different profile's key map/behaviors and rebuilds all ten
    // word cards from scratch against it — "a full new set of ten numbered
    // cards to adjust freely". The old cards are disposed (not just hidden)
    // so their tap-counter timers actually stop. Stays on the Profiles tab
    // afterward rather than jumping to a numbered tab.
    private void SwitchToProfile(string profileName)
    {
        _openCategoryPopup?.Close();
        _openCategoryPopup = null;

        KeyMap.SwitchProfile(profileName);

        foreach (var word in KeyMap.RemappableWords)
        {
            var oldCard = _cards[word];
            _actionBarContent.Controls.Remove(oldCard);
            oldCard.Dispose();

            var newCard = MakeCard(word);
            newCard.Visible = false;
            _actionBarContent.Controls.Add(newCard);
            _cards[word] = newCard;
        }

        // Stay on the Profiles tab — clicking a profile shouldn't yank you
        // over to tab "one"; you just want to see it's now the active one
        // (via the refreshed highlight) and switch to a numbered tab
        // yourself whenever you're ready.
        _refreshProfilesHighlight?.Invoke();
    }

    // Grows/shrinks the window to fit however many accordion rows the
    // selected card currently has open — so Key/Repeat/Hold/Reset always stay
    // the same size, and each accordion adds height rather than taking it
    // from them. If the selected tab's card is collapsed, the window shrinks
    // to just the action bar, with no card showing at all.
    private void AdjustHeight()
    {
        int cardHeight;
        if (!_selectedTabExpanded || _selectedWord == null)
        {
            cardHeight = 0;
        }
        else
        {
            cardHeight = _cardHeight.TryGetValue(_selectedWord, out var value) ? value : BaseCardHeight;
        }
        var newSize = new Size(ClientSize.Width, TabStripHeight + cardHeight);
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

    // The Profiles card: same width/row-height as the word cards (it lives in
    // the same _actionBarContent, and every row uses ItemHeight, matching the
    // word cards' own rows). "Add Profile" is always first; clicking it
    // expands an inline accordion — a name field + confirm button — for
    // naming a new profile. Existing profiles list below it, one per row,
    // each a name button (click to switch to it) plus a delete "X" (except
    // Default, which can't be deleted). Rows grow/shrink the card exactly
    // like the word cards' own accordions do.
    private Control MakeProfilesCard()
    {
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

        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 1,
            BackColor = BackgroundColor,
        };
        card.Controls.Add(list);

        var existingNames = new HashSet<string>(Settings.LoadProfileNames());
        var profileRows = new List<TableLayoutPanel>();

        var addProfileButton = MakeListButton("Add Profile");
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
            BackColor = ButtonColor,
            ForeColor = AccentColor,
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
            BackColor = ButtonColor,
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
        var confirmButton = MakeTinyButton("✓");
        // Same idea as the field above: a plain centered checkmark glyph
        // still reads as sitting a bit low (the character's own metrics
        // leave more visual space above it than below), so padding the
        // bottom of its content area nudges the centered glyph upward —
        // roughly 30% of the row's own height (ItemHeight).
        confirmButton.Padding = new Padding(0, 0, 0, (int)(ItemHeight * 0.3));

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
            BackColor = ButtonColor,
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
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, ItemHeight));
                list.Controls.Add(rows[i], 0, i);

                // Same fix as the word cards' own list: each row's 1px
                // bottom margin is a spacer to the row after it, so the
                // last row needs it zeroed instead of leaving a 1px sliver
                // of black exposed below it, at the card's true bottom edge.
                var m = rows[i].Margin;
                rows[i].Margin = new Padding(m.Left, m.Top, m.Right, i == rows.Count - 1 ? 0 : 1);
            }
            list.ResumeLayout(true);
            ActiveControl = null;

            foreach (var row in profileRows)
                if (row.Controls.Count > 0 && row.Controls[0] is Button nameButton)
                    SetToggleAppearance(nameButton, nameButton.Text == KeyMap.ActiveProfile);

            _cardHeight[ProfilesTabId] = ItemHeight * rows.Count;
            if (ProfilesTabId == _selectedWord)
                AdjustHeight();
        }

        _refreshProfilesHighlight = RebuildProfilesList;

        TableLayoutPanel AddProfileRow(string name)
        {
            bool deletable = name != Settings.DefaultProfileName;

            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 1),
                ColumnCount = deletable ? 2 : 1,
                RowCount = 1,
                BackColor = BackgroundColor,
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, deletable ? 80 : 100));
            if (deletable)
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

            var nameButton = MakeListButton(name);
            nameButton.Click += (_, _) =>
            {
                // Picking a profile while the naming row is still open (from
                // an earlier "Add Profile" tap) should close it, same as
                // tapping "Add Profile" again would — RebuildProfilesList
                // runs as part of SwitchToProfile's own highlight refresh.
                namingExpanded = false;
                SwitchToProfile(name);
            };
            row.Controls.Add(nameButton, 0, 0);

            if (deletable)
            {
                // Tap once to arm (lights up), tap again within 2 seconds to
                // actually delete — same "confirm via a second tap" language
                // as the rest of the app, since this is destructive.
                var deleteButton = MakeTinyButton("✕");
                bool armed = false;
                var armTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                armTimer.Tick += (_, _) =>
                {
                    armed = false;
                    armTimer.Stop();
                    SetToggleAppearance(deleteButton, false);
                };
                deleteButton.Click += (_, _) =>
                {
                    if (!armed)
                    {
                        armed = true;
                        SetToggleAppearance(deleteButton, true);
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
                        SwitchToProfile(Settings.DefaultProfileName);
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
            var freshBehaviors = new Dictionary<string, KeyBehavior>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in KeyMap.RemappableWords)
                freshBehaviors[word] = new KeyBehavior();
            Settings.CreateProfileIfMissing(name, freshWords, freshBehaviors);

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
            Margin = new Padding(0),
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
            ActiveControl = null;

            int extraHeight = (keyExpanded ? categoryButtons.Count * ItemHeight : 0)
                + (repeatOn || holdOn ? AccordionHeight : 0)
                + (resetAllExpanded ? ItemHeight : 0);
            _cardHeight[word] = BaseCardHeight + extraHeight;
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

        // Editing the timer in any way — adding time or resetting it — means
        // you're moving away from infinite mode: turn the Infinite toggle
        // itself off (not just release whatever's currently engaged), so the
        // dashboard and the actual behavior agree with each other again.
        void DisengageInfinite()
        {
            if (infiniteOn)
            {
                infiniteOn = false;
                SetToggleAppearance(infiniteButton, false);
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
            durationLabel.Text = FormatDuration(duration);
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
                SetToggleAppearance(holdButton, false);
                ResetTimingForModeSwitch();
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
                ResetTimingForModeSwitch();
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
            DisengageInfinite();
            SaveBehavior();
        };
        plusTenth.Click += (_, _) =>
        {
            duration = Math.Round(duration + 0.1, 1);
            durationLabel.Text = FormatDuration(duration);
            DisengageInfinite();
            SaveBehavior();
        };
        resetDurationButton.Click += (_, _) =>
        {
            duration = 0.0;
            durationLabel.Text = FormatDuration(duration);
            DisengageInfinite();
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

        // Easter eggs on Reset — two independent sliding time windows, both
        // checked on every click:
        //  - 3 taps within 1.5s expands a "Reset All" row underneath (an
        //    accordion, same as Key/Repeat/Hold) with a button that resets
        //    every card, not just this one.
        //  - 2 taps within 1s brings back the Press tag if it's currently
        //    imploded (see _pressLabel's own tap-counter above).
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
            SetToggleAppearance(resetCardButton, true);
            ResetCard();

            if (resetTapCount == 3)
            {
                resetAllExpanded = true;
                SetToggleAppearance(resetCardButton, true);
                RebuildList();
            }

            if (PressTagEasterEggEnabled)
            {
                var now = DateTime.UtcNow;
                resetReappearClickTimes.Add(now);
                resetReappearClickTimes.RemoveAll(t => (now - t).TotalSeconds > 1.0);
                if (resetReappearClickTimes.Count >= 2)
                {
                    resetReappearClickTimes.Clear();
                    if (!_pressTagVisible)
                        AnimatePressTag(show: true);
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
            // Vertical-only: these stack in a single column, so left/right
            // margin only insets them from the card's edges (unwanted, since
            // the card should line up flush with the tab row's width) —
            // top/bottom still gives the thin gap between stacked buttons.
            Margin = new Padding(0, 1, 0, 1),
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
            ClientSize = new Size(_contentWidth, keys.Length * rowHeight),
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
