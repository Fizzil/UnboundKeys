namespace UnboundKeys;

// Handing something to the rest of the desktop: a web page to the
// browser, a file to whatever opens it, a folder to the file manager.
// How is the system's business (WindowsOpener on Windows). Each call may
// throw; the caller says what it was trying to open in the log.
internal interface IOpener
{
    void Link(string url);
    void File(string path);
    void Folder(string folder);
    // The file manager, with this file selected in its folder.
    void ShowFile(string path);
}

internal static class Opener
{
    private static IOpener _opener = new WindowsOpener();

    // Swaps the opener in use and hands back the old one (the tests).
    internal static IOpener Use(IOpener opener)
    {
        var old = _opener;
        _opener = opener;
        return old;
    }

    public static void OpenLink(string url) => _opener.Link(url);
    public static void OpenFile(string path) => _opener.File(path);
    public static void OpenFolder(string folder) => _opener.Folder(folder);
    public static void ShowFile(string path) => _opener.ShowFile(path);
}
