namespace UnboundKeys;

// The right-click dashboard. This window is a fixed WIDTH from the moment
// it's created and never resizes horizontally — only its height grows to
// fit whatever's currently revealed. Keyboard, Voice Keys, Mouse Keys, and
// Profile sit at permanent coordinates within a given left/right side (see
// Mirrored), and opening one of them just reveals a pre-allocated area that
// was already part of the window the whole time (previously invisible/
// unclickable via the window's Region), rather than growing the window
// into existence. That's what keeps the buttons from ever "jumping" apart
// from a side flip: there's no resize-and-reposition dance left to get
// wrong.
//
// Layout, left to right (unmirrored — see Mirrored for the flipped order),
// at RevealedContentX/RevealedContentWidth (FixedGroupX/FixedGroupWidth
// restated once something's revealed below):
//   x:[0, DrawerWidth)                    — nothing, ever, at any height —
//                                            an invisible buffer only OverlayForm's
//                                            flip math (VisibleLeftInset) still cares about
//   x:[DrawerWidth, DrawerWidth+FixedGroupWidth) — the only zone anything ever
//                                            shows in: row 0 is always Keyboard /
//                                            Mouse Keys / Voice Keys / Profile; below that,
//                                            whichever of the three is open reveals here —
//                                            Voice/Mouse an extra tab-strip row first
//                                            (their own numbered/button tabs, since they
//                                            no longer sit beside the fixed group — see
//                                            Fizzil's "drop down like Profile" ask, driven
//                                            by the floating keyboard window often needing
//                                            that space instead), then the selected card;
//                                            Profile's list reveals directly, no extra row
//   x:[DrawerWidth+FixedGroupWidth, MaxWidth) — nothing, ever (the listener icon sits here)
// Only one of the three is ever revealed at a time — see RecomputeRegion,
// which is now a single state-independent cut (the two side zones, full
// height) rather than a per-row/per-state decision. Voice Keys opens its
// own drawer directly, the same one-step pattern Mouse Keys already uses —
// it used to sit behind an extra Voice/Physical picker step (Physical
// Press was removed once the virtual on-screen keyboard made physically
// remapping the number row redundant), but there's nothing left to pick
// between now. Keyboard doesn't use any of this drawer/prime-tab machinery
// at all — it just toggles a separate floating window (see the
// constructor's toggleKeyboard/isKeyboardOpen parameters).
public sealed class DashboardForm : Form
{
    private const int TabStripHeight = 60;
    // One locked width shared by Keyboard/Mouse Keys/Voice Keys/Profile —
    // sized to fit the longest labels ("Mouse Keys"/"Voice Keys") without
    // clipping at the shared 9pt font, so the other two end up a bit
    // roomier than they strictly need rather than reading as mismatched
    // sizes.
    private const int FixedButtonWidth = 120;
    // Half that — Fade is just one short word and doesn't need the same
    // room, and Fizzil asked for its own space to come out of the
    // dashboard's total width rather than being redistributed to the other
    // four (the dashboard "feels a bit too long").
    private const int FadeButtonWidth = FixedButtonWidth / 2;
    // Internal, not private: OverlayForm anchors VirtualKeyboardForm's
    // top-right corner to the Keyboard button's top-left — the leftmost
    // pixel of this fixed group — purely from the icon's own position and
    // this width, without needing a live DashboardForm instance to read a
    // button's actual location from (the keyboard can be open while the
    // dashboard itself is closed). See OverlayForm.RepositionVirtualKeyboard.
    internal const int FixedGroupWidth = FixedButtonWidth * 4 + FadeButtonWidth;

    // The left zone: Voice's ten tabs and Mouse's six both render within
    // this same fixed width, so opening either always reveals exactly the
    // same amount of space.
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

