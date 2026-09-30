using System.IO;

namespace UnboundKeys;

// A small log beside the settings file, so a problem on someone else's
// machine leaves something to look at (a professional review's first red
// flag: errors were swallowed without a trace). The app still keeps going
// wherever it did before; it now also writes down what went wrong.
// %AppData%\UnboundKeys\log.txt, plain text, one line per event; at 256 KB
// it rolls over to log.txt.old, so it can never grow without bound. Nothing
// typed, spoken or mapped is ever written here: only starts, stops and
// errors.
public static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnboundKeys", "log.txt");

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
