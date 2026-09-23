using System.Threading.Tasks;

namespace UnboundKeys;

// The floating on-screen keyboard spawned by the dashboard's Keyboard
// button — meant to replace the Windows on-screen keyboard for someone
// typing with a mouse. Inherits NonActivatingForm (same as OverlayForm and
// every popup) so clicking a key never steals focus from whatever app is
// actually being typed into — more important here than anywhere else in
// the app, since that's the entire point.
//
// Left-click sends a key (like the Windows OSK); right-click on one of the
// 36 remappable keys (the ten digits and 26 letters — see
// VirtualKeyCatalog) opens its Key/Repeat/Hold/Infinite card instead (see
// VirtualKeyRemapPopup) — remapping one also mirrors onto a real physical
// keyboard, see PhysicalKeyWatcher. Every other key (including Tab, Space,
// the arrows, and the modifiers) is a plain, fixed copy of a real key with
// no card and no physical mirroring. Shift/Ctrl/Alt/Win are sticky (see
// StickyModifiers) rather than simple taps, so they can combo with a
// second click the way a real held-down modifier would — deliberately
// fixed rather than remappable, since a held-down modifier isn't
// meaningfully "a key you'd send something else instead of."
//
// "Mini" (Row1, sliced out of Backspace's own width) collapses the whole
// window down to MiniRow's single quick-access strip — Windows' own
// on-screen keyboard has an equivalent compact mode; ours uses only keys
// already in this layout rather than that one's own settings-panel
// shortcuts. "Maxi" (MiniRow's own last button) expands back — deliberately
// at the same top-right corner Mini itself sits at, and both layouts share
// the same overall width and start at the same window position (see
// OverlayForm.RepositionVirtualKeyboard), so toggling between them barely
// moves the mouse. See ToggleMiniMode. Fade (see FadeMode) used to live
// here too (sliced out of Enter) but is now a permanent fixture on the
// dashboard instead, reachable whether or not this window is even open.
public sealed class VirtualKeyboardForm : NonActivatingForm
{
    private const int RowHeight = 44;
    private const int FormWidth = 780;
    // Matches DashboardForm's own TabStripHeight — both windows start at
    // the same Y (see OverlayForm.RepositionVirtualKeyboard), so matching
    // heights here is what makes the minimized strip's top AND bottom
    // edges line up with the dashboard's own collapsed (no drawer open)
    // height, not just its top.
    private const int MiniHeight = 60;

    private enum KeyKind { Plain, Remappable, StickyFixed, MiniToggle }

    // LargeLabel defaults to false, so Remap/StickyFixed/Toggle below don't
    // need to pass it — only Plain() exposes it, for the handful of
    // punctuation keys (semicolon/colon, quote, comma) whose glyphs read
    // as too small at the normal size (see Row3/Row4).
    private readonly record struct KeySpec(string Label, KeyKind Kind, string? Id, ushort Vk, bool Extended, float Width, string? ShiftLabel, bool LargeLabel = false);

    // shiftLabel is the symbol this key's own label switches to while
    // Shift is sticky-active — purely cosmetic (the real Shift key is
    // genuinely held down via SendInput, so the target app already gets
    // the shifted character regardless of the label) — same reasoning as
    // the letter buttons' own lowercase/uppercase flip. largeLabel bumps
    // the font size for a key whose glyph(s) are hard to read at the
    // normal size — applies to whichever of label/shiftLabel is currently
    // showing, since both share one Font set once at BuildRow time.
    private static KeySpec Plain(string label, ushort vk, bool extended, float width, string? shiftLabel = null, bool largeLabel = false) =>
        new(label, KeyKind.Plain, null, vk, extended, width, shiftLabel, largeLabel);

    private static KeySpec Remap(string id, string label, float width, string? shiftLabel = null) =>
        new(label, KeyKind.Remappable, id, 0, false, width, shiftLabel);

