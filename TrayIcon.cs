namespace UnboundKeys;

// The app's presence while its dashboard is hidden: an icon in the
// system tray, the way Discord and Steam live there — the overlay skull
// that used to float over the game is gone (Fizzil's call; it was the
// v0.1 way of having a presence on screen). Left-click only: it toggles
// the dashboard. No right-click menu, because a remapped right button is
// swallowed by the mouse hook even over the tray; Quit lives at the bottom
// of the dashboard's rail (and in its Settings) instead. Dims while
// listening is paused. The first time the dashboard is closed with its X,
// a balloon here says the app is still running — the standard Windows
// answer to "where did it go", and the moment a newcomer needs it.
//
// This class decides what the icon says; the icon itself is the
// system's (ITrayShell: WindowsTray on Windows), so the deciding can be
// tested and another system only has to draw.

// The system's side of the tray icon: an icon that is bright or dimmed,
// with a tooltip, that reports a left click and can show a short message.
internal interface ITrayShell : IDisposable
{
    event Action? Clicked;
    void Show(bool dimmed, string tooltip);
    void Notify(string title, string text);
}

internal sealed class TrayIcon : IDisposable
{
    private readonly ITrayShell _shell;

    public event Action? Clicked;

    public TrayIcon(ITrayShell shell)
    {
        _shell = shell;
        _shell.Clicked += () => Clicked?.Invoke();
        Refresh();
    }

    private bool _pausedState;
    private bool _micLost;

    public void SetPaused(bool paused)
    {
        _pausedState = paused;
        Refresh();
    }

    // The microphone went away (see VoiceEngine.MicrophoneLost): dimmed
    // like paused, and the tooltip says what is wrong and that the app is
    // trying to get it back.
    public void SetMicrophoneLost(bool lost)
    {
        _micLost = lost;
        Refresh();
    }

    private void Refresh() =>
        _shell.Show(_pausedState || _micLost,
            _micLost ? "UnboundKeys — microphone lost, trying again"
            : _pausedState ? "UnboundKeys — voice paused"
            : "UnboundKeys — listening");

    private bool _saidStillRunning;

    // Once per run: the dashboard was closed with its X for the first time.
    public void SayStillRunningOnce()
    {
        if (_saidStillRunning)
            return;
        _saidStillRunning = true;
        _shell.Notify("UnboundKeys is still running",
            "It lives here in the tray. Click this icon to bring the dashboard back; Quit is at the bottom of its rail.");
    }

    public void Dispose() => _shell.Dispose();
}
