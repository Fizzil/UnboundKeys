using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;

namespace UnboundKeys;

// The app's presence while its dashboard is hidden: an icon in the
// system tray, the way Discord and Steam live there — the overlay skull
// that used to float over the game is gone (Fizzil's call; it was the
// v0.1 way of having a presence on screen). Left-click only: it toggles
// the dashboard. No right-click menu, because a remapped right button is
// swallowed by the mouse hook even over the tray; Quit lives in the
// dashboard's Settings instead. Dims while listening is paused.
//
// Built on WinForms' NotifyIcon — WPF has no tray icon of its own, and
// NotifyIcon only needs a message loop, which WPF's dispatcher provides.
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _listening;
    private readonly Icon _paused;

    public event Action? Clicked;

    public TrayIcon()
    {
        _listening = LoadEmbeddedIcon("UnboundKeys.Assets.skull.ico");
        _paused = MakeDimmed(_listening);
        _icon = new NotifyIcon { Icon = _listening, Text = "UnboundKeys — listening", Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                Clicked?.Invoke();
        };
    }

    public void SetPaused(bool paused)
    {
        _icon.Icon = paused ? _paused : _listening;
        _icon.Text = paused ? "UnboundKeys — voice paused" : "UnboundKeys — listening";
    }

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