    private static KeySpec StickyFixed(string id, string label, ushort vk, bool extended, float width) =>
        new(label, KeyKind.StickyFixed, id, vk, extended, width, null);

    // "Mini" on the full-size keyboard, "Maxi" on the minimized strip —
    // two different buttons (see Row5/MiniRow), same ToggleMiniMode
    // click behavior either way.
    private static KeySpec Toggle(string label, float width) =>
        new(label, KeyKind.MiniToggle, null, 0, false, width, null);

    private readonly List<(Button Button, string Id)> _stickyButtons = new();
    // Every remappable digit/letter button — paints a light accent-colored
    // wash over any of them that's actually customized (see
    // VirtualKeyMap.IsCustomized), so a glance at the layout shows which
    // keys aren't at their own natural default anymore. Refreshed after a
    // remap popup closes (see ToggleRemapPopup) rather than live while
    // it's still open, since nothing currently reports "the card's content
    // just changed" back out of RemapCardTab — that's the one gap in an
    // otherwise-live indicator.
    private readonly List<Button> _remappableButtons = new();
    // Every remappable letter key (va..vz) — its displayed label flips
    // between lowercase and uppercase with Shift's sticky state, same as a
    // real on-screen keyboard, purely cosmetic (the actual key sent is
    // still whatever's mapped, unaffected by letter case).
    private readonly List<Button> _letterButtons = new();
    // Number-row and punctuation keys with a shifted symbol (1 -> !, ; -> :,
    // ...) — flips to that symbol while Shift is sticky-active, same idea
    // as the letter buttons above but swapping between two fixed labels
    // instead of upper/lowercasing the same one.
    private readonly List<(Button Button, string Base, string Shifted)> _shiftableButtons = new();
    // Caps has its own button in both the full layout and the minimized
    // strip (see MiniRow) — both need to stay in sync.
    private readonly List<Button> _capsLockButtons = new();
    // Tracked locally rather than re-queried from Windows after every
    // click: Control.IsKeyLocked reads GetKeyState's toggle bit, which is
    // only reliably up to date on the thread that actually owns keyboard
    // focus — our own SendInput-injected Caps Lock keystroke goes to
    // whatever window currently has real focus (never this one, since it's
    // a NonActivatingForm), so querying it back immediately (even a tick
    // later) raced with that other thread actually processing it, which is
    // what was making the underline/letter case need an extra click to
    // catch up. Since Fizzil has no physical keyboard, this button is the
    // only thing that ever toggles Caps Lock, so tracking it ourselves is
    // both simpler and actually reliable.
    private bool _capsLockOn;

    // Same panic button PhysicalKeyWatcher's own real Caps Lock key
    // already gives an able-bodied friend with a keyboard — mirrored here
    // so Fizzil (mouse-only, no physical keyboard) has an equivalent way
    // to reach it: two rapid clicks of this button also releases
    // everything (see KeyExecutor.ReleaseAll), the same "even count so
    // the toggle it causes cancels itself out" reasoning as the physical
    // version.
    private const int PanicTapWindowMs = 1000;
    private int _capsLockPanicTapCount;
    private DateTime _lastCapsLockPanicTap = DateTime.MinValue;

    private Form? _openRemapPopup;
    private Control? _openRemapAnchor;
    private string? _openRemapKeyId;

    // Whether the minimized strip (see MiniRow) is currently showing
    // instead of the full 5-row layout — toggled by "Mini"/"Maxi" (see
    // ToggleMiniMode). Both layouts are built once, up front, and kept
    // around the whole time; toggling just swaps which is Visible and
    // resizes the window to match, the same "pre-built, nothing torn
    // down" approach DashboardForm's own prime tabs use.
    private bool _isMini;
    // Read by OverlayForm.OnThemeChanged so reopening the keyboard while
    // comparing colors (see that method's own comment) restores whichever
    // of Mini/Maxi it was already in, instead of always starting fresh at
    // full size.
    internal bool IsMini => _isMini;
    private TableLayoutPanel _fullLayout;
    private TableLayoutPanel _miniLayout;