    // Halved from the original 324/56 — Fizzil's playtester found every
    // card (Mouse Keys, Voice Keys, and the virtual keyboard's own remap
    // popup, which reuses these same values — see VirtualKeyRemapPopup)
    // way too tall for what's just a single line of button text per row.
    // Internal (not private) so that popup can reference these directly
    // instead of keeping its own separately-maintained copy in sync.
    internal const int BaseCardHeight = 162;
    internal const int ItemHeight = BaseCardHeight / 4;
    // Not halved the same way — the timing row's own content (the
    // +1/+0.1/reset/Infinite buttons) runs at a bigger 14pt font than the
    // 12pt everything else uses, so it needs closer to a full row's worth
    // of room rather than the roughly-70%-of-ItemHeight it used to get
    // (56 of 81) — at that same ratio applied to the new ItemHeight it
    // came out too cramped, crowding into the row below it.
    internal const int AccordionHeight = ItemHeight;

    // The dashboard has no outer padding — its visible content starts
    // exactly at its own window edges — so there's no offset for
    // OverlayForm to account for when lining the two windows up vertically.
    public const int TopInset = 0;

    private Button _profileButton;
    private Button _mouseToggleButton;
    private Button _voicePressToggleButton;
    private Button _keyboardToggleButton;
    private Button _fadeButton;
    private TableLayoutPanel _fixedGroup;

    private Panel _profileDropdown;

    private ActionBar _actionBar;
    private Panel _actionBarContent;
    private TableLayoutPanel _mouseStrip;
    private Panel _mouseStripContent;
    private Panel _profileContent;

    // Which of "profile"/"mouse"/"voice" (if any) currently has its area
    // revealed — independent of _selectedWord below, which is whichever
    // specific sub-tab inside Voice's or Mouse's drawer is showing a card.
    private string? _activePrimeTab;

    // Whether the fixed group / drawer / profile-extra zones are laid out
    // in mirrored order (Profile nearest the icon, Drawer farthest) —
    // driven by OverlayForm.RepositionDashboard, true exactly when this
    // window is currently flipped to the icon's right side. Left mode
    // keeps the original order (Drawer, Keyboard, Mouse Keys, Voice Keys,
    // Profile, icon) — Profile already ends up right against the icon
    // there. Mirrored reverses the fixed group's own button order too, so
    // Profile still ends up against the icon, and Keyboard — where the
    // floating keyboard window itself attaches — still ends up at the
    // outer edge, on whichever side that now is.
    private bool _mirrored;
    internal bool Mirrored
    {
        get => _mirrored;
        set
        {
            if (_mirrored == value)
                return;
            _mirrored = value;
            ApplyMirroring();
        }
    }

    // Each zone's own left edge, swapping ends when Mirrored — width never
    // changes (see the class comment), only which end each zone sits at.
    private int DrawerX => Mirrored ? FixedGroupWidth + ProfileExtraWidth : 0;
    private int FixedGroupX => Mirrored ? ProfileExtraWidth : DrawerWidth;
    private int ProfileExtraX => Mirrored ? 0 : DrawerWidth + FixedGroupWidth;

    // The single width/position every revealed drop-down uses — Voice's
    // word cards, Mouse's button cards, and Profile's list all share this
    // same area now (Fizzil: "that drop down should be the same width as
    // the dashboard"). "The dashboard" means the *visible* button row —
    // the only part that shows at all while collapsed — not this window's
    // own full (partly hidden) footprint: the drawer zone past FixedGroupX
    // only ever shows when Voice/Mouse's own tab strip is open, so lining
    // revealed content up with the window's true edge there made it jut
    // out well past where the collapsed dashboard visibly starts. So this
    // is just FixedGroupX/FixedGroupWidth restated under its own name —
    // exactly flush with the button row, on whichever side Mirrored has it
    // — which also directly satisfies "shouldn't cross under the listener
    // icon": it stops exactly where the icon-adjacent profile-extra sliver
    // begins, same as the button row above it always has.
    private const int RevealedContentWidth = FixedGroupWidth;
    private int RevealedContentX => FixedGroupX;

