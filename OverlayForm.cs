using System.Drawing.Imaging;
using System.Reflection;

namespace VoicePress;

// The overlay is just the skull image, sitting in the corner. Click it to
// toggle listening on/off (dimmed = paused). Close via the taskbar icon.
public sealed class OverlayForm : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int TargetWidth = 140;

    private readonly VoiceEngine _voice;
    private readonly PictureBox _icon;
    private readonly Image _listeningImage;
    private readonly Image _pausedImage;
    private bool _paused;
    private DashboardForm? _dashboard;

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

    public OverlayForm(VoiceEngine voice)
    {
        _voice = voice;

        Text = "VoicePress";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = true;
        Icon = LoadEmbeddedIcon("VoicePress.Assets.skull.ico");
        BackColor = Color.Black;

        _listeningImage = LoadEmbeddedImage("VoicePress.Assets.skull.png");
        _pausedImage = MakeDimmed(_listeningImage);

        int targetHeight = (int)(_listeningImage.Height * (TargetWidth / (double)_listeningImage.Width));
        ClientSize = new Size(TargetWidth, targetHeight);

        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(screen.Right - ClientSize.Width - 20, screen.Top + 20);

        _icon = new PictureBox
        {
            Image = _listeningImage,
            SizeMode = PictureBoxSizeMode.StretchImage,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
        };
        _icon.MouseUp += (_, e) =>
        {
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
        int x = Math.Max(Screen.PrimaryScreen!.WorkingArea.Left, Left - _dashboard.Width - 10);
        _dashboard.Location = new Point(x, Top - DashboardForm.TopInset);
        _dashboard.Show(this);
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            _voice.Pause();
            _icon.Image = _pausedImage;
        }
        else
        {
            _voice.Resume();
            _icon.Image = _listeningImage;
        }
    }
}