    // startMini seeds the initial layout directly (bypassing
    // ToggleMiniMode's popup/anchor cleanup, which has nothing to clean up
    // yet at construction time) — used by OverlayForm.OnThemeChanged to
    // reopen the keyboard in the same Mini/Maxi state it was closed in.
    public VirtualKeyboardForm(bool startMini = false)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Theme.Current.Background;
        DoubleBuffered = true;

        _isMini = startMini;
        ClientSize = _isMini ? new Size(FormWidth, MiniHeight) : new Size(FormWidth, RowHeight * 5);

        _fullLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Current.Background,
            Visible = !_isMini,
        };
        for (int i = 0; i < 5; i++)
            _fullLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, RowHeight));

        _fullLayout.Controls.Add(BuildRow(Row1()), 0, 0);
        _fullLayout.Controls.Add(BuildRow(Row2()), 0, 1);
        _fullLayout.Controls.Add(BuildRow(Row3()), 0, 2);
        _fullLayout.Controls.Add(BuildRow(Row4()), 0, 3);
        _fullLayout.Controls.Add(BuildRow(Row5()), 0, 4);

        _miniLayout = BuildRow(MiniRow(), flush: true);
        _miniLayout.Visible = _isMini;

        Controls.Add(_fullLayout);
        Controls.Add(_miniLayout);

        // A one-time cold read to match whatever Caps Lock's real state
        // already is when the keyboard opens — reliable here since it
        // isn't racing against a SendInput call this same thread just
        // made (see _capsLockOn's own comment for why every click after
        // this just flips the tracked value instead of asking again).
        _capsLockOn = Control.IsKeyLocked(Keys.CapsLock);

        StickyModifiers.Changed += RefreshStickyHighlights;
        RefreshStickyHighlights();
        RefreshCapsLockHighlight();
        // Reflects whatever's already customized from a previous session
        // (or a profile switch) the moment the keyboard opens, not just
        // after the first time a card happens to be opened and closed.
        RefreshCustomizedIndicators();

        LocationChanged += (_, _) =>
        {
            if (_openRemapPopup != null && !_openRemapPopup.IsDisposed && _openRemapAnchor != null)
                VirtualKeyRemapPopup.Reposition(_openRemapPopup, _openRemapAnchor);
        };

        // Esc (Row1's first control) would otherwise be the implicit
        // default ActiveControl — closing the dashboard (a sibling
        // NonActivatingForm) shifts window activation away and back even
        // though neither ever takes real focus, and that blip is enough
        // for Windows to paint its own focus cue on it. Same fix
        // DashboardForm already uses for the identical reason.
        Activated += (_, _) => ActiveControl = null;
        Deactivate += (_, _) => ActiveControl = null;

        FormClosing += (_, _) =>
        {
            _openRemapPopup?.Close();
            // Never leave a real Shift/Ctrl/Alt/Win stuck down system-wide
            // just because the keyboard itself was closed mid-combo.
            StickyModifiers.ReleaseAll();
        };
        FormClosed += (_, _) =>
        {
            StickyModifiers.Changed -= RefreshStickyHighlights;
        };
    }

    // Row 1: Esc, backtick, the number row (remappable), -, =, Backspace,
    // Mini. Mini is sliced out of what used to be Backspace's full width
    // (2f -> 0.6f for Backspace, 1.4f for Mini) — deliberately placed here
    // rather than at the end of Row5: Row1 sits at the very top of the
    // window, the same place MiniRow's own single row occupies when
    // collapsed, and both are the same overall width (FormWidth) — so Mini
    // and Maxi (MiniRow's last button) end up in very close to the same
    // on-screen spot regardless of which mode is showing, and toggling
    // between them barely moves the mouse at all. Esc and backtick were
    // each trimmed 20% (1.5f -> 1.2f, 1f -> 0.8f), freeing up room for
    // Backspace, which was reading as too small — rather than handing it
    // the exact amount freed, it's sized to land at 1f, matching Equals
    // right next to it.
    private static KeySpec[] Row1() => new[]
    {
        Plain("Esc", 0x1B, false, 1.2f),
        Plain("`", 0xC0, false, 0.8f, "~"),
        Remap("v1", "1", 1f, "!"),
        Remap("v2", "2", 1f, "@"),
        Remap("v3", "3", 1f, "#"),
        Remap("v4", "4", 1f, "$"),
        Remap("v5", "5", 1f, "%"),
        Remap("v6", "6", 1f, "^"),
        Remap("v7", "7", 1f, "&"),
        Remap("v8", "8", 1f, "*"),
        Remap("v9", "9", 1f, "("),
        Remap("v0", "0", 1f, ")"),
        Plain("-", 0xBD, false, 1f, "_"),
        Plain("=", 0xBB, false, 1f, "+"),
        Plain("⌫", 0x08, false, 1f),
        Toggle("Mini", 1.4f),
    };

    // Row 2: Tab, Q-P (remappable), brackets/backslash, Del. Backslash
    // trimmed 10% (1.5f -> 1.35f) and that handed to Del (1.5f -> 1.65f),
    // so Del reads as a step bigger than Mini right above it (1.4f) —
    // continuing the same stepped-bigger-going-down effect the left edge
    // already has (Esc/Tab/Caps/Shift), now on the right edge too
    // (Mini/Del/Enter/Shift — see Row4's own comment).
    private static KeySpec[] Row2() => new[]
    {
        Plain("Tab", 0x09, false, 1.5f),
        Remap("vq", "q", 1f),
        Remap("vw", "w", 1f),
        Remap("ve", "e", 1f),
        Remap("vr", "r", 1f),
        Remap("vt", "t", 1f),
        Remap("vy", "y", 1f),
        Remap("vu", "u", 1f),
        Remap("vi", "i", 1f),
        Remap("vo", "o", 1f),
        Remap("vp", "p", 1f),
        Plain("[", 0xDB, false, 1f, "{"),
        Plain("]", 0xDD, false, 1f, "}"),
        Plain("\\", 0xDC, false, 1.35f, "|"),
        Plain("Del", 0x2E, true, 1.65f),
    };

    // Row 3: Caps, A-L (remappable), semicolon/quote, Enter — Enter's back
    // to its original full width now that Fade (which used to be sliced
    // out of it) moved to the dashboard.
    private static KeySpec[] Row3() => new[]
    {
        Plain("Caps", 0x14, false, 1.8f),
        Remap("va", "a", 1f),
        Remap("vs", "s", 1f),
        Remap("vd", "d", 1f),
        Remap("vf", "f", 1f),
        Remap("vg", "g", 1f),
        Remap("vh", "h", 1f),
        Remap("vj", "j", 1f),
        Remap("vk", "k", 1f),
        Remap("vl", "l", 1f),
        Plain(";", 0xBA, false, 1f, ":", largeLabel: true),
        Plain("'", 0xDE, false, 1f, "\"", largeLabel: true),
        Plain("Enter", 0x0D, false, 2.2f),
    };

    // Row 4: Shift (sticky-fixed), Z-M (remappable), comma/period/slash,
    // Shift again — both Shift buttons share id "vshift". Up used to sit
    // here, right before the second Shift, but it never visually lined up
    // with Down on Row 5 below (this row and Row 5 carry a different mix
    // of keys, so their percentage-based widths don't share a common grid)
    // — moved down to Row 5 instead, alongside the other three arrows,
    // where alignment is automatic. The width it freed went to both Shift
    // keys (2.3f -> 2.8f each) rather than sitting idle, continuing the
    // same descending-right-edge-width staircase Esc/Tab/Caps/Shift
    // already form going down the left edge (see Row5/Row2's own comments).
    private static KeySpec[] Row4() => new[]
    {
        StickyFixed("vshift", "Shift", 0x10, false, 2.8f),
        Remap("vz", "z", 1f),
        Remap("vx", "x", 1f),
        Remap("vc", "c", 1f),
        Remap("vv", "v", 1f),
        Remap("vb", "b", 1f),
        Remap("vn", "n", 1f),
        Remap("vm", "m", 1f),
        Plain(",", 0xBC, false, 1f, "<", largeLabel: true),
        Plain(".", 0xBE, false, 1f, ">"),
        Plain("/", 0xBF, false, 1f, "?"),
        StickyFixed("vshift", "Shift", 0x10, false, 2.8f),
    };

    // Row 5: Ctrl/Win/Alt (all sticky-fixed), Space, Alt/Ctrl again,
    // Left/Down/Up/Right — all four arrows now, same order MiniRow already
    // uses (←↓↑→), since Up moved down here from Row 4 (see that row's own
    // comment) rather than sitting one row up and never quite lining up
    // with Down. Space is trimmed by exactly Up's own width (6f -> 5f) to
    // make room, so every other key on this row — Ctrl/Win/Alt included —
    // renders at the exact same pixel width as before; Space is simply the
    // one key on this row with width to spare.
    private static KeySpec[] Row5() => new[]
    {
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1.5f),
        StickyFixed("vwin", "Win", 0x5B, true, 1.5f),
        StickyFixed("valt", "Alt", 0x12, false, 1.5f),
        Plain("", 0x20, false, 5f),
        StickyFixed("valt", "Alt", 0x12, false, 1.5f),
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1.5f),
        Plain("←", 0x25, true, 1f),
        Plain("↓", 0x28, true, 1f),
        Plain("↑", 0x26, true, 1f),
        Plain("→", 0x27, true, 1f),
    };

    // The minimized strip — same idea as Windows' own on-screen keyboard's
    // compact mode, just with our own keys instead of its settings-panel
    // shortcuts (PgUp/PgDn/F6/"General" aren't real keys and don't exist
    // anywhere else in this layout). None of these 15 are remappable —
    // that's the whole point of collapsing away the letters/digits — so
    // there's nothing here that ever shows a remapped label. Order is
    // Fizzil's own, left to right; Maxi (last) expands back to Row1-5.
    private static KeySpec[] MiniRow() => new[]
    {
        Plain("Esc", 0x1B, false, 1f),
        Plain("Tab", 0x09, false, 1f),
        Plain("Caps", 0x14, false, 1f),
        StickyFixed("vctrl", "Ctrl", 0x11, false, 1f),
        StickyFixed("vwin", "Win", 0x5B, true, 1f),
        StickyFixed("valt", "Alt", 0x12, false, 1f),
        Plain("Space", 0x20, false, 1f),
        Plain("←", 0x25, true, 1f),
        Plain("↓", 0x28, true, 1f),
        Plain("↑", 0x26, true, 1f),
        Plain("→", 0x27, true, 1f),
        Plain("Enter", 0x0D, false, 1f),
        Plain("Del", 0x2E, true, 1f),
        Plain("⌫", 0x08, false, 1f),
        Toggle("Maxi", 1f),
    };

    // flush strips the small gap MakeTinyButton normally leaves between
    // buttons (its default 1px Margin, which reads as a thin border once
    // there's black Background showing through it) — used for MiniRow so
    // it sits flush like Mouse's own drawer tabs do, instead of looking
    // like a row of separately-bordered keys the way the full keyboard's
    // own rows deliberately do.
    private TableLayoutPanel BuildRow(KeySpec[] specs, bool flush = false)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            ColumnCount = specs.Length,
            RowCount = 1,
            BackColor = Theme.Current.Background,
        };

        float totalUnits = 0f;
        foreach (var spec in specs)
            totalUnits += spec.Width;

        foreach (var spec in specs)
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, spec.Width / totalUnits * 100f));

        for (int i = 0; i < specs.Length; i++)
        {
            var button = Theme.MakeTinyButton(specs[i].Label);
            button.Font = new Font("Segoe UI", specs[i].LargeLabel ? 12f : 8f);
            if (flush)
                button.Margin = new Padding(0);
            // WinForms otherwise treats a lone "&" in a button's Text as a
            // hidden keyboard-mnemonic marker instead of displaying it —
            // that's what was swallowing "&" (Shift+7)'s own label. None of
            // these buttons need mnemonic/access-key behavior anyway.
            button.UseMnemonic = false;
            // MakeTinyButton's own built-in pressed-state fill (a solid
            // Accent flash while the mouse is down) is fine for a quick
            // click elsewhere in the app, but these keys can be held for a
            // while (see the repeat timer below) — a lasting solid fill
            // looks wrong for that, so it's turned off here in favor of
            // just the underline below, which already carries the same
            // "currently pressed" meaning.
            button.FlatAppearance.MouseDownBackColor = Theme.Current.Button;
            // Every key gets the same bottom-border-underline treatment —
            // a brief flash while held down (see WireKey), or a lasting
            // highlight for a sticky modifier until it's released.
            Theme.EnableTabUnderline(button);

            if (specs[i].Kind == KeyKind.Remappable)
            {
                var remapId = specs[i].Id!;
                // A light accent-colored wash — low alpha, so it reads as
                // a subtle "this one's customized" hint rather than the
                // solid, everything-covering fill that made the held-down
                // flash look wrong (see the MouseDownBackColor comment
                // above). Painted on top of the button's own rendering,
                // same trick EnableTabUnderline already uses for its bar.
                button.Paint += (_, e) =>
                {
                    if (!VirtualKeyMap.IsCustomized(remapId))
                        return;
                    using var brush = new SolidBrush(Color.FromArgb(60, Theme.Current.Accent));
                    e.Graphics.FillRectangle(brush, button.ClientRectangle);
                };
                _remappableButtons.Add(button);
            }

            row.Controls.Add(button, i, 0);
            WireKey(button, specs[i]);

            if (specs[i].Kind == KeyKind.StickyFixed)
                _stickyButtons.Add((button, specs[i].Id!));

            // A remappable letter specifically (ids "va".."vz") — the
            // other remappable ids are the digits ("v1".."v9","v0"), whose
            // second character is a digit rather than a lowercase letter,
            // so this check never catches those.
            if (specs[i].Kind == KeyKind.Remappable && specs[i].Id is { Length: 2 } id && id[0] == 'v' && char.IsLower(id[1]))
                _letterButtons.Add(button);

            if (specs[i].ShiftLabel != null)
                _shiftableButtons.Add((button, specs[i].Label, specs[i].ShiftLabel!));

            // Caps Lock is the one Plain key that isn't a simple tap — its
            // underline should track the real OS Caps Lock state (like a
            // real keyboard's LED), not just flash while the mouse is down.
            if (specs[i].Kind == KeyKind.Plain && specs[i].Vk == VkCapital)
                _capsLockButtons.Add(button);
        }

        return row;
    }

    private const ushort VkCapital = 0x14;
    private const int RepeatInitialDelayMs = 450;
    private const int RepeatIntervalMs = 100; // matches KeyExecutor's own RepeatIntervalMs

    // MouseUp/MouseDown (not Click) so left and right clicks can be told
    // apart, same reason OverlayForm's own icon uses MouseUp instead of
    // Click. The actual press now fires on MouseDown, not MouseUp — same
    // as a real key, which types the instant it's pressed rather than
    // waiting for release — so holding it down can then repeat it for as
    // long as it stays down (see the repeat timer below).
    private void WireKey(Button button, KeySpec spec)
    {
        System.Windows.Forms.Timer? repeatTimer = null;

        // Deliberately doesn't consume active sticky modifiers itself —
        // this runs once per tap AND once per repeat tick while a key is
        // held (see the repeat timer below), and a held Shift needs to
        // stay down for the whole hold (typing a run of capitals), not
        // just its first character. MouseUp consumes them instead, once
        // the whole press-or-hold gesture is actually over.
        void PerformPress()
        {
            switch (spec.Kind)
            {
                case KeyKind.Remappable:
                    Task.Run(() => KeyExecutor.Execute(spec.Id!, VirtualKeyMap.GetAllKeys(spec.Id!), VirtualKeyMap.Behaviors[spec.Id!]));
                    break;
                case KeyKind.Plain:
                    NativeInput.TapKey(spec.Vk, spec.Extended);
                    if (spec.Vk == VkCapital)
                    {
                        _capsLockOn = !_capsLockOn;
                        RefreshCapsLockHighlight();
                        HandleCapsLockPanicTap();
                    }
                    break;
            }
        }

        // Caps Lock toggles rather than types, so holding it down doesn't
        // mean "toggle repeatedly" — a Remappable key only auto-repeats
        // while its own card has no Repeat/Hold/Infinite configured; if it
        // does, a single press already triggers that full behavior (same
        // as Mouse), and layering mechanical mouse-hold repeat on top of
        // it would just fight with whatever's already running.
        bool CanRepeat() => spec.Kind switch
        {
            KeyKind.Plain => spec.Vk != VkCapital,
            KeyKind.Remappable => VirtualKeyMap.Behaviors[spec.Id!] is { Repeat: false, Hold: false, Infinite: false },
            _ => false,
        };

        void StopRepeat()
        {
            repeatTimer?.Stop();
            repeatTimer?.Dispose();
            repeatTimer = null;
        }

        button.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            // Immediate visual feedback the instant the button goes down,
            // same as a real key dipping under your finger — for a sticky
            // modifier this gets superseded by RefreshStickyHighlights the
            // moment MouseUp actually toggles it, so the underline still
            // ends up reading the modifier's true state either way.
            Theme.SetTabSelected(button, true);

            if (spec.Kind is not (KeyKind.Plain or KeyKind.Remappable))
                return;

            PerformPress();
            if (!CanRepeat())
                return;

            bool firstTick = true;
            repeatTimer = new System.Windows.Forms.Timer { Interval = RepeatInitialDelayMs };
            repeatTimer.Tick += (_, _) =>
            {
                if (firstTick)
                {
                    firstTick = false;
                    repeatTimer!.Interval = RepeatIntervalMs;
                }
                PerformPress();
            };
            repeatTimer.Start();
        };

        button.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                StopRepeat();
                switch (spec.Kind)
                {
                    case KeyKind.StickyFixed:
                        StickyModifiers.Toggle(spec.Id!, new List<(ushort, bool)> { (spec.Vk, spec.Extended) });
                        break;
                    case KeyKind.Remappable:
                        StickyModifiers.ConsumeForKeyPress();
                        Theme.SetTabSelected(button, false);
                        break;
                    case KeyKind.Plain:
                        StickyModifiers.ConsumeForKeyPress();
                        // Caps Lock keeps reflecting its real toggle state
                        // (set by PerformPress above) instead of always
                        // clearing on release.
                        if (spec.Vk != VkCapital)
                            Theme.SetTabSelected(button, false);
                        break;
                    case KeyKind.MiniToggle:
                        ToggleMiniMode();
                        Theme.SetTabSelected(button, false);
                        break;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (spec.Kind == KeyKind.Remappable)
                    ToggleRemapPopup(button, spec.Id!, spec.Label);
            }
        };
    }

    // Only one remap popup open at a time — right-clicking a different
    // remappable key swaps it in; right-clicking the same key again closes
    // it (matching how Key/Repeat/Hold rows already toggle elsewhere).
    private void ToggleRemapPopup(Control anchor, string id, string label)
    {
        bool wasOpenForSameKey = _openRemapPopup != null && !_openRemapPopup.IsDisposed && _openRemapKeyId == id;
        bool hadPopupOpen = _openRemapPopup != null && !_openRemapPopup.IsDisposed;

        _openRemapPopup?.Close();
        _openRemapPopup = null;
        _openRemapAnchor = null;
        _openRemapKeyId = null;

        // Whatever card just closed (this one, or a different one being
        // swapped out) may have changed that key's customized state —
        // catches every close path in one place rather than needing every
        // individual edit inside RemapCardTab to report back out.
        if (hadPopupOpen)
            RefreshCustomizedIndicators();

        if (wasOpenForSameKey)
            return;

        _openRemapPopup = VirtualKeyRemapPopup.Show(this, anchor, id, label);
        _openRemapAnchor = anchor;
        _openRemapKeyId = id;
    }

    private void RefreshCustomizedIndicators()
    {
        foreach (var button in _remappableButtons)
            button.Invalidate();
    }

    private void RefreshStickyHighlights()
    {
        foreach (var (button, id) in _stickyButtons)
            Theme.SetTabSelected(button, StickyModifiers.IsActive(id));

        RefreshLetterCase();

        bool shiftActive = StickyModifiers.IsActive("vshift");
        foreach (var (button, baseLabel, shiftedLabel) in _shiftableButtons)
            button.Text = shiftActive ? shiftedLabel : baseLabel;
    }

    // Letters show uppercase whenever exactly one of Caps Lock/Shift is
    // active, same as a real keyboard actually types — Shift inverts
    // whatever Caps Lock already set, rather than always winning outright,
    // so Shift+CapsLock-on reads as lowercase, matching real typed output.
    private void RefreshLetterCase()
    {
        bool showUpper = _capsLockOn ^ StickyModifiers.IsActive("vshift");
        foreach (var button in _letterButtons)
            button.Text = showUpper ? button.Text.ToUpperInvariant() : button.Text.ToLowerInvariant();
    }

    // Reflects _capsLockOn — same idea as a physical keyboard's own Caps
    // Lock LED — rather than the brief press/release flash every other
    // key gets.
    private void RefreshCapsLockHighlight()
    {
        foreach (var button in _capsLockButtons)
            Theme.SetTabSelected(button, _capsLockOn);
        RefreshLetterCase();
    }

    // Collapses the full 5-row layout down to MiniRow's single quick-
    // access strip, or expands back — same window, just which of
    // _fullLayout/_miniLayout is Visible and the ClientSize to match.
    // Closes any open remap card first: none of MiniRow's keys are
    // remappable, so a card anchored to a full-layout key would be left
    // pointing at a now-hidden button.
    private void ToggleMiniMode()
    {
        _openRemapPopup?.Close();
        _openRemapPopup = null;
        _openRemapAnchor = null;
        _openRemapKeyId = null;

        _isMini = !_isMini;
        _fullLayout.Visible = !_isMini;
        _miniLayout.Visible = _isMini;
        // Same width either way — only the height collapses — so the 15
        // strip buttons have room for real labels ("Space", "Enter", ...)
        // instead of clipping down to a single letter.
        ClientSize = _isMini ? new Size(FormWidth, MiniHeight) : new Size(FormWidth, RowHeight * 5);
    }

    // Two rapid clicks of the virtual Caps Lock key — same window/reset
    // shape as PhysicalKeyWatcher's own real-keyboard version. Also
    // releases Fade (see FadeMode.TurnOff) — this is the panic button
    // Fizzil (mouse-only, no physical keyboard) actually has, so it's the
    // one that has to double as "the screen's too faded to find the
    // dashboard's Fade button" recovery.
    private void HandleCapsLockPanicTap()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastCapsLockPanicTap).TotalMilliseconds > PanicTapWindowMs)
            _capsLockPanicTapCount = 0;
        _capsLockPanicTapCount++;
        _lastCapsLockPanicTap = now;

        if (_capsLockPanicTapCount < 2)
            return;

        _capsLockPanicTapCount = 0;
        Task.Run(KeyExecutor.ReleaseAll);
        FadeMode.TurnOff();
    }
}
