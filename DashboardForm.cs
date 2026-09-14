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
    private const int TabStripHeight = 60;
    private const int PressTagWidth = 100;
    private const int ProfileTabWidth = 70;
    private const int InitialCardWidth = 370;
    private const int BaseCardHeight = 324;
    private const int ItemHeight = BaseCardHeight / 4;
    private const int AccordionHeight = 56;

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
    private ActionBar _actionBar;
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
    // only one popup should ever be open, at a time. _openCategoryPopupAnchor
    // is whichever category button opened it, so the popup can be
    // re-positioned relative to it if the dashboard itself moves (see
    // RepositionCategoryPopup, wired to LocationChanged in the constructor)
    // — otherwise dragging the listener icon would leave the popup behind,
    // no longer attached to the card it came from.
    private Form? _openCategoryPopup;
    private Control? _openCategoryPopupAnchor;

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
        BackColor = Theme.Current.Background;
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
            BackColor = Theme.Current.Background,
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
            BackColor = Theme.Current.Button,
            ForeColor = Theme.Current.Accent,
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

        _actionBar = new ActionBar(InitialCardWidth / KeyMap.RemappableWords.Length, EnsureContentWidth);

        // All tabs' content lives in the same spot, stacked on top of each
        // other — only the selected one is visible at a time.
        _actionBarContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Current.Background };

        // AddActionBarTab inserts at the left end of the bar, so this builds
        // the initial ten in reverse (10 down to 1) — each new insert at
        // index 0 pushes the previous ones right, leaving them in the
        // correct 1..10 left-to-right order once the loop finishes.
        var words = KeyMap.RemappableWords;
        for (int i = words.Length - 1; i >= 0; i--)
        {
            var wordTab = new WordCardTab(words[i]);
            AddActionBarTab(wordTab.Id, wordTab.Label, wordTab.BuildContent(MakeTabContext(wordTab.Id)));
        }

        // Profile gets its own fixed slot to the left of Press, instead of
        // living in the action bar with the word tabs — it goes through the
        // same MakeTabButton wiring as every other tab (so switching to it,
        // highlighting it, etc. all work identically), it's just placed in
        // its own outer-grid column rather than inserted into _actionBar.
        // The numbered tabs' widths, order, and position are untouched.
        var profilesTab = new ProfilesTab();
        var profileButton = MakeTabButton(profilesTab.Id, profilesTab.Label, profilesTab.BuildContent(MakeTabContext(profilesTab.Id)));
        profileButton.Margin = new Padding(0);

        _outer.Controls.Add(profileButton, 0, 0);
        _outer.Controls.Add(_pressLabel, 1, 0);
        _outer.Controls.Add(_actionBar.Control, 2, 0);
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

        // Keeps an open category popup glued to its card whenever this
        // window itself moves (e.g. while the listener icon is being
        // dragged, which drags the dashboard along with it).
        LocationChanged += (_, _) => RepositionCategoryPopup();
    }

    // Builds one tab's button and wires up its click behavior (toggle open/
    // closed if it's already the active tab, otherwise switch to it), and
    // registers its content in the shared display area. Shared by every
    // tab regardless of where its button ends up living — the action bar,
    // or the Profile tab's own fixed slot to the left of Press.
    private Button MakeTabButton(string id, string label, Control content)
    {
        var tabButton = Theme.MakeTinyButton(label);
        tabButton.Margin = new Padding(0);
        tabButton.Padding = new Padding(0);
        tabButton.Font = new Font("Segoe UI", 9f);
        tabButton.Click += (_, _) =>
        {
            // Clicking the already-active tab toggles its card open/closed;
            // clicking a different tab always opens (SelectTab handles that).
            if (_selectedWord == id)
            {
                // SelectTab already closes this when switching tabs, but
                // collapsing the current tab's own card is handled here
                // instead — without this, the popup was left floating on
                // screen with no card left open underneath it.
                _openCategoryPopup?.Close();
                _openCategoryPopup = null;

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
        _actionBar.Add(id, tabButton, width);
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
        bool profileCardOpen = _selectedWord == ProfilesTab.TabId && _selectedTabExpanded;
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
        if (word == ProfilesTab.TabId)
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
            Theme.SetToggleAppearance(button, w == word);

        AdjustHeight();
    }

    // Refreshes the Profiles tab's own highlighting after a switch —
    // registered by ProfilesTab itself via DashboardTabContext.OnProfileSwitched.
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

            var newCard = new WordCardTab(word).BuildContent(MakeTabContext(word));
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

    // Builds the small set of callbacks a tab (see IDashboardTab) gets
    // instead of reaching into this class's private fields directly. Bound
    // to a specific tabId, so ReportHeight always resizes the right card.
    private DashboardTabContext MakeTabContext(string tabId) => new()
    {
        ItemHeight = ItemHeight,
        ReportHeight = height =>
        {
            _cardHeight[tabId] = height;
            if (tabId == _selectedWord)
                AdjustHeight();
        },
        ClearFocus = () => ActiveControl = null,
        SwitchToProfile = SwitchToProfile,
        OnProfileSwitched = listener => _refreshProfilesHighlight = listener,
        AccordionHeight = AccordionHeight,
        BaseCardHeight = BaseCardHeight,
        ShowCategoryPopup = (anchor, keys, onSelect) =>
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = CategoryKeyPopup.Show(this, anchor, _contentWidth, keys, onSelect);
            _openCategoryPopupAnchor = anchor;
        },
        CloseCategoryPopup = () =>
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;
        },
        RegisterResetAction = action => _resetCardActions[tabId] = action,
        ResetAllCards = () =>
        {
            foreach (var reset in _resetCardActions.Values)
                reset();
        },
        PressTagEasterEggEnabled = PressTagEasterEggEnabled,
        ShowPressTagIfHidden = () =>
        {
            if (!_pressTagVisible)
                AnimatePressTag(show: true);
        },
    };

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

    // Called whenever this window moves (see LocationChanged in the
    // constructor). Without this, dragging the listener icon around would
    // drag the dashboard along with it but leave any open category popup
    // stranded at its old position, disconnected from the card it belongs to.
    private void RepositionCategoryPopup()
    {
        if (_openCategoryPopup != null && _openCategoryPopupAnchor != null)
            CategoryKeyPopup.Reposition(_openCategoryPopup, _openCategoryPopupAnchor);
    }

    // Recolors menu hover/selection/border chrome to match the black-and-red
    // theme (the BackColor/ForeColor set on the menu items only covers the
    // text and idle background, not the built-in blue hover highlight).
    private sealed class DarkRedColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Theme.Current.Hover;
        public override Color MenuItemSelectedGradientBegin => Theme.Current.Hover;
        public override Color MenuItemSelectedGradientEnd => Theme.Current.Hover;
        public override Color MenuItemBorder => Theme.Current.Hover;
        public override Color MenuBorder => Theme.Current.Button;
        public override Color ToolStripBorder => Theme.Current.Button;
        public override Color ImageMarginGradientBegin => Theme.Current.Button;
        public override Color ImageMarginGradientMiddle => Theme.Current.Button;
        public override Color ImageMarginGradientEnd => Theme.Current.Button;
        public override Color SeparatorDark => Theme.Current.Accent;
        public override Color SeparatorLight => Theme.Current.Accent;
    }
}
