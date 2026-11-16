using System.IO;
using System.Runtime.InteropServices;

namespace UnboundKeys;

// Where shared profile files live (see Settings.ExportProfile and
// ImportProfile for what is in one). There is no file picker anywhere:
// a Windows Save or Open dialog takes the focus and wants a typed name,
// and this app is used with the mouse alone. So Share always writes to
// one fixed folder and shows the file there, and Import lists what it
// finds in that folder and in Downloads, where a file sent by a friend
// lands.
internal static class ProfileShare
{
    // How many files the import list shows, newest first.
    public const int MaxListed = 8;

    // For the harness in tests/ only: both folders are these instead, and
    // no Explorer window is opened, so its tests never reach Documents or
    // Downloads.
    private static string? _scratchShare;
    private static string? _scratchDownloads;

    internal static void UseScratchFolders(string share, string downloads)
    {
        _scratchShare = share;
        _scratchDownloads = downloads;
    }

    // Documents\UnboundKeys: where Share puts its files.
    public static string ShareFolder => _scratchShare ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UnboundKeys");

    // The user's Downloads folder, wherever they keep it. .NET has no
    // SpecialFolder for it, so Windows is asked directly; the usual place
    // is the answer if that fails.
    public static string DownloadsFolder
    {
        get
        {
            if (_scratchDownloads != null)
                return _scratchDownloads;
            try
            {
                if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out IntPtr found) == 0)
                {
                    string? path = Marshal.PtrToStringUni(found);
                    Marshal.FreeCoTaskMem(found);
                    if (!string.IsNullOrEmpty(path))
                        return path;
                }
            }
            catch (Exception ex)
            {
                Log.Error("asking Windows for the Downloads folder", ex);
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    // The profile files waiting to be imported: Downloads, then the share
    // folder.
    public static List<(string Path, string Name)> FindShared() => Find(DownloadsFolder, ShareFolder);

    // Every real profile file directly inside those folders, newest first,
    // with the profile's name from inside it, cut to MaxListed. Files that
    // only have the right ending are left out, and a folder that is
    // missing or cannot be read is skipped.
    public static List<(string Path, string Name)> Find(params string[] folders)
    {
        var files = new List<FileInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*" + Settings.SharedFileExtension))
                    if (file.Extension.Equals(Settings.SharedFileExtension, StringComparison.OrdinalIgnoreCase) && seen.Add(file.FullName))
                        files.Add(file);
            }
            catch (Exception ex)
            {
                Log.Error($"looking for profile files in {folder}", ex);
            }
        }

        var found = new List<(string Path, string Name)>();
        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc))
        {
            if (found.Count >= MaxListed)
                break;
            if (Settings.SharedProfileName(file.FullName) is { } name)
                found.Add((file.FullName, name));
        }
        return found;
    }

    // Opens the file's folder in Explorer with the file selected, ready to
    // be dragged to a friend.
    public static void ShowInExplorer(string path)
    {
        if (_scratchShare != null)
            return;
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("showing the shared profile in Explorer", ex);
        }
    }
}
