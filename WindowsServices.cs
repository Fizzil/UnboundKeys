using System.Diagnostics;
using System.IO;
using System.Media;

namespace UnboundKeys;

// The small Windows answers that need no file of their own: opening
// things (see Opener) and playing a short sound (see KeyClick).

internal sealed class WindowsOpener : IOpener
{
    // The shell picks the program: the default browser for a link, the
    // associated editor for a file.
    public void Link(string url) => Start(new ProcessStartInfo(url) { UseShellExecute = true });
    public void File(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void Folder(string folder) =>
        Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });

    public void ShowFile(string path) =>
        Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    private static void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();
}

// SoundPlayer, the one Windows player quick enough to keep up with
// typing; each new sound cuts the last one short. The player is built
// once for a sound and kept while the same sound keeps being asked for.
internal sealed class WindowsSoundOut : ISoundOut
{
    private readonly object _gate = new();
    private byte[]? _wav;
    private SoundPlayer? _player;

    public void Play(byte[] wav)
    {
        SoundPlayer player;
        lock (_gate)
        {
            if (_player == null || !ReferenceEquals(_wav, wav))
            {
                _player?.Dispose();
                _player = new SoundPlayer(new MemoryStream(wav));
                _player.Load();
                _wav = wav;
            }
            player = _player;
        }
        player.Play();
    }
}
