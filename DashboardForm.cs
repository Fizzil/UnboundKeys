namespace VoicePress;

// The right-click dashboard. This window is a fixed size from the moment
// it's created and never resizes or repositions itself — Press, Mouse, and
// Profile sit at permanent coordinates within it, and opening one of them
// just reveals a pre-allocated area that was already part of the window the
// whole time (previously invisible/unclickable via the window's Region),
// rather than growing the window into existence. That's what keeps the
// three buttons from ever "jumping": there's no resize-and-reposition dance
// left to get wrong.
//
// Layout, left to right, all fixed:
//   x:[0, DrawerWidth)                     — Voice's ten tabs, Physical's ten, or Mouse's six
//   x:[DrawerWidth, DrawerWidth+FixedGroupWidth) — Press / Mouse / Profile buttons
//   x:[DrawerWidth+FixedGroupWidth, MaxWidth)    — Profile's dropdown's extra reach
// Only one of {Voice, Physical, Mouse}'s drawer, Press's own Voice/Physical
// selector (all left zone), or Profile's dropdown (right zone, which also
// includes the space under the three buttons) is ever revealed at a time —
// see RecomputeRegion. Press itself has no drawer of its own: tapping it
// opens the selector, and picking Voice or Physical there is really just
// opening a different prime tab (see TogglePrimeTab) — Press's button
// stays highlighted for all three states, since visually there's still
// only one button representing them.
public sealed class DashboardForm : Form
{
    private const int TabStripHeight = 60;
    private const int ProfileTabWidth = 70;
    private const int MouseTabWidth = 70;
    private const int PressTabWidth = 100;
    private const int FixedGroupWidth = PressTabWidth + MouseTabWidth + ProfileTabWidth;

    // The left zone: Voice's ten tabs, Physical's ten, and Mouse's six all
    // render within this same fixed width, so opening any one of them
    // always reveals exactly the same amount of space.
    private const int DrawerWidth = 370;

    // The right zone, past the fixed group: exactly as wide as the listener
    // icon itself, so Profile's dropdown (fixed group + this) reaches
    // precisely to the icon's own far edge.
    private const int ProfileExtraWidth = OverlayForm.TargetWidth;

    private const int MaxWidth = DrawerWidth + FixedGroupWidth + ProfileExtraWidth;

    // How far left of the listener icon's left edge this window's own left
    // edge sits — a fixed relationship (the window never resizes, so this
    // never needs recomputing). OverlayForm's RepositionDashboard uses this
    // instead of this window's Width, since Width no longer reflects "how
    // far left the icon-adjacent content reaches" the way it used to.
    public const int LeftEdgeOffsetFromIcon = DrawerWidth + FixedGroupWidth;

    private const int BaseCardHeight = 324;
    private const int ItemHeight = BaseCardHeight / 4;
    private const int AccordionHeight = 56;

    // Two stacked list-style rows (Voice Press / Physical Press) — the
    // selector's content never varies, so unlike every other card here it
    // doesn't need the ReportHeight/_cardHeight machinery, just this fixed
    // value (see CurrentCardHeight).
    private const int PressSelectorHeight = ItemHeight * 2;

    // The dashboard has no outer padding — its visible content starts
    // exactly at its own window edges — so there's no offset for
    // OverlayForm to account for when lining the two windows up vertically.
    public const int TopInset = 0;

    private Button _profileButton;
    private Button _mouseToggleButton;
    private Button _pressToggleButton;
    private Button _voicePressButton;
    private Button _physicalPressButton;

    private Panel _pressDrawer;
    private Panel _physicalDrawer;
    private Panel _mouseDrawer;
    private Panel _profileDropdown;
    private Panel _pressSelectorPanel;

    private ActionBar _actionBar;
    private Panel _actionBarContent;
    private ActionBar _physicalActionBar;
    private Panel _physicalActionBarContent;
    private TableLayoutPanel _mouseStrip;
    private Panel _mouseStripContent;
    private Panel _profileContent;

