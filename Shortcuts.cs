using System.IO;

namespace UnboundKeys;

// Every release has its own exe name (UnboundKeys-v4.2.1.exe), so a
// shortcut made to one release keeps opening that release after an
// update (Fizzil's UBK shortcut). Each time the app starts, any shortcut
// on the desktop, in the Start menu or pinned to the taskbar that points
// at an OLDER UnboundKeys exe is pointed at this one instead — never at a
// newer one, so running an old build on purpose leaves the shortcut
// alone. Same rule, same occasion as the Start with Windows task (see
// StartupTask). Shortcuts are edited through the shell's own object
// (WScript.Shell), late-bound, since .NET has no .lnk writer of its own.
public static class Shortcuts
{
    public static void PointAtThisVersion()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || ParseVersion(Path.GetFileName(exe)) is not Version mine)
            return;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                return;
            dynamic shell = Activator.CreateInstance(shellType)!;

            foreach (string link in CandidateLinks())
            {
                try
                {
                    dynamic shortcut = shell.CreateShortcut(link);
                    string target = (string)shortcut.TargetPath;
                    if (ParseVersion(Path.GetFileName(target)) is not Version theirs || theirs >= mine)
                        continue;
                    shortcut.TargetPath = exe;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
                    shortcut.IconLocation = exe + ",0";
                    shortcut.Save();
                }
                catch
                {
                    // One shortcut that will not read or save never stops the rest.
                }
            }
        }
        catch
        {
            // No shell object to edit shortcuts with: nothing to do.
        }
    }

    private static IEnumerable<string> CandidateLinks()
    {
        var folders = new (string Path, bool Recurse)[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.Programs), true),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), true),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"), false),
        };

        foreach (var (folder, recurse) in folders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                continue;
            string[] links;
            try
            {
                links = Directory.GetFiles(folder, "*.lnk", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue; // a folder that will not list is skipped whole
            }
            foreach (string link in links)
                yield return link;
        }
    }

    // "UnboundKeys-v4.2.1.exe" -> 4.2.1; anything else -> null.
    private static Version? ParseVersion(string fileName)
    {
        const string prefix = "UnboundKeys-v";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return null;
        return Version.TryParse(fileName[prefix.Length..^4], out var version) ? version : null;
    }
}
