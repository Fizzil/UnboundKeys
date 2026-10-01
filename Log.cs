using System.IO;

namespace UnboundKeys;

// The action logger: a small log beside the settings file, used for
// debugging and for building new features (a professional review's first
// red flag: errors were swallowed without a trace). OFF unless the user
// switches it on (Settings > Action logger; Fizzil: an app should not keep
// a file about what you do unless you asked it to), and every line goes
// through Write below, so while it is off nothing at all is written,
// errors and crashes included. %AppData%\UnboundKeys\log.txt, plain text,
// one line per event; at 256 KB it rolls over to log.txt.old, so it can
// never grow without bound. What goes in: starts, stops, errors, and the
// changes made in the dashboard (a mapping's keys or mode, the profile,
// the dashboard shown or hidden). Never what is typed or said.
public static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    // Set from the saved setting at startup (Program.cs) and by the
    // Settings switch. Not read from Settings here: Settings logs its own
    // errors, and the two must not wait on each other to initialize.
    public static bool Enabled { get; set; }

    public static string FilePath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnboundKeys", "log.txt");

    // For the harness in tests/ only, so its deliberate errors stay out of
    // the real log.
    internal static void UseScratchFile(string path) => FilePath = path;

    // Reset app (Settings > Reset): the log and its rolled-over copy are
    // deleted, and logging stops with them.
    public static void DeleteFiles()
    {
        Enabled = false;
        try
        {
            lock (Gate)
            {
                File.Delete(FilePath);
                File.Delete(FilePath + ".old");
            }
        }
        catch
        {
            // The log must never be the thing that breaks the app.
        }
    }

    public static void Info(string message) => Write("info ", message);

    public static void Error(string message) => Write("error", message);

    // The short form, for an error the app recovers from.
    public static void Error(string where, Exception ex) =>
        Write("error", $"{where}: {ex.GetType().Name}: {ex.Message}");

    // The whole exception with its stack, for one nothing caught.
    public static void Crash(string where, Exception ex) =>
        Write("crash", $"{where}: {ex}");

    private static void Write(string level, string message)
    {
        if (!Enabled)
            return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    string old = FilePath + ".old";
                    File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // The log must never be the thing that breaks the app.
        }
    }
}