    // Which of "profile"/"mouse"/"voice"/"physical"/"pressSelector" (if
    // any) currently has its area revealed — independent of _selectedWord
    // below, which is whichever specific sub-tab inside Voice's, Physical's,
    // or Mouse's drawer is showing a card.
    private string? _activePrimeTab;

    private readonly Dictionary<string, Control> _cards = new();
    private readonly Dictionary<string, Button> _tabButtons = new();

    // The full height each sub-tab's card currently needs. The word/mouse-
    // button cards use BaseCardHeight plus whatever accordions (Key's
    // categories, Repeat/Hold's timing row) are open; the Profiles card
    // computes its own height from however many rows it has.
    private readonly Dictionary<string, int> _cardHeight = new();
    private string? _selectedWord;

    // Whether the currently selected sub-tab's card is showing. Clicking a
    // sub-tab that's already active toggles this; clicking a different one
    // always sets it back to true (SelectTab).
    private bool _selectedTabExpanded = true;

    // The currently-open category key-list popup (from any card's Key
    // accordion) — only one should ever be open at a time.
    // _openCategoryPopupAnchor is whichever category button opened it, so
    // the popup can be re-positioned relative to it if the dashboard itself
    // moves (see RepositionCategoryPopup) — otherwise dragging the listener
    // icon would leave the popup behind, no longer attached to its card.
    private Form? _openCategoryPopup;
    private Control? _openCategoryPopupAnchor;

    // Each card's own "reset everything about this card" logic, so the
    // triple-tap "Reset All" easter egg can run every card's reset at once —
    // including ones that aren't currently visible.
    private readonly Dictionary<string, Action> _resetCardActions = new();

    // Refreshes the Profiles list's own highlighting after a switch —
    // registered by ProfilesTab itself via DashboardTabContext.OnProfileSwitched.
    private Action? _refreshProfilesHighlight;

    static DashboardForm()
    {
        // Applies to every menu the dashboard pops up (including submenus),
        // so the key-picker dropdowns match the black-and-red theme too.
        ToolStripManager.Renderer = new ToolStripProfessionalRenderer(new DarkRedColorTable());
    }

    // Windows erases a control's background to the default (white) just
    // before repainting it — normally invisible because it happens between
    // frames, but resizing this window during an accordion expand/collapse
    // makes that blank frame visible as a flash. Telling Windows "already
    // handled" skips that erase; our own painting (solid BackColor) covers
    // the same area anyway.
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETREDRAW = 0x000B;

    // SuspendLayout only pauses .NET's own layout engine — it does nothing
    // to stop Windows from actually painting in between the several
    // Region/Visible/size changes TogglePrimeTab makes, which is what was
    // showing up as a brief white flash across the fixed-group buttons
    // (and everything else) on every prime-tab switch. WM_SETREDRAW tells
    // Windows itself to stop flushing this window to the screen at all
    // until EndScreenUpdate turns it back on and forces one clean repaint —
    // the standard fix for exactly this kind of multi-step-change flicker.
    // Reference-counted so these safely nest — e.g. TogglePrimeTab calls
    // SelectTab, which calls AdjustHeight, and each wraps its own work in
    // this same pair. Only the outermost Begin actually freezes the window,
    // and only the matching outermost End thaws and repaints it; an inner
    // End firing early would have re-enabled painting while the outer
    // caller's own changes were still mid-flight.
    private int _screenUpdateDepth = 0;

    private void BeginScreenUpdate()
    {
        if (_screenUpdateDepth == 0)
            SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        _screenUpdateDepth++;
    }

