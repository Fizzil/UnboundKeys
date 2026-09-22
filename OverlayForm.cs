using System.Drawing.Imaging;
using System.Reflection;

namespace VoicePress;

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
        // VoicePress.csproj and this picks it up automatically.
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionText = version == null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";

        Text = $"VoicePress v{versionText}";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = true;
        Icon = LoadEmbeddedIcon("VoicePress.Assets.skull.ico");
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

        new ToolTip().SetToolTip(_icon, $"VoicePress v{versionText}");
        // Left-button drag moves the whole icon; a left/right press that
        // never moves past DragThreshold still counts as a plain click
        // (dashboard toggle / pause toggle) same as before.
        _icon.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragMouseStart = e.Location;
                _dragging = false;
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
                var screen = Screen.PrimaryScreen!.WorkingArea;
                bool dashboardOpen = _dashboard != null && !_dashboard.IsDisposed;
                int newX = Location.X + dx;
                int newY = Location.Y + dy;

                // Left: the dashboard (when open) can't be dragged past the
                // screen's left edge — RepositionDashboard clamps it there.
                // Without a matching clamp here, the icon would keep going
                // past that point on its own, ending up dragged behind the
                // now-stuck dashboard instead of staying glued to its edge.
                int minX = screen.Left + (dashboardOpen ? DashboardForm.LeftEdgeOffsetFromIcon : 0);
                newX = Math.Max(newX, minX);

                // Right: the icon's own right edge is the rightmost point of
                // the combined shape — the dashboard only ever extends to
                // its left, never past it.
                newX = Math.Min(newX, screen.Right - Width);

                // Top: the dashboard's top always matches the icon's own
                // top exactly, so one clamp covers both.
                newY = Math.Max(newY, screen.Top);

                // Bottom: whichever of the icon or the dashboard (which can
                // be much taller than the icon while a card is open) reaches
                // further down sets the limit.
                int bottomHeight = dashboardOpen ? Math.Max(Height, _dashboard!.Height) : Height;
                newY = Math.Min(newY, screen.Bottom - bottomHeight);

                Location = new Point(newX, newY);
                RepositionDashboard();
                // Moving via Location alone can leave a stale sliver of
                // whatever was behind the icon's old position un-cleared —
                // forcing a full repaint after every move avoids that.
                Invalidate(true);
                Update();
            }
        };
        _icon.MouseUp += (_, e) =>
        {
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

        FormClosed += (_, _) => Application.Exit();
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

    private void ToggleDashboard()
    {
        if (_dashboard != null && !_dashboard.IsDisposed)
        {
            _dashboard.Close();
            return;
        }

        _dashboard = new DashboardForm();
        RepositionDashboard();
        _dashboard.Show(this);
    }

    // Flush against the icon, not floating near it: the dashboard's right
    // edge touches the icon's left edge exactly (no gap), and their tops
    // line up exactly (TopInset is 0 now that the dashboard has no outer
    // padding of its own to offset for). Called both when the dashboard
    // first opens and continuously while the icon is being dragged, so it
    // always stays glued to the icon's current position.
    private void RepositionDashboard()
    {
        if (_dashboard == null || _dashboard.IsDisposed)
            return;

        int x = Math.Max(Screen.PrimaryScreen!.WorkingArea.Left, Left - DashboardForm.LeftEdgeOffsetFromIcon);
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
        var resourceName = $"VoicePress.Assets.skull-{colorName.ToLowerInvariant()}.png";
        var source = LoadEmbeddedImage(resourceName);
        return RecolorBackground(source, Theme.Current.Button);
    }

    // Reacts to a color change from anywhere — the popup below, or a
    // profile switch happening inside an already-open dashboard (see
    // DashboardForm.SwitchToProfile). Rebuilding the icon images is always
    // safe to do immediately, but closing _dashboard is deferred via
    // BeginInvoke: ThemeMode.Changed can fire from partway through
    // SwitchToProfile's own call stack, and disposing a Form while it's
    // still mid-method on its own stack is a real hazard, not just untidy.
    // A fresh, correctly-colored dashboard gets built the next time it's
    // opened — a fully seamless in-place re-theme of an already-open one
    // is a bigger change than this feature calls for.
    private void OnThemeChanged()
    {
        RebuildIconImages();

        if (_dashboard != null && !_dashboard.IsDisposed)
        {
            var dashboard = _dashboard;
            BeginInvoke(() =>
            {
                if (!dashboard.IsDisposed)
                    dashboard.Close();
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
