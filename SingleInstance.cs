using System.Runtime.InteropServices;

namespace UnboundKeys;

// One copy at a time. A second launch (the shortcut clicked again after the
// dashboard was "lost" to the tray, say) used to start a second app with a
// second set of hooks, so every remapped key fired twice. Now the first
// copy holds a named mutex; a later one finds it held, asks the running
// copy to show its dashboard (a registered window message, broadcast to
// every top-level window — both copies run elevated, so it gets through),
// and quits before touching a hook — unless the first copy goes away
// within a few seconds, which is an update handing over (see HandOverWait).
public static class SingleInstance
{
    private const string MutexName = "UnboundKeys.SingleInstance";

    public static readonly int ShowDashboardMessage = RegisterWindowMessage("UnboundKeys.ShowDashboard");

    private static Mutex? _held;

    // How long a later copy waits for the first to go away before leaving.
    // An update hands over like this: the old version starts the new one
    // and then quits, and the new one can get here before the old one has
    // gone. Without the wait it would see "already running" and leave, and
    // a moment later nothing would be running at all.
    private static readonly TimeSpan HandOverWait = TimeSpan.FromSeconds(5);

    // True if this copy may run (and the mutex is now held for the life of
    // the process); false if another copy runs and is staying. In that case
    // the running copy has already been asked to show its dashboard.
    public static bool Claim() => Claim(MutexName, HandOverWait, AskRunningCopyToShowDashboard);

    internal static bool Claim(string name, TimeSpan wait, Action? whenHeld = null)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out bool createdNew);
        if (createdNew)
        {
            _held = mutex;
            return true;
        }

        // Another copy holds it. Ask at once (the usual case is a second
        // click on the shortcut, and the dashboard should come up without
        // a pause), then give the holder a few seconds to go away.
        whenHeld?.Invoke();
        try
        {
            if (mutex.WaitOne(wait))
            {
                _held = mutex;
                return true;
            }
        }
        catch (AbandonedMutexException)
        {
            // The holder exited without releasing it, which is how a copy
            // that quits lets go: the mutex is this copy's now.
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
