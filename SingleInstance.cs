using System.Runtime.InteropServices;

namespace UnboundKeys;

// One copy at a time. A second launch (the shortcut clicked again after the
// dashboard was "lost" to the tray, say) used to start a second app with a
// second set of hooks, so every remapped key fired twice. Now the first
// copy holds a named mutex; a later one finds it held, asks the running
// copy to show its dashboard (a registered window message, broadcast to
// every top-level window — both copies run elevated, so it gets through),
// and quits before touching a hook.
public static class SingleInstance
{
    private const string MutexName = "UnboundKeys.SingleInstance";

    public static readonly int ShowDashboardMessage = RegisterWindowMessage("UnboundKeys.ShowDashboard");

    private static Mutex? _held;

    // True if this is the first copy (and the mutex is now held for the
    // life of the process); false if another copy already runs.
    public static bool Claim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (createdNew)
        {
            _held = mutex;
            return true;
        }
        mutex.Dispose();
        return false;
    }

    public static void AskRunningCopyToShowDashboard() =>
        PostMessage(HWND_BROADCAST, ShowDashboardMessage, IntPtr.Zero, IntPtr.Zero);

    private static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
