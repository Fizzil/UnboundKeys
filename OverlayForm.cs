using System.Drawing.Imaging;
using System.Reflection;

namespace UnboundKeys;

// The overlay is just the skull image, sitting in the corner. Left-click
// opens the dashboard; right-click toggles listening on/off (dimmed =
// paused); four rapid right-clicks opens a small color picker instead
// (see ThemeColorPopup) — Red/Green/Blue, re-theming every accent color
// in the app at once, not just the icon. Close via the taskbar icon.
//
// Dashboard access deliberately rides on Left Click specifically, not
// Right Click: Left Click is the one mouse button that can never be
// remapped (see MouseCatalog), so no matter what's mapped to Right Click
// — even something that swallows every Right Click system-wide — the
// dashboard is always reachable to go fix it. Right Click doesn't need
// that same protection now that it's not the way in.
public sealed class OverlayForm : NonActivatingForm
{
    // Shrunk to match the size the taskbar's own (now-removed) listener icon
    // used to be, since this is now the only listener icon there is.
    // Internal rather than private — DashboardForm's Profile dropdown needs
    // to know exactly how wide the icon is to size itself flush with its
    // right edge, and duplicating the number risked them drifting apart.
    internal const int TargetWidth = 60;


    // How far the mouse has to move (while held down) before a press counts
    // as a drag instead of a click — small enough to feel immediate, large
    // enough that a slightly shaky click doesn't accidentally drag instead
    // of toggling pause.
    private const int DragThreshold = 4;

    private readonly VoiceEngine _voice;
    private readonly MouseInputWatcher _mouse;
    private readonly PhysicalKeyWatcher _physical;
    private readonly PictureBox _icon;
    private Image _listeningImage;
    private Image _pausedImage;
    private bool _paused;
    private DashboardForm? _dashboard;
    private VirtualKeyboardForm? _virtualKeyboard;
    // Which side RepositionDashboard/RepositionVirtualKeyboard chose last
    // call — see the hysteresis comments there. Each reset whenever its
    // own window is (re)opened, so a fresh open always starts from the
    // plain "does it fit normally" check rather than stale state.
    private bool _dashboardFlipped;
    private bool _keyboardFlipped;
    private Point _dragMouseStart;
    private bool _dragging;

    // Four rapid right-clicks opens the color picker (see ThemeColorPopup)
    // instead of just pausing — reset if the gap between two clicks
    // exceeds the window, same shape as PhysicalKeyWatcher's Caps Lock
    // double-tap. TogglePause() still fires on every right-click
    // regardless (never suppressed, same reasoning as Caps Lock too), so
    // four taps land pause back in its original state — deliberately an
    // even count for exactly that reason.
    private const int ColorPopupTapWindowMs = 1000;
    private int _colorPopupTapCount;
    private DateTime _lastColorPopupTap = DateTime.MinValue;
    private Form? _colorPopup;

    public OverlayForm(VoiceEngine voice, MouseInputWatcher mouse, PhysicalKeyWatcher physical)
    {
        _voice = voice;
        _mouse = mouse;
        _physical = physical;

        // Pulled from the project file's <Version> at build time rather
        // than hardcoded here, so it never falls out of sync — bump it in
        // UnboundKeys.csproj and this picks it up automatically.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionText = version == null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";

        Text = $"UnboundKeys v{versionText}";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = true;
        Icon = LoadEmbeddedIcon("UnboundKeys.Assets.skull.ico");
        BackColor = Color.Black;
        DoubleBuffered = true; // cuts down on repaint artifacts while being dragged

        // A square window, exactly TabStripHeight tall — not the image's own
        // aspect ratio — so the dashboard's top strip and this icon are the
        // exact same height. Whenever the dashboard is collapsed down to
        // just that strip, its top and bottom line up with the icon's with
        // no extra padding needed on either side. StretchImage (below)
        // stretches the image itself to match, a minor trade-off for that
        // exact alignment.
        ClientSize = new Size(TargetWidth, TargetWidth);

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - ClientSize.Width - 20, screen.Top + 20);