    private void EndScreenUpdate()
    {
        _screenUpdateDepth--;
        if (_screenUpdateDepth == 0)
        {
            SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            Invalidate(true);
            Update();
        }
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

        // Fixed for the entire lifetime of this window — see the class
        // comment. Only ever the height changes afterward (AdjustHeight),
        // never the width, and Location is set once by OverlayForm and
        // otherwise only moves when the icon itself is dragged.
        ClientSize = new Size(MaxWidth, TabStripHeight);

        // --- The fixed group: Press, Mouse, Profile, left to right,
        // always at x:[DrawerWidth, DrawerWidth+FixedGroupWidth). ---
        var fixedGroup = new TableLayoutPanel
        {
            Location = new Point(DrawerWidth, 0),
            Size = new Size(FixedGroupWidth, TabStripHeight),
            Margin = new Padding(0),
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };
        fixedGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, PressTabWidth));
        fixedGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, MouseTabWidth));
        fixedGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ProfileTabWidth));

        _pressToggleButton = Theme.MakeTinyButton("Press");
        _pressToggleButton.Margin = new Padding(0);
        _pressToggleButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_pressToggleButton);
        _pressToggleButton.Click += (_, _) => TogglePrimeTab("pressSelector");

        _mouseToggleButton = Theme.MakeTinyButton("Mouse");
        _mouseToggleButton.Margin = new Padding(0);
        _mouseToggleButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_mouseToggleButton);
        _mouseToggleButton.Click += (_, _) => TogglePrimeTab("mouse");

        _profileButton = Theme.MakeTinyButton("Profile");
        _profileButton.Margin = new Padding(0);
        _profileButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_profileButton);
        _profileButton.Click += (_, _) => TogglePrimeTab("profile");

        fixedGroup.Controls.Add(_pressToggleButton, 0, 0);
        fixedGroup.Controls.Add(_mouseToggleButton, 1, 0);
        fixedGroup.Controls.Add(_profileButton, 2, 0);

        // --- Voice's drawer: the ten numbered word tabs, plus whichever of
        // their cards is currently selected. Fixed at x:[0, DrawerWidth). ---
        _actionBar = new ActionBar(DrawerWidth / KeyMap.RemappableWords.Length);
        _actionBarContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Current.Background };

        // Press sits at the LEFT of the fixed group, immediately next to
        // this drawer's right edge — so "one" needs to end up rightmost
        // (closest to Press) and "ten" leftmost. Inserted in forward order
        // (1 through 10) since ActionBar.Add always inserts at the left
        // end — each insert-at-0 pushes the previous ones further right,
        // leaving "one" as the last (rightmost) one in.
        var words = KeyMap.RemappableWords;
        for (int i = 0; i < words.Length; i++)
        {
            var wordTab = new RemapCardTab(KeyMapSource.Instance, words[i], (i + 1).ToString());
            var tabButton = MakeSubTabButton(wordTab.Id, wordTab.Label, wordTab.BuildContent(MakeTabContext(wordTab.Id)), _actionBarContent);
            _actionBar.Add(wordTab.Id, tabButton);
        }

        _pressDrawer = BuildDrawerShell(_actionBar.Control, _actionBarContent);
        _pressDrawer.Location = new Point(0, 0);
        _pressDrawer.Size = new Size(DrawerWidth, TabStripHeight);

        // --- Physical's drawer: the ten physical number-row keys, same
        // shape and ordering convention as Voice's drawer above (so
        // switching between the two doesn't also flip which end "1" sits
        // at). Also x:[0, DrawerWidth). ---
        _physicalActionBar = new ActionBar(DrawerWidth / PhysicalKeyCatalog.Keys.Length);
        _physicalActionBarContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Current.Background };

        var physicalKeys = PhysicalKeyCatalog.Keys;
        for (int i = 0; i < physicalKeys.Length; i++)
        {
            var physTab = new RemapCardTab(PhysicalKeyMapSource.Instance, physicalKeys[i].Id, physicalKeys[i].ShortLabel);
            var tabButton = MakeSubTabButton(physTab.Id, physTab.Label, physTab.BuildContent(MakeTabContext(physTab.Id)), _physicalActionBarContent);
            _physicalActionBar.Add(physTab.Id, tabButton);
        }

        _physicalDrawer = BuildDrawerShell(_physicalActionBar.Control, _physicalActionBarContent);
        _physicalDrawer.Location = new Point(0, 0);
        _physicalDrawer.Size = new Size(DrawerWidth, TabStripHeight);

        // --- Mouse's drawer: the six remappable-button tabs, plus whichever
        // of their cards is currently selected. Also x:[0, DrawerWidth). ---
        _mouseStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = MouseCatalog.Buttons.Length,
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };
        _mouseStripContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Current.Background };

        for (int i = 0; i < MouseCatalog.Buttons.Length; i++)
        {
            var buttonInfo = MouseCatalog.Buttons[i];
            // Percent, not Absolute, so the six buttons stretch to fill
            // DrawerWidth evenly rather than leaving a leftover sliver
            // (360 doesn't divide DrawerWidth's 370 cleanly).
            _mouseStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / MouseCatalog.Buttons.Length));

            var mouseTab = new RemapCardTab(MouseMapSource.Instance, buttonInfo.Id, buttonInfo.ShortLabel);
            var tabButton = MakeSubTabButton(mouseTab.Id, mouseTab.Label, mouseTab.BuildContent(MakeTabContext(mouseTab.Id)), _mouseStripContent);
            tabButton.Margin = new Padding(0);
            new ToolTip().SetToolTip(tabButton, buttonInfo.Label);
            _mouseStrip.Controls.Add(tabButton, i, 0);
        }

        _mouseDrawer = BuildDrawerShell(_mouseStrip, _mouseStripContent);
        _mouseDrawer.Location = new Point(0, 0);
        _mouseDrawer.Size = new Size(DrawerWidth, TabStripHeight);

        // --- Profile's dropdown: just its list, no sub-tabs of its own.
        // Fixed at x:[DrawerWidth, MaxWidth) — under the fixed group AND the
        // extra reach past it — starting at y:TabStripHeight since the
        // fixed group's own buttons already occupy that row at this x-range. ---
        var profilesTab = new ProfilesTab();
        _profileContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Current.Background };
        var profileCard = profilesTab.BuildContent(MakeTabContext(profilesTab.Id));
        profileCard.Visible = false;
        _profileContent.Controls.Add(profileCard);
        _cards[profilesTab.Id] = profileCard;

        _profileDropdown = new Panel
        {
            Location = new Point(DrawerWidth, TabStripHeight),
            Size = new Size(FixedGroupWidth + ProfileExtraWidth, 0),
            Visible = false,
            BackColor = Theme.Current.Background,
        };
        _profileDropdown.Controls.Add(_profileContent);

        // --- Press's own selector: just two buttons, no sub-tabs of its
        // own (same situation as Profile above) — picking one opens the
        // Voice or Physical drawer instead (see TogglePrimeTab), and also
        // switches which one is the active Press source (see PressMode).
        // Drops down directly below Press itself, same positioning as
        // Profile's own dropdown below Profile — x:[DrawerWidth, MaxWidth),
        // the same right zone Profile's dropdown uses (safe to share since
        // the two are never shown at the same time). ---
        _voicePressButton = Theme.MakeListButton("Voice Press");
        Theme.EnableTabUnderline(_voicePressButton);
        _voicePressButton.Click += (_, _) =>
        {
            PressMode.SwitchTo("voice");
            RefreshPressModeHighlight();
            TogglePrimeTab("voice");
        };
        _physicalPressButton = Theme.MakeListButton("Physical Press");
        Theme.EnableTabUnderline(_physicalPressButton);
        _physicalPressButton.Click += (_, _) =>
        {
            PressMode.SwitchTo("physical");
            RefreshPressModeHighlight();
            TogglePrimeTab("physical");
        };

        var pressSelectorList = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Current.Background,
        };
        pressSelectorList.RowStyles.Add(new RowStyle(SizeType.Absolute, ItemHeight));
        pressSelectorList.RowStyles.Add(new RowStyle(SizeType.Absolute, ItemHeight));
        pressSelectorList.Controls.Add(_voicePressButton, 0, 0);
        pressSelectorList.Controls.Add(_physicalPressButton, 0, 1);

        _pressSelectorPanel = new Panel
        {
            Location = new Point(DrawerWidth, TabStripHeight),
            Size = new Size(FixedGroupWidth + ProfileExtraWidth, 0),
            Visible = false,
            BackColor = Theme.Current.Background,
        };
        _pressSelectorPanel.Controls.Add(pressSelectorList);

        // --- Assemble. Z-order doesn't matter here for click purposes —
        // none of these x-ranges ever overlap — but the fixed group is
        // added last/frontmost purely so its buttons are never visually
        // clipped by anything. ---
        _pressDrawer.Visible = false;
        _physicalDrawer.Visible = false;
        _mouseDrawer.Visible = false;
        Controls.Add(_pressDrawer);
        Controls.Add(_physicalDrawer);
        Controls.Add(_mouseDrawer);
        Controls.Add(_profileDropdown);
        Controls.Add(_pressSelectorPanel);
        Controls.Add(fixedGroup);

        RecomputeRegion();

        // The fixed group's buttons are among the first controls added, so
        // one of them would otherwise be the implicit default ActiveControl
        // — clicking the separate overlay icon shifts window activation
        // away and back even though it never takes real focus, and that
        // activation blip was enough to make Windows paint a focus cue on
        // it. Clearing ActiveControl on both transitions means there's
        // never a control left to draw one on.
        Activated += (_, _) => ActiveControl = null;
        Deactivate += (_, _) => ActiveControl = null;

        // Keeps an open category popup glued to its card whenever this
        // window itself moves (e.g. while the listener icon is being
        // dragged, which drags the dashboard along with it).
        LocationChanged += (_, _) => RepositionCategoryPopup();
    }

    // A drawer is always the same shape: a fixed-height sub-tab-strip row on
    // top, and whatever's currently selected within it below.
    private static Panel BuildDrawerShell(Control tabStrip, Control content)
    {
        var shell = new TableLayoutPanel
        {
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Current.Background,
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, TabStripHeight));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(tabStrip, 0, 0);
        shell.Controls.Add(content, 0, 1);
        return shell;
    }

    // Builds one sub-tab's button (a numbered word, a physical key, or a
    // mouse button) and wires up its click behavior: toggle its own card
    // open/closed if it's already the active one, otherwise switch to it.
    // Shared by all three drawers that have sub-tabs at all — Profile and
    // Press's own selector don't, since neither has anything further to
    // pick once its own area is open.
    private Button MakeSubTabButton(string id, string label, Control content, Panel targetContent)
    {
        var tabButton = Theme.MakeTinyButton(label);
        tabButton.Margin = new Padding(0);
        tabButton.Padding = new Padding(0);
        tabButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(tabButton);
        tabButton.Click += (_, _) =>
        {
            if (_selectedWord == id)
            {
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
        targetContent.Controls.Add(content);
        _cards[id] = content;

        return tabButton;
    }

    private Panel GetDrawer(string primeTab) => primeTab switch
    {
        "mouse" => _mouseDrawer,
        "voice" => _pressDrawer,
        "physical" => _physicalDrawer,
        _ => throw new ArgumentOutOfRangeException(nameof(primeTab)),
    };

    // Highlights whichever of Voice Press/Physical Press is currently the
    // active Press source (see PressMode) — called whenever the selector
    // is about to show, and right after either button switches the mode,
    // so the just-picked one lights up immediately rather than waiting for
    // the selector to be reopened.
    private void RefreshPressModeHighlight()
    {
        Theme.SetTabSelected(_voicePressButton, PressMode.Active == "voice");
        Theme.SetTabSelected(_physicalPressButton, PressMode.Active == "physical");
    }

    private Button GetPrimeButton(string primeTab) => primeTab switch
    {
        "profile" => _profileButton,
        "mouse" => _mouseToggleButton,
        "voice" or "physical" or "pressSelector" => _pressToggleButton,
        _ => throw new ArgumentOutOfRangeException(nameof(primeTab)),
    };

    // Opens/closes one of the prime tabs. Clicking the one that's already
    // open collapses it back down to nothing; clicking a different one
    // swaps it in, closing whichever was open first — only one is ever
    // open at a time. This never touches the window's own Location or
    // Width — only which pre-positioned area is Visible, and the Region
    // that makes the rest of it non-existent rather than just an empty
    // black rectangle. See the class comment for why.
    //
    // "voice"/"physical"/"pressSelector" are three separate prime-tab keys
    // even though there's only one physical button (Press) behind all of
    // them — that's deliberate, not extra machinery. Press's own click
    // handler always calls TogglePrimeTab("pressSelector"); the selector's
    // two buttons call TogglePrimeTab("voice")/("physical"). Since those
    // are three different keys, switching between any two of them is
    // always an ordinary "close old, open new" — including going from
    // Voice or Physical's drawer back to the selector on a second Press
    // tap. A third Press tap, from the selector, finally matches itself
    // and falls into the existing close branch below. No separate state
    // machine needed for the extra toggle level Press has that Mouse and
    // Profile don't.
    private void TogglePrimeTab(string primeTab)
    {
        bool wasOpen = _activePrimeTab == primeTab;

        BeginScreenUpdate();
        SuspendLayout();
        try
        {
            if (_activePrimeTab != null)
            {
                _openCategoryPopup?.Close();
                _openCategoryPopup = null;
                _selectedWord = null;
                _selectedTabExpanded = false;
                foreach (var (_, card) in _cards)
                    card.Visible = false;
                foreach (var (_, button) in _tabButtons)
                    Theme.SetTabSelected(button, false);
                Theme.SetTabSelected(GetPrimeButton(_activePrimeTab), false);

                if (_activePrimeTab == "profile")
                    _profileDropdown.Visible = false;
                else if (_activePrimeTab == "pressSelector")
                    _pressSelectorPanel.Visible = false;
                else
                    GetDrawer(_activePrimeTab).Visible = false;
            }

            _activePrimeTab = wasOpen ? null : primeTab;

            if (_activePrimeTab == "profile")
            {
                Theme.SetTabSelected(_profileButton, true);
                _profileDropdown.Visible = true;
                // Profile has no further sub-tab to pick — its content
                // shows immediately. SelectTab also calls AdjustHeight.
                SelectTab(ProfilesTab.TabId);
            }
            else if (_activePrimeTab == "pressSelector")
            {
                // Same situation as Profile above — a small fixed list
                // with nothing further to pick — except its height never
                // varies, so it skips SelectTab/_cardHeight entirely (see
                // CurrentCardHeight).
                Theme.SetTabSelected(_pressToggleButton, true);
                RefreshPressModeHighlight();
                _pressSelectorPanel.Visible = true;
                AdjustHeight();
            }
            else if (_activePrimeTab != null)
            {
                GetDrawer(_activePrimeTab).Visible = true;
                Theme.SetTabSelected(GetPrimeButton(_activePrimeTab), true);
                AdjustHeight();
            }
            else
            {
                AdjustHeight();
            }

            RecomputeRegion();
        }
        finally
        {
            ResumeLayout(true);
            EndScreenUpdate();
        }
    }

    private void SelectTab(string word)
    {
        _openCategoryPopup?.Close();
        _openCategoryPopup = null;

        _selectedWord = word;
        _selectedTabExpanded = true;

        foreach (var (w, card) in _cards)
            card.Visible = w == word;
        foreach (var (w, button) in _tabButtons)
            Theme.SetTabSelected(button, w == word);

        AdjustHeight();
    }

    // Loads a different profile's key map/behaviors and rebuilds all ten
    // word cards, all ten physical-key cards, and all six mouse-button
    // cards, from scratch against it — a full new set to adjust freely.
    // The old cards are disposed (not just hidden) so their tap-counter
    // timers actually stop. Stays on the Profiles dropdown afterward
    // rather than jumping to a numbered tab.
    private void SwitchToProfile(string profileName)
    {
        // Twenty-six cards get torn down and rebuilt below — without
        // freezing the screen for the duration, that was flashing visibly.
        BeginScreenUpdate();
        try
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;

            KeyMap.SwitchProfile(profileName);
            MouseMap.SwitchProfile(profileName);
            PhysicalKeyMap.SwitchProfile(profileName);
            PressMode.SwitchProfile(profileName);
            RefreshPressModeHighlight();

            foreach (var word in KeyMap.RemappableWords)
            {
                var oldCard = _cards[word];
                _actionBarContent.Controls.Remove(oldCard);
                oldCard.Dispose();

                var label = (Array.IndexOf(KeyMap.RemappableWords, word) + 1).ToString();
                var newCard = new RemapCardTab(KeyMapSource.Instance, word, label).BuildContent(MakeTabContext(word));
                newCard.Visible = false;
                _actionBarContent.Controls.Add(newCard);
                _cards[word] = newCard;
            }

            foreach (var key in PhysicalKeyCatalog.Keys)
            {
                var oldCard = _cards[key.Id];
                _physicalActionBarContent.Controls.Remove(oldCard);
                oldCard.Dispose();

                var newCard = new RemapCardTab(PhysicalKeyMapSource.Instance, key.Id, key.ShortLabel).BuildContent(MakeTabContext(key.Id));
                newCard.Visible = false;
                _physicalActionBarContent.Controls.Add(newCard);
                _cards[key.Id] = newCard;
            }

            foreach (var button in MouseCatalog.Buttons)
            {
                var oldCard = _cards[button.Id];
                _mouseStripContent.Controls.Remove(oldCard);
                oldCard.Dispose();

                var newCard = new RemapCardTab(MouseMapSource.Instance, button.Id, button.ShortLabel).BuildContent(MakeTabContext(button.Id));
                newCard.Visible = false;
                _mouseStripContent.Controls.Add(newCard);
                _cards[button.Id] = newCard;
            }

            _refreshProfilesHighlight?.Invoke();
        }
        finally
        {
            EndScreenUpdate();
        }
    }

    // Builds the small set of callbacks a tab (see IDashboardTab) gets
    // instead of reaching into this class's private fields directly. Bound
    // to a specific tabId, so ReportHeight always resizes against the right
    // card's own stored height.
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
            _openCategoryPopup = CategoryKeyPopup.Show(this, anchor, DrawerWidth, keys, onSelect);
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
        BeginScreenUpdate = BeginScreenUpdate,
        EndScreenUpdate = EndScreenUpdate,
    };

    // The height the currently-selected sub-tab's card actually needs right
    // now (0 if nothing's selected/expanded) — the one authoritative source
    // for window height.
    private int CurrentCardHeight()
    {
        // The selector's height never varies (just its two fixed rows), so
        // unlike every other prime tab it doesn't go through
        // _selectedWord/_cardHeight/ReportHeight at all.
        if (_activePrimeTab == "pressSelector")
            return PressSelectorHeight;

        if (!_selectedTabExpanded || _selectedWord == null)
            return 0;

        return _cardHeight.TryGetValue(_selectedWord, out var value) ? value : BaseCardHeight;
    }

    // Grows/shrinks the window (height only — width is fixed for good, see
    // the class comment) to fit however tall the selected sub-tab's card
    // currently is, and keeps whichever of _pressDrawer/_physicalDrawer/
    // _mouseDrawer/_profileDropdown/_pressSelectorPanel is actually active
    // in sync with that same height — the others don't matter since
    // RecomputeRegion hides them anyway, but leaving their height stale
    // would show through if the active one ever changed without a height
    // change accompanying it.
    private void AdjustHeight()
    {
        BeginScreenUpdate();
        try
        {
            int cardHeight = CurrentCardHeight();
            int fullHeight = TabStripHeight + cardHeight;

            if (_activePrimeTab == "profile")
                _profileDropdown.Height = cardHeight;
            else if (_activePrimeTab == "pressSelector")
                _pressSelectorPanel.Height = cardHeight;
            else if (_activePrimeTab != null)
                GetDrawer(_activePrimeTab).Height = fullHeight;

            var newSize = new Size(ClientSize.Width, fullHeight);
            if (ClientSize != newSize)
            {
                SuspendLayout();
                ClientSize = newSize;
                RecomputeRegion();
                ResumeLayout(true);
            }
        }
        finally
        {
            // EndScreenUpdate's own Invalidate+Update stands in for what
            // used to be this method's own — still needed for the same
            // reason (clearing a stray leftover fragment of the card's red
            // border after a resize), just done once by whichever call in
            // the current nest is outermost instead of by every level.
            EndScreenUpdate();
        }
    }

    // Cuts away whichever of the three zones (see the class comment) isn't
    // currently relevant, so it's genuinely not there — not just painted
    // black — matching how the window behaved before it grew a fixed
    // maximum size: nothing to click, nothing extra to see.
    private void RecomputeRegion()
    {
        var shape = new Region(new Rectangle(Point.Empty, ClientSize));

        // The tab-strip row (y: 0 to TabStripHeight) and the card-content
        // row beneath it (y: TabStripHeight onward) need different
        // treatment — the fixed group's buttons live in the first and
        // should always show there, but nothing of theirs lives in the
        // second, so leaving it un-excluded was showing as a plain black
        // slab under the buttons whenever a card below the drawer was
        // taller than the tab strip alone.
        int contentRowHeight = ClientSize.Height - TabStripHeight;

        if (_activePrimeTab is "voice" or "physical" or "mouse")
        {
            // Row 0: the drawer's own sub-tab-strip plus the fixed group
            // both stay; only the profile-extra zone (where the listener
            // icon sits) is cut.
            shape.Exclude(new Rectangle(DrawerWidth + FixedGroupWidth, 0, ProfileExtraWidth, TabStripHeight));

            // Row 1: only the drawer's own width is ever used here — the
            // fixed group and profile-extra zone have nothing under them
            // in this state, so both get cut for the card row's full height.
            if (contentRowHeight > 0)
                shape.Exclude(new Rectangle(DrawerWidth, TabStripHeight, ClientSize.Width - DrawerWidth, contentRowHeight));
        }
        else if (_activePrimeTab is "profile" or "pressSelector")
        {
            // Row 0: fixed group stays, the drawer's own zone doesn't; the
            // small square where the listener icon sits gets cut too, or
            // the window's plain background paints over the icon instead
            // of letting it show through.
            shape.Exclude(new Rectangle(0, 0, DrawerWidth, TabStripHeight));
            shape.Exclude(new Rectangle(DrawerWidth + FixedGroupWidth, 0, ProfileExtraWidth, TabStripHeight));

            // Row 1: Profile's dropdown (or Press's own selector — same
            // geometry, same reasoning) spans the fixed group's width plus
            // the extra reach past it; the drawer's own zone still isn't
            // used here either.
            if (contentRowHeight > 0)
                shape.Exclude(new Rectangle(0, TabStripHeight, DrawerWidth, contentRowHeight));
        }
        else
        {
            // Nothing open — only the fixed group itself stays (there's no
            // row 1 at all in this state; ClientSize.Height == TabStripHeight).
            shape.Exclude(new Rectangle(0, 0, DrawerWidth, ClientSize.Height));
            shape.Exclude(new Rectangle(DrawerWidth + FixedGroupWidth, 0, ProfileExtraWidth, ClientSize.Height));
        }

        Region = shape;
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