    // How far in from this window's own left/right edges the currently-
    // VISIBLE content actually starts/ends — read by OverlayForm when this
    // window is flipped to the icon's right side (see RepositionDashboard),
    // so whichever content is actually showing ends up flush against the
    // icon instead of the window's own edge, which sits behind an
    // invisible (Region-cut) zone. Now a fixed relationship rather than a
    // state-dependent one: the drawer zone and the profile-extra sliver
    // are never visible in any state (see RecomputeRegion) since Voice's
    // and Mouse's own tab strips dropped down into the reveal area
    // instead of sitting in the drawer zone — only which physical side
    // (left/right) each inset applies to still swaps under Mirrored.
    internal int VisibleLeftInset => Mirrored ? ProfileExtraWidth : DrawerWidth;
    internal int VisibleRightInset => Mirrored ? DrawerWidth : ProfileExtraWidth;

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

    // Unlike Voice Keys/Mouse Keys/Profile, Keyboard doesn't reveal an internal
    // drawer — it toggles a separate floating window (VirtualKeyboardForm)
    // that OverlayForm owns, deliberately independent of this dashboard's
    // own lifetime (see OverlayForm.ToggleVirtualKeyboard). toggleKeyboard
    // is called on click; isKeyboardOpen seeds the button's own highlight
    // at construction time, since a fresh DashboardForm is built every time
    // the dashboard reopens (see OverlayForm.ToggleDashboard) and needs to
    // reflect whatever the keyboard's actual state already is.
    public DashboardForm(Action toggleKeyboard, Func<bool> isKeyboardOpen)
    {
        // A Form built entirely in code (no Designer-generated
        // InitializeComponent, same situation NonActivatingForm's own
        // constructor documents) still defaults to AutoScaleMode.Font —
        // without a real design-time font baseline to compare against,
        // that can silently scale every explicit pixel Size/Location
        // this class sets for reasons that have nothing to do with the
        // monitor's actual DPI, which is what was leaving OverlayForm's
        // own (correctly unscaled — it inherits NonActivatingForm) icon-
        // relative math for this window's position visibly off by a
        // noticeable margin.
        AutoScaleMode = AutoScaleMode.None;

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

        // --- The fixed group: Keyboard, Voice Keys, Mouse Keys, Profile, Fade
        // (or the reverse — see LayoutFixedGroupButtons/Mirrored), always
        // FixedGroupWidth wide, positioned at FixedGroupX. Column widths
        // are set in LayoutFixedGroupButtons instead of here, since Fade's
        // own (narrower) column moves between index 0 and index 4
        // depending on Mirrored. ---
        _fixedGroup = new TableLayoutPanel
        {
            Location = new Point(FixedGroupX, 0),
            Size = new Size(FixedGroupWidth, TabStripHeight),
            Margin = new Padding(0),
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };
        // A thin frame around the button row, on all four sides — reads as
        // a clean, deliberate box on its own when collapsed, and (paired
        // with each content panel's own border below, which skips its top
        // edge) as one continuous outer frame with this row's own bottom
        // edge doubling as the divider between it and whatever's revealed
        // underneath, when something is. Same border-drawing technique
        // RemapCardTab's own cards already use.
        _fixedGroup.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Current.Muted);
            e.Graphics.DrawRectangle(pen, 0, 0, _fixedGroup.Width - 1, _fixedGroup.Height - 1);
        };

        _keyboardToggleButton = Theme.MakeTinyButton("Keyboard");
        _keyboardToggleButton.Margin = new Padding(0);
        _keyboardToggleButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_keyboardToggleButton);
        Theme.SetTabSelected(_keyboardToggleButton, isKeyboardOpen());
        _keyboardToggleButton.Click += (_, _) =>
        {
            toggleKeyboard();
            Theme.SetTabSelected(_keyboardToggleButton, isKeyboardOpen());
        };

        _voicePressToggleButton = Theme.MakeTinyButton("Voice Keys");
        _voicePressToggleButton.Margin = new Padding(0);
        _voicePressToggleButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_voicePressToggleButton);
        _voicePressToggleButton.Click += (_, _) => TogglePrimeTab("voice");

        _mouseToggleButton = Theme.MakeTinyButton("Mouse Keys");
        _mouseToggleButton.Margin = new Padding(0);
        _mouseToggleButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_mouseToggleButton);
        _mouseToggleButton.Click += (_, _) => TogglePrimeTab("mouse");

        _profileButton = Theme.MakeTinyButton("Profile");
        _profileButton.Margin = new Padding(0);
        _profileButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_profileButton);
        _profileButton.Click += (_, _) => TogglePrimeTab("profile");

        // Fades the dashboard, the virtual keyboard, and the listener icon
        // down to mostly see-through and back (see FadeMode) — a permanent
        // fixture here now rather than living on the virtual keyboard,
        // since it needs to be reachable whether or not that's even open.
        // Not a prime tab (doesn't reveal a dropdown) — an instant toggle,
        // same as Keyboard's own button.
        _fadeButton = Theme.MakeTinyButton("Fade");
        _fadeButton.Margin = new Padding(0);
        _fadeButton.Font = new Font("Segoe UI", 9f);
        Theme.EnableTabUnderline(_fadeButton);
        Theme.SetTabSelected(_fadeButton, FadeMode.IsOn);
        _fadeButton.Click += (_, _) => FadeMode.Toggle();
        FadeMode.Changed += RefreshFadeHighlight;

        LayoutFixedGroupButtons();

        // --- Voice's drawer: no longer a row beside the fixed group (that
        // strip used to sit exactly where the floating keyboard window
        // often needs to attach — see RepositionVirtualKeyboard — so
        // Fizzil asked for it to drop down instead, the same way Profile
        // already does). It's now the dropdown's own first row instead:
        // the ten numbered word tabs (RevealedContentWidth wide, at
        // RevealedContentX, y:TabStripHeight), then whichever of their
        // cards is currently selected right below that (same width/x,
        // y:TabStripHeight*2). ---
        _actionBar = new ActionBar(RevealedContentWidth / KeyMap.RemappableWords.Length);
        _actionBar.Control.Dock = DockStyle.None;
        _actionBar.Control.Location = new Point(RevealedContentX, TabStripHeight);
        _actionBar.Control.Size = new Size(RevealedContentWidth, TabStripHeight);
        _actionBar.Control.Paint += DrawOpenTopBorder;
        _actionBarContent = new Panel { Dock = DockStyle.None, Margin = new Padding(0), BackColor = Theme.Current.Background };
        _actionBarContent.Location = new Point(RevealedContentX, TabStripHeight * 2);
        _actionBarContent.Size = new Size(RevealedContentWidth, 0);
        _actionBarContent.Paint += DrawOpenTopBorder;

        // Voice Keys sits at the LEFT of the fixed group, immediately next
        // to this drawer's right edge — so "one" needs to end up rightmost
        // (closest to Voice Keys) and "ten" leftmost. Inserted in forward
        // order (1 through 10) since ActionBar.Add always inserts at the
        // left end — each insert-at-0 pushes the previous ones further
        // right, leaving "one" as the last (rightmost) one in.
        var words = KeyMap.RemappableWords;
        for (int i = 0; i < words.Length; i++)
        {
            var wordTab = new RemapCardTab(KeyMapSource.Instance, words[i], (i + 1).ToString());
            var tabButton = MakeSubTabButton(wordTab.Id, wordTab.Label, wordTab.BuildContent(MakeTabContext(wordTab.Id)), _actionBarContent);
            _actionBar.Add(wordTab.Id, tabButton);
        }

        // --- Mouse's drawer: same restructuring as Voice's above — the six
        // remappable-button tabs are now the dropdown's own first row
        // (RevealedContentWidth wide, at RevealedContentX, y:TabStripHeight),
        // then whichever of their cards is selected right below that
        // (same width/x, y:TabStripHeight*2). ---
        _mouseStrip = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            Location = new Point(RevealedContentX, TabStripHeight),
            Size = new Size(RevealedContentWidth, TabStripHeight),
            Margin = new Padding(0),
            ColumnCount = MouseCatalog.Buttons.Length,
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };
        _mouseStrip.Paint += DrawOpenTopBorder;
        _mouseStripContent = new Panel { Dock = DockStyle.None, Margin = new Padding(0), BackColor = Theme.Current.Background };
        _mouseStripContent.Location = new Point(RevealedContentX, TabStripHeight * 2);
        _mouseStripContent.Size = new Size(RevealedContentWidth, 0);
        _mouseStripContent.Paint += DrawOpenTopBorder;

        for (int i = 0; i < MouseCatalog.Buttons.Length; i++)
        {
            var buttonInfo = MouseCatalog.Buttons[i];
            // Percent, not Absolute, so the six buttons stretch to fill
            // RevealedContentWidth evenly rather than leaving a leftover
            // sliver.
            _mouseStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / MouseCatalog.Buttons.Length));

            var mouseTab = new RemapCardTab(MouseMapSource.Instance, buttonInfo.Id, buttonInfo.ShortLabel);
            var tabButton = MakeSubTabButton(mouseTab.Id, mouseTab.Label, mouseTab.BuildContent(MakeTabContext(mouseTab.Id)), _mouseStripContent);
            tabButton.Margin = new Padding(0);
            new ToolTip().SetToolTip(tabButton, buttonInfo.Label);
            _mouseStrip.Controls.Add(tabButton, i, 0);
        }

        // --- Profile's dropdown: just its list, no sub-tabs of its own.
        // Same RevealedContentWidth/RevealedContentX as the other two —
        // starting at y:TabStripHeight since the fixed group's own buttons
        // already occupy that row above it. ---
        var profilesTab = new ProfilesTab();
        _profileContent = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Current.Background };
        var profileCard = profilesTab.BuildContent(MakeTabContext(profilesTab.Id));
        profileCard.Visible = false;
        _profileContent.Controls.Add(profileCard);
        _cards[profilesTab.Id] = profileCard;

        _profileDropdown = new Panel
        {
            Location = new Point(RevealedContentX, TabStripHeight),
            Size = new Size(RevealedContentWidth, 0),
            Visible = false,
            BackColor = Theme.Current.Background,
        };
        _profileDropdown.Paint += DrawOpenTopBorder;
        _profileDropdown.Controls.Add(_profileContent);

        // --- Assemble. Z-order doesn't matter here for click purposes —
        // none of these x-ranges ever overlap (or, for the two tab strips/
        // three content panels, only one of each ever shows at a time) —
        // but the fixed group is added last/frontmost purely so its
        // buttons are never visually clipped by anything. ---
        _actionBar.Control.Visible = false;
        _actionBarContent.Visible = false;
        _mouseStrip.Visible = false;
        _mouseStripContent.Visible = false;
        Controls.Add(_actionBar.Control);
        Controls.Add(_actionBarContent);
        Controls.Add(_mouseStrip);
        Controls.Add(_mouseStripContent);
        Controls.Add(_profileDropdown);
        Controls.Add(_fixedGroup);

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

        // A fresh DashboardForm is built every time the dashboard reopens
        // (see OverlayForm.ToggleDashboard) — without this, each reopen
        // would stack another subscription onto FadeMode.Changed, from an
        // old window that's already gone.
        FormClosed += (_, _) => FadeMode.Changed -= RefreshFadeHighlight;
    }

    private void RefreshFadeHighlight() => Theme.SetTabSelected(_fadeButton, FadeMode.IsOn);

    // Re-lays-out the fixed group / drawers / profile dropdown at their
    // mirrored-or-not positions and reverses the fixed group's own button
    // order to match — see the Mirrored property. Wrapped in a screen-
    // update freeze for the same multi-step-change flicker reason
    // TogglePrimeTab is.
    private void ApplyMirroring()
    {
        BeginScreenUpdate();
        SuspendLayout();
        try
        {
            _fixedGroup.Location = new Point(FixedGroupX, 0);
            LayoutFixedGroupButtons();
            _actionBar.Control.Location = new Point(RevealedContentX, TabStripHeight);
            _mouseStrip.Location = new Point(RevealedContentX, TabStripHeight);
            _actionBarContent.Location = new Point(RevealedContentX, TabStripHeight * 2);
            _mouseStripContent.Location = new Point(RevealedContentX, TabStripHeight * 2);
            _profileDropdown.Location = new Point(RevealedContentX, TabStripHeight);
            RecomputeRegion();
        }
        finally
        {
            ResumeLayout(true);
            EndScreenUpdate();
        }
    }

    // Keyboard, Mouse Keys, Voice Keys, Profile, Fade, left to right — reversed
    // when Mirrored so Fade ends up nearest the icon either way (Fizzil:
    // "on the right hand side of profile", "so even if it's faded, it is
    // easy to find" — always right by the icon, the one fixed landmark
    // regardless of which side the dashboard is on), and Keyboard ends up
    // at the outer edge either way, right where the floating keyboard
    // window itself attaches (see OverlayForm.RepositionVirtualKeyboard).
    private void LayoutFixedGroupButtons()
    {
        var order = Mirrored
            ? new[] { _fadeButton, _profileButton, _voicePressToggleButton, _mouseToggleButton, _keyboardToggleButton }
            : new[] { _keyboardToggleButton, _mouseToggleButton, _voicePressToggleButton, _profileButton, _fadeButton };

        _fixedGroup.Controls.Clear();
        _fixedGroup.ColumnStyles.Clear();
        _fixedGroup.ColumnCount = order.Length;
        for (int i = 0; i < order.Length; i++)
        {
            int width = order[i] == _fadeButton ? FadeButtonWidth : FixedButtonWidth;
            _fixedGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            _fixedGroup.Controls.Add(order[i], i, 0);
        }
    }

    // Builds one sub-tab's button (a numbered word or a mouse button) and
    // wires up its click behavior: toggle its own card open/closed if it's
    // already the active one, otherwise switch to it. Shared by both
    // drawers that have sub-tabs at all — Profile doesn't, since it has
    // nothing further to pick once its own area is open.
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

    private Control GetDrawerTabStrip(string primeTab) => primeTab switch
    {
        "mouse" => _mouseStrip,
        "voice" => _actionBar.Control,
        _ => throw new ArgumentOutOfRangeException(nameof(primeTab)),
    };

    private Panel GetDrawerContent(string primeTab) => primeTab switch
    {
        "mouse" => _mouseStripContent,
        "voice" => _actionBarContent,
        _ => throw new ArgumentOutOfRangeException(nameof(primeTab)),
    };

    private Button GetPrimeButton(string primeTab) => primeTab switch
    {
        "profile" => _profileButton,
        "mouse" => _mouseToggleButton,
        "voice" => _voicePressToggleButton,
        _ => throw new ArgumentOutOfRangeException(nameof(primeTab)),
    };

    // Opens/closes one of the prime tabs. Clicking the one that's already
    // open collapses it back down to nothing; clicking a different one
    // swaps it in, closing whichever was open first — only one is ever
    // open at a time. This never touches the window's own Location or
    // Width — only which pre-positioned area is Visible, and the Region
    // that makes the rest of it non-existent rather than just an empty
    // black rectangle. See the class comment for why.
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
                else
                {
                    GetDrawerTabStrip(_activePrimeTab).Visible = false;
                    GetDrawerContent(_activePrimeTab).Visible = false;
                }
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
            else if (_activePrimeTab != null)
            {
                GetDrawerTabStrip(_activePrimeTab).Visible = true;
                GetDrawerContent(_activePrimeTab).Visible = true;
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
    // word cards and all six mouse-button cards, from scratch against it —
    // a full new set to adjust freely. The old cards are disposed (not
    // just hidden) so their tap-counter timers actually stop. Stays on the
    // Profiles dropdown afterward rather than jumping to a numbered tab.
    private void SwitchToProfile(string profileName)
    {
        // Sixteen cards get torn down and rebuilt below — without freezing
        // the screen for the duration, that was flashing visibly.
        BeginScreenUpdate();
        try
        {
            _openCategoryPopup?.Close();
            _openCategoryPopup = null;

            KeyMap.SwitchProfile(profileName);
            MouseMap.SwitchProfile(profileName);
            VirtualKeyMap.SwitchProfile(profileName);
            // If this profile's saved color differs, ThemeMode.Changed
            // fires here and OverlayForm closes this very dashboard out
            // from under the rest of this method (deferred, so the card
            // rebuild below still runs and completes normally against the
            // old colors on a window that's about to disappear anyway —
            // see OverlayForm.OnThemeChanged for why it's deferred).
            ThemeMode.SwitchProfile(profileName);

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
        if (!_selectedTabExpanded || _selectedWord == null)
            return 0;

        return _cardHeight.TryGetValue(_selectedWord, out var value) ? value : BaseCardHeight;
    }

    // Grows/shrinks the window (height only — width is fixed for good, see
    // the class comment) to fit however tall the selected sub-tab's card
    // currently is, and keeps whichever of _actionBarContent/
    // _mouseStripContent/_profileDropdown is actually active in sync with
    // that same height — the others don't matter since RecomputeRegion
    // hides them anyway, but leaving their height stale would show through
    // if the active one ever changed without a height change accompanying it.
    private void AdjustHeight()
    {
        BeginScreenUpdate();
        try
        {
            int cardHeight = CurrentCardHeight();
            // Voice/Mouse have an extra TabStripHeight-tall row now (their
            // own numbered/button tab strip, dropped down into the reveal
            // area itself instead of sitting beside the fixed group) —
            // Profile doesn't, since it has no sub-tabs of its own to pick.
            int fullHeight = _activePrimeTab is "voice" or "mouse"
                ? TabStripHeight + TabStripHeight + cardHeight
                : TabStripHeight + cardHeight;

            if (_activePrimeTab == "profile")
                _profileDropdown.Height = cardHeight;
            else if (_activePrimeTab != null)
                GetDrawerContent(_activePrimeTab).Height = cardHeight;

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

    // Shared by every control that sits directly below another bordered
    // one in the fixed-group/tab-strip/content stack (see the border-
    // wiring comment on _fixedGroup itself) — left, right, and bottom
    // only, so the piece above it can supply the top edge as a shared
    // divider line instead of two borders sitting flush against each
    // other (which would just look like a slightly thicker single line,
    // at best, or a visible double line if they don't land on the exact
    // same pixel).
    private static void DrawOpenTopBorder(object? sender, PaintEventArgs e)
    {
        var control = (Control)sender!;
        using var pen = new Pen(Theme.Current.Muted);
        e.Graphics.DrawLine(pen, 0, 0, 0, control.Height - 1);
        e.Graphics.DrawLine(pen, control.Width - 1, 0, control.Width - 1, control.Height - 1);
        e.Graphics.DrawLine(pen, 0, control.Height - 1, control.Width - 1, control.Height - 1);
    }

    // Cuts away whichever parts of the fixed MaxWidth×(current height)
    // rectangle aren't currently relevant, so they're genuinely not there
    // — not just painted black — matching how the window behaved before it
    // grew a fixed maximum size: nothing to click, nothing extra to see.
    //
    // Visible content now lives exclusively within FixedGroupX/
    // FixedGroupWidth (restated as RevealedContentX/Width once something's
    // revealed below) at every height and in every state: Voice's and
    // Mouse's own tab strips used to sit beside the fixed group (the
    // drawer zone), but they've dropped down into the reveal area itself
    // instead — the same spot Profile's dropdown already used — so the
    // drawer zone and the profile-extra sliver (where the icon sits) are
    // simply never shown, full stop, rather than needing a per-state,
    // per-row decision the way they used to.
    private void RecomputeRegion()
    {
        var shape = new Region(new Rectangle(Point.Empty, ClientSize));
        shape.Exclude(new Rectangle(DrawerX, 0, DrawerWidth, ClientSize.Height));
        shape.Exclude(new Rectangle(ProfileExtraX, 0, ProfileExtraWidth, ClientSize.Height));
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