        _icon = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.StretchImage,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
        };
        // Builds _listeningImage/_pausedImage from whichever color is
        // currently active and sets _icon.Image — the one-time initial
        // build here, and every later rebuild when the color changes (see
        // ThemeMode.Changed below), go through this same method.
        _listeningImage = null!;
        _pausedImage = null!;
        RebuildIconImages();
        ThemeMode.Changed += OnThemeChanged;

        new ToolTip().SetToolTip(_icon, $"UnboundKeys v{versionText}");
        // Left-button drag moves the whole icon; a left/right press that
        // never moves past DragThreshold still counts as a plain click
        // (dashboard toggle / pause toggle) same as before.
        _icon.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragMouseStart = e.Location;
                _dragging = false;
                // The icon is a tiny 60x60 control — a normal-speed real
                // mouse drag easily moves the cursor faster than 60px
                // between ticks, letting it slip outside the control's
                // own bounds mid-drag. Without capture, WinForms simply
                // stops delivering MouseMove (and even MouseUp) once the
                // cursor's left the control, freezing the drag wherever
                // it happened to be at that instant instead of tracking
                // the rest of the gesture. Capture keeps every mouse
                // event routed here regardless of where the cursor
                // actually is, for as long as the button stays down.
                _icon.Capture = true;
            }
        };
        _icon.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
                return;

            int dx = e.X - _dragMouseStart.X;
            int dy = e.Y - _dragMouseStart.Y;
            if (!_dragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold))
                _dragging = true;

            if (_dragging)
            {
                Location = ClampIconLocation(Location.X + dx, Location.Y + dy);
                RepositionDashboard();
                RepositionVirtualKeyboard();
                // Moving via Location alone can leave a stale sliver of
                // whatever was behind the icon's old position un-cleared —
                // forcing a full repaint after every move avoids that.
                Invalidate(true);
                Update();
            }
        };
        _icon.MouseUp += (_, e) =>
        {
            // Matches the Capture = true set on MouseDown — releases it
            // back once the gesture (drag or plain click) is actually
            // done, regardless of which branch below handles it.
            _icon.Capture = false;

            if (_dragging)
            {
                _dragging = false;
                return;
            }

            // While the color picker is showing, either button just
            // dismisses it instead of doing its normal thing — the same
            // "click away to cancel" a dropdown menu would give you,
            // rather than also opening the dashboard or toggling pause
            // as a side effect of the click that closed it.
            if (_colorPopup != null && !_colorPopup.IsDisposed)
            {
                _colorPopup.Close();
                return;
            }

            if (e.Button == MouseButtons.Left)
                ToggleDashboard();
            else if (e.Button == MouseButtons.Right)
            {
                TogglePause();
                HandleColorPopupTap();
            }
        };

        Controls.Add(_icon);

        // Keeps an open color popup glued to the icon whenever this window
        // itself moves (e.g. while it's being dragged) — same idea as
        // DashboardForm's own RepositionCategoryPopup, for the same reason:
        // without this, dragging the icon leaves the popup stranded at its
        // old position instead of following along.
        LocationChanged += (_, _) => RepositionColorPopup();

        // The dashboard's own Fade button (see FadeMode) fades itself, the
        // virtual keyboard, and the listener icon together — this is the
        // one place that actually owns all three, so it's where the effect
        // gets applied, to whichever of the two windows happen to be open
        // right now (the icon is always "open" — it just fades this form
        // itself, which is nothing but the icon — see the constructor).
        FadeMode.Changed += ApplyFadeToOpenWindows;

        FormClosed += (_, _) => Application.Exit();
    }

    private void ApplyFadeToOpenWindows()
    {
        double opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;
        Opacity = opacity;
        if (_dashboard != null && !_dashboard.IsDisposed)
            _dashboard.Opacity = opacity;
        if (_virtualKeyboard != null && !_virtualKeyboard.IsDisposed)
            _virtualKeyboard.Opacity = opacity;
    }

    private static Image LoadEmbeddedImage(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        return Image.FromStream(stream);
    }

    // Fills a new bitmap with backgroundColor, then draws the original image
    // over it with black (and near-black, for anti-aliased edges) treated as
    // transparent via a color key — so wherever the source was black, the
    // new background color shows through instead.
    private static Bitmap RecolorBackground(Image original, Color backgroundColor)
    {
        var bitmap = new Bitmap(original.Width, original.Height);
        using var g = Graphics.FromImage(bitmap);

        using (var bgBrush = new SolidBrush(backgroundColor))
            g.FillRectangle(bgBrush, 0, 0, bitmap.Width, bitmap.Height);

        using var attributes = new ImageAttributes();
        attributes.SetColorKey(Color.FromArgb(0, 0, 0), Color.FromArgb(20, 20, 20));
        g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
            0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

        return bitmap;
    }

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        return new Icon(stream);
    }

    private static Bitmap MakeDimmed(Image original)
    {
        var bitmap = new Bitmap(original.Width, original.Height);
        using var g = Graphics.FromImage(bitmap);

        var colorMatrix = new ColorMatrix(new float[][]
        {
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new float[] { 0, 0, 0, 0.5f, 0 },
            new float[] { 0, 0, 0, 0, 1 },
        });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(colorMatrix);

        g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
            0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);

        return bitmap;
    }

    // No left/right restriction at all anymore — Fizzil wants the icon
    // (and with it, the dashboard/keyboard, which flip to whichever side
    // actually fits — see RepositionDashboard/RepositionVirtualKeyboard)
    // freely draggable however close to, or past, either screen edge.
    // Bottom still reserves room for whichever of the dashboard/keyboard
    // is taller, since neither flips vertically — this is about the
    // *icon's* own vertical position, not a left/right restriction, so it
    // stays. Called with the icon's own current position to just clamp
    // it in place, or with a dragged-to position mid-drag.
    private Point ClampIconLocation(int desiredX, int desiredY)
    {
        var screen = Screen.PrimaryScreen!.WorkingArea;
        bool dashboardOpen = _dashboard != null && !_dashboard.IsDisposed;
        bool keyboardOpen = _virtualKeyboard != null && !_virtualKeyboard.IsDisposed;

        int x = desiredX;

        // Top: the dashboard's and keyboard's own tops always match the
        // icon's own top exactly, so one clamp covers all three.
        int y = Math.Max(desiredY, screen.Top);
        // Bottom: whichever of the icon, the dashboard (which can be much
        // taller than the icon while a card is open), or the keyboard
        // (taller still, full-size especially) reaches further down sets
        // the limit.
        int bottomHeight = Height;
        if (dashboardOpen)
            bottomHeight = Math.Max(bottomHeight, _dashboard!.Height);
        if (keyboardOpen)
            bottomHeight = Math.Max(bottomHeight, _virtualKeyboard!.Height);
        y = Math.Min(y, screen.Bottom - bottomHeight);

        return new Point(x, y);
    }

    private void ToggleDashboard()
    {
        if (_dashboard != null && !_dashboard.IsDisposed)
        {
            _dashboard.Close();
            // The minimized keyboard strip hugs the icon directly once
            // there's no dashboard left for it to sit next to — see
            // RepositionVirtualKeyboard.
            RepositionVirtualKeyboard();
            return;
        }

        _dashboard = new DashboardForm(ToggleVirtualKeyboard, IsVirtualKeyboardOpen);
        // Fresh open: start from the plain "does it fit normally" check
        // rather than remembering whichever side was chosen last time it
        // was open (see the hysteresis comment in RepositionDashboard).
        _dashboardFlipped = false;
        // The dashboard may be taller than whatever was open a moment ago
        // (if anything) — re-clamp the icon's own Y first, in case it's
        // now sitting too low for the dashboard to fit above the screen's
        // bottom edge. (Left/right need no such nudge — RepositionDashboard
        // itself flips sides instead if there's no room, see below.)
        Location = ClampIconLocation(Location.X, Location.Y);
        RepositionDashboard();
        // Picks up whatever Fade is already set to, rather than always
        // opening fully opaque until the next toggle.
        _dashboard.Opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;
        _dashboard.Show(this);
        // Pushes the minimized keyboard strip back out to its usual spot
        // next to the Keyboard button, now that the dashboard is back.
        RepositionVirtualKeyboard();
    }

    private bool IsVirtualKeyboardOpen() => _virtualKeyboard != null && !_virtualKeyboard.IsDisposed;

    // Deliberately independent of the dashboard's own lifetime: closing the
    // dashboard popup (left-clicking the icon again) leaves an already-open
    // keyboard floating on screen, since the whole point is a typing tool
    // that outlives the small config popup. A fresh one is built each time
    // it's reopened (same pattern as ToggleDashboard above), anchored to
    // the Keyboard button's position exactly like Mouse's own drawer is
    // anchored to the dashboard — see RepositionVirtualKeyboard.
    private void ToggleVirtualKeyboard()
    {
        if (_virtualKeyboard != null && !_virtualKeyboard.IsDisposed)
        {
            _virtualKeyboard.Close();
            return;
        }

        OpenVirtualKeyboard(startMini: false);
    }

    // Split out of ToggleVirtualKeyboard so OnThemeChanged can reopen the
    // keyboard in whichever of Mini/Maxi it was already in while comparing
    // colors, instead of always starting fresh at full size — see
    // OnThemeChanged's own comment for why that reopen happens at all.
    private void OpenVirtualKeyboard(bool startMini)
    {
        _virtualKeyboard = new VirtualKeyboardForm(startMini);
        // Fresh open: start from the plain "does it fit normally" check
        // rather than remembering whichever side was chosen last time it
        // was open (see the hysteresis comment in RepositionVirtualKeyboard).
        _keyboardFlipped = false;
        // Toggling Mini/Maxi resizes the keyboard itself (see
        // VirtualKeyboardForm.ToggleMiniMode) — full-size is much taller
        // than mini, so re-clamping the icon's own Y here too (not just
        // on open) keeps it from ending up too low for the taller
        // full-size keyboard to fit.
        _virtualKeyboard.SizeChanged += (_, _) =>
        {
            Location = ClampIconLocation(Location.X, Location.Y);
            RepositionDashboard();
            RepositionVirtualKeyboard();
        };
        // Same Y-only nudge as ToggleDashboard's own call to this — the
        // keyboard may be taller than whatever was open a moment ago.
        Location = ClampIconLocation(Location.X, Location.Y);
        RepositionVirtualKeyboard();
        // Picks up whatever Fade is currently set to (it was last toggled
        // from this same window, so this only matters on the very next
        // reopen), rather than always starting fully opaque.
        _virtualKeyboard.Opacity = FadeMode.IsOn ? FadeMode.FadedOpacity : 1.0;
        _virtualKeyboard.Show(this);
    }

    // The keyboard's own top-right corner normally lines up with the
    // Keyboard button's top-left while the dashboard is open — computed
    // purely from the icon's own position and DashboardForm's fixed
    // layout constants (Keyboard is the leftmost pixel of the fixed
    // group, whose right edge always sits flush against the icon's own
    // left edge — see DashboardForm's class comment), not from a live
    // DashboardForm instance, since the keyboard can be open while the
    // dashboard itself is closed. In this arrangement the keyboard
    // actually sits a bit further from the icon than the dashboard's own
    // edge does (its own left edge reaches past where the invisible,
    // collapsed drawer zone would be) — it's "attached" to the Keyboard
    // button's position, not the dashboard's outer edge.
    //
    // While the dashboard is actually closed, the keyboard (mini or full
    // — both the same width, see FormWidth/MiniHeight) instead anchors
    // flush against the icon directly, since there's no dashboard left
    // for it to sit next to. Same story when the dashboard is open but on
    // the *opposite* side from where the keyboard ends up — attaching
    // beyond the dashboard's edge only makes sense when they're on the
    // same side; otherwise the keyboard bypasses the dashboard and
    // attaches straight to the icon.
    //
    // Left and right are each judged independently by whether *that*
    // position is still fully on screen (mirrored, with its own
    // hysteresis — see RepositionDashboard for why a single shared
    // threshold flip-flops), rather than one flip decision borrowed from
    // the dashboard's own — that was the bug that let the keyboard run
    // straight off the right edge while just following a flipped
    // dashboard's edge with no check of its own.
    //
    // Called when the keyboard first opens, continuously while the icon
    // is being dragged (see the icon's MouseMove handler), on every
    // Mini/Maxi toggle (see VirtualKeyboardForm's own SizeChanged
    // wiring), and on every dashboard open/close (see ToggleDashboard) —
    // any of those can change which side ends up applying.
    private void RepositionVirtualKeyboard()
    {
        if (_virtualKeyboard == null || _virtualKeyboard.IsDisposed)
            return;

        bool dashboardOpen = _dashboard != null && !_dashboard.IsDisposed;
        var screen = Screen.PrimaryScreen!.WorkingArea;

        // When the keyboard lands on the same side as the dashboard, it
        // attaches beyond the dashboard's own edge rather than overlapping
        // it; when it lands on the *opposite* side, there's no dashboard
        // edge over there to line up with, so it attaches directly to the
        // icon instead. _dashboardFlipped is set by RepositionDashboard
        // (always called first at every call site) — a direct read of
        // which side the dashboard actually chose, rather than re-deriving
        // it from position math a second time.
        int leftCandidate = dashboardOpen && !_dashboardFlipped
            ? Left - DashboardForm.FixedGroupWidth - _virtualKeyboard.Width
            : Left - _virtualKeyboard.Width;
        // Same idea as the dashboard's own VisibleRightInset use: its right
        // edge can be sitting behind an invisible zone (ProfileExtraWidth,
        // unless Profile's own dropdown is open).
        int rightCandidate = dashboardOpen && _dashboardFlipped
            ? _dashboard!.Left + _dashboard.Width - _dashboard.VisibleRightInset
            : Left + Width;

        // Same mirrored, hysteresis-based left/right choice as
        // RepositionDashboard — each side judged by whether *it* still
        // fits, not by whether the other side has also become valid (that
        // was the bug: the keyboard used to always follow a flipped
        // dashboard's edge with no check that doing so stayed on screen,
        // so it could run straight off the right edge). _keyboardFlipped
        // remembers which side was chosen last call.
        bool useFlipped = _keyboardFlipped
            ? rightCandidate + _virtualKeyboard.Width <= screen.Right
            : leftCandidate < screen.Left;
        _keyboardFlipped = useFlipped;

        int x = useFlipped ? rightCandidate : leftCandidate;
        _virtualKeyboard.Location = new Point(x, Top - DashboardForm.TopInset);
    }

    // Flush against the icon, not floating near it: the dashboard's right
    // edge touches the icon's left edge exactly (no gap), and their tops
    // line up exactly (TopInset is 0 now that the dashboard has no outer
    // padding of its own to offset for) — unless that would put the
    // dashboard's own left edge off the screen, in which case it flips to
    // attach to the icon's *right* edge instead. When flipped, it's not
    // the window's own left edge that needs to touch the icon — it's
    // whichever content is actually VISIBLE there right now. Collapsed
    // (or Profile's dropdown open), that's the fixed group, DrawerWidth
    // in from the window's own edge, since the drawer zone itself is
    // invisible (Region-cut) in both those states; with Voice/Mouse's own
    // drawer open, the drawer *is* the visible content, flush with the
    // window's edge already. DashboardForm.VisibleLeftInset tracks which
    // applies. Called both when the dashboard first opens and continuously
    // while the icon is being dragged, so it always stays glued to the
    // icon's current position, on whichever side currently fits.
    private void RepositionDashboard()
    {
        if (_dashboard == null || _dashboard.IsDisposed)
            return;

        var screen = Screen.PrimaryScreen!.WorkingArea;
        int leftSideX = Left - DashboardForm.LeftEdgeOffsetFromIcon;
        // Only the *visible* left edge needs to stay on screen — the drawer
        // zone folded in behind it (VisibleLeftInset) is Region-cut and
        // invisible whenever collapsed, so there's nothing wrong with it
        // poking off-screen. Checking the raw window edge instead (as
        // before) flipped a full drawer-width earlier than necessary,
        // which is why the flipped dashboard used to land a whole
        // dashboard's width away from the edge it had just left.
        int visibleLeftEdge = leftSideX + _dashboard.VisibleLeftInset;

        // Mirrored check for the flipped side: how far flipped mode's own
        // *visible* content reaches to the right of the icon, so leaving
        // flipped mode can be judged by whether flipped mode itself is
        // still fully on screen — not by whether normal mode has also
        // become valid again. Those two conditions cross at very
        // different icon positions (normal mode only needs ~480px of
        // room; flipped mode's visible slice is far narrower), so basing
        // the un-flip on "does normal mode fit" was leaving flipped mode
        // a couple of dashboard-widths before the icon ever got near the
        // right edge.
        int visibleWidth = _dashboard.Width - _dashboard.VisibleLeftInset - _dashboard.VisibleRightInset;
        int flippedVisibleRightEdge = Left + Width + visibleWidth;

        // Hysteresis: each side is judged by whether *it* still fits, not
        // by whether the other side has also become valid — otherwise a
        // wide overlapping range where both fit would flip back the
        // instant the icon crossed the (arbitrary) threshold used to
        // enter the mode in the first place, even by a pixel.
        // _dashboardFlipped (not a position comparison — see
        // RepositionVirtualKeyboard) is the memory of which side was
        // chosen last call.
        bool useFlipped = _dashboardFlipped
            ? flippedVisibleRightEdge <= screen.Right
            : visibleLeftEdge < screen.Left;
        _dashboardFlipped = useFlipped;
        // Mirrors the dashboard's own internal layout (Profile nearest the
        // icon, Keyboard at the outer edge, either way — see DashboardForm's
        // Mirrored property) — set before reading VisibleLeftInset below,
        // since which side is invisible flips along with it.
        _dashboard.Mirrored = useFlipped;

        int x = useFlipped ? Left + Width - _dashboard.VisibleLeftInset : leftSideX;
        _dashboard.Location = new Point(x, Top - DashboardForm.TopInset);
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            _voice.Pause();
            _mouse.Pause();
            _physical.Pause();
            _icon.Image = _pausedImage;
            // Otherwise a key left mid-infinite-hold/repeat would stay stuck
            // that way — with listening off, there's no way to say the word
            // (or press the button) again to release it.
            KeyExecutor.ReleaseAll();
        }
        else
        {
            _voice.Resume();
            _mouse.Resume();
            _physical.Resume();
            _icon.Image = _listeningImage;
        }
    }

    // Builds _listeningImage/_pausedImage from whichever color's skull
    // asset ThemeMode.Current currently names, through the same crop-free
    // recolor-then-dim pipeline as always — RecolorBackground only ever
    // touches the background behind the transparent-keyed skull, so it
    // naturally keeps working once handed a different source image. Called
    // once up front (see the constructor) and again every time the color
    // changes (see OnThemeChanged).
    private void RebuildIconImages()
    {
        _listeningImage = BuildSkullImage(ThemeMode.Current);
        _pausedImage = MakeDimmed(_listeningImage);
        _icon.Image = _paused ? _pausedImage : _listeningImage;
    }

    // The "listening" rendering of one color's skull, background-blended
    // to match the current theme's button color — the same image
    // RebuildIconImages uses for whichever color is actually active,
    // shared with ThemeColorPopup so its swatches show the real skull
    // art (not just a color name) for every color, active or not.
    internal static Image BuildSkullImage(string colorName)
    {
        var resourceName = $"UnboundKeys.Assets.skull-{colorName.ToLowerInvariant()}.png";
        var source = LoadEmbeddedImage(resourceName);
        return RecolorBackground(source, Theme.Current.Button);
    }

    // Reacts to a color change from anywhere — the popup below, or a
    // profile switch happening inside an already-open dashboard (see
    // DashboardForm.SwitchToProfile). Rebuilding the icon images is always
    // safe to do immediately, but closing _dashboard/_virtualKeyboard is
    // deferred via BeginInvoke: ThemeMode.Changed can fire from partway
    // through SwitchToProfile's own call stack, and disposing a Form while
    // it's still mid-method on its own stack is a real hazard, not just
    // untidy. A fresh, correctly-colored window gets built the next time
    // each is opened — a fully seamless in-place re-theme of an already-
    // open one is a bigger change than this feature calls for, for either
    // window.
    //
    // The one exception: while the color popup is still open (mid-
    // comparison — ThemeColorPopup never closes itself on a click, only
    // rebuilds its own rows), the dashboard AND the keyboard (whichever of
    // Mini/Maxi it was already in) immediately reopen fresh right after
    // closing, so clicking through colors reads as the whole app re-
    // coloring on the fly rather than repeatedly vanishing. Whatever
    // drawer/card was open on the dashboard collapses on each reopen —
    // preserving that too would mean tracking and replaying UI state, a
    // bigger change than this comparison feature calls for.
    private void OnThemeChanged()
    {
        RebuildIconImages();

        bool comparingColors = _colorPopup != null && !_colorPopup.IsDisposed;

        if (_dashboard != null && !_dashboard.IsDisposed)
        {
            var dashboard = _dashboard;
            BeginInvoke(() =>
            {
                if (!dashboard.IsDisposed)
                    dashboard.Close();
                if (comparingColors)
                    ToggleDashboard();
            });
        }

        if (_virtualKeyboard != null && !_virtualKeyboard.IsDisposed)
        {
            var keyboard = _virtualKeyboard;
            bool wasMini = keyboard.IsMini;
            BeginInvoke(() =>
            {
                if (!keyboard.IsDisposed)
                    keyboard.Close();
                if (comparingColors)
                    OpenVirtualKeyboard(wasMini);
            });
        }
    }

    // Four rapid right-clicks opens the color picker instead of just
    // pausing — see the field comments up top for why this doesn't
    // suppress the normal pause toggle.
    private void HandleColorPopupTap()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastColorPopupTap).TotalMilliseconds > ColorPopupTapWindowMs)
            _colorPopupTapCount = 0;
        _colorPopupTapCount++;
        _lastColorPopupTap = now;

        if (_colorPopupTapCount < 4)
            return;

        _colorPopupTapCount = 0;

        _colorPopup?.Close();
        _colorPopup = ThemeColorPopup.Show(this, _icon, ThemeMode.SwitchTo);
    }

    // Called whenever this window moves (see LocationChanged in the
    // constructor). Without this, dragging the icon around would leave an
    // open color popup stranded at its old position, disconnected from
    // the icon it belongs to.
    private void RepositionColorPopup()
    {
        if (_colorPopup != null && !_colorPopup.IsDisposed)
            ThemeColorPopup.Reposition(_colorPopup, _icon);
    }
}
