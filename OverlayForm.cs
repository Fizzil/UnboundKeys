using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace VoicePress;

// The overlay is just the skull image, sitting in the corner. Click it to
// toggle listening on/off (dimmed = paused). Close via the taskbar icon.
public sealed class OverlayForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
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
    private readonly PictureBox _icon;
    private readonly Image _listeningImage;
    private readonly Image _pausedImage;
    private bool _paused;
    private DashboardForm? _dashboard;
    private Point _dragMouseStart;
    private bool _dragging;

    // Keeps this window from ever taking keyboard focus, so clicking it can never
    // steal focus away from the game.
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    // Windows enforces a minimum trackable window size (well over 100px
    // wide) on every top-level window by default, regardless of how small
    // ClientSize is set to — below that floor, the OS silently widens the
    // real window back up, which is exactly what stretched the shrunk skull
    // image sideways. Intercepting WM_GETMINMAXINFO and reporting a tiny
    // minimum lets this window actually be as small as TargetWidth asks.
    protected override void WndProc(ref Message m)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (m.Msg == WM_GETMINMAXINFO)
        {
            var info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO))!;
            info.ptMinTrackSize.X = 1;
            info.ptMinTrackSize.Y = 1;
            Marshal.StructureToPtr(info, m.LParam, true);
        }
        base.WndProc(ref m);
    }

    public OverlayForm(VoiceEngine voice, MouseInputWatcher mouse)
    {
        _voice = voice;
        _mouse = mouse;

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

        // The source asset has a solid gray band baked into its bottom ~9
        // pixels (a leftover crop artifact from however it was made) —
        // invisible back when the icon was shown at its natural aspect
        // ratio, but stretched into a visible strip once the icon became a
        // fixed square. Cropped off here rather than editing the asset
        // file itself.
        // The asset's background is plain black, which reads as a stark
        // square next to the dashboard's own dark-gray button color once
        // they're sitting flush against each other — recoloring it to that
        // same gray blends the icon into the dashboard instead of clashing
        // with it.
        var cropped = CropBottom(LoadEmbeddedImage("VoicePress.Assets.skull.png"), 10);
        _listeningImage = RecolorBackground(cropped, Theme.Current.Button);
        _pausedImage = MakeDimmed(_listeningImage);

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
            Image = _listeningImage,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
        };
        new ToolTip().SetToolTip(_icon, $"VoicePress v{versionText}");
        // Left-button drag moves the whole icon; a left/right press that
        // never moves past DragThreshold still counts as a plain click
        // (pause toggle / dashboard toggle) same as before.
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

            if (e.Button == MouseButtons.Left)
                TogglePause();
            else if (e.Button == MouseButtons.Right)
                ToggleDashboard();
        };

        Controls.Add(_icon);

        FormClosed += (_, _) => Application.Exit();
    }

    private static Image LoadEmbeddedImage(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        return Image.FromStream(stream);
    }

    private static Bitmap CropBottom(Image original, int pixels)
    {
        int newHeight = original.Height - pixels;
        var bitmap = new Bitmap(original.Width, newHeight);
        using var g = Graphics.FromImage(bitmap);
        g.DrawImage(original, new Rectangle(0, 0, original.Width, newHeight),
            0, 0, original.Width, newHeight, GraphicsUnit.Pixel);
        return bitmap;
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
            _icon.Image = _listeningImage;
        }
    }
}
