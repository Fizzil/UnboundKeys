using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace UnboundKeys;

// The tray icon on Windows (see TrayIcon for what it says and when).
// Built on WinForms' NotifyIcon — WPF has no tray icon of its own, and
// NotifyIcon only needs a message loop, which WPF's dispatcher provides.
internal sealed class WindowsTray : ITrayShell
{
    private readonly NotifyIcon _icon;
    private readonly Icon _bright;
    private readonly Icon _dimmed;

    public event Action? Clicked;

    public WindowsTray()
    {
        _bright = LoadEmbeddedIcon("UnboundKeys.Assets.skull.ico");
        _dimmed = MakeDimmed(_bright);
        _icon = new NotifyIcon { Icon = _bright, Text = "UnboundKeys", Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Clicked?.Invoke();
        };
    }

    public void Show(bool dimmed, string tooltip)
    {
        _icon.Icon = dimmed ? _dimmed : _bright;
        _icon.Text = tooltip;
    }

    public void Notify(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private static Icon LoadEmbeddedIcon(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        // At the size the tray actually draws (20 px at 125% scaling), so
        // Windows picks that frame of the icon instead of shrinking a big one.
        return new Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
    }

    // Greyed and half-transparent — the same treatment the old overlay
    // gave its paused state.
    private static Icon MakeDimmed(Icon original)
    {
        using var source = original.ToBitmap();
        using var dimmed = new Bitmap(source.Width, source.Height);
        using var g = Graphics.FromImage(dimmed);

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
        g.DrawImage(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);

        return Icon.FromHandle(dimmed.GetHicon());
    }
}
