namespace UnboundKeys;

// The microphone, as the voice engine needs it: raw sound in, and word
// when it stops. Which microphone and how it is read is the system's
// business (WinMmMicrophone on Windows).
internal interface IMicrophone : IDisposable
{
    // A chunk of sound: the buffer and how many of its bytes are sound.
    event Action<byte[], int>? Data;

    // Raised after any stop: one asked for (Stop) or the device's own,
    // with the reason the system gave, if any.
    event Action<Exception?>? Stopped;

    // Throws when there is no microphone to start.
    void Start();
    void Stop();
}

// Keeps the microphone going while it is wanted. A microphone that goes
// away while wanted (unplugged, a device change, an error in the capture)
// or is missing at the start is a loss: said once (Lost), tried again
// every few seconds, and said again when it is back (Back). Before this,
// a dead microphone was silent: the app looked alive and listened to
// nothing, and a word that had switched steering on could not switch it
// off.
internal sealed class MicrophoneKeeper : IDisposable
{
    public event Action<Exception?>? Lost;
    public event Action? Back;

    private readonly IMicrophone _microphone;
    private readonly int _retryMs;
    private readonly object _gate = new();
    private bool _wanted; // between Begin and Pause/Dispose
    private bool _lost;   // stopped without being asked to
    private System.Threading.Timer? _retry;

    public MicrophoneKeeper(IMicrophone microphone, int retryMs = 3000)
    {
        _microphone = microphone;
        _retryMs = retryMs;
        _microphone.Stopped += OnStopped;
    }

    // Wanted from now on. A microphone missing at this moment (Start
    // throws) is a loss like any other, retried the same way, not a crash
    // of the UI thread the switch was clicked on.
    public void Begin()
    {
        bool lost = false;
        lock (_gate)
        {
            _wanted = true;
            if (_lost)
                return; // the retry timer is on it
            try
            {
                _microphone.Start();
            }
            catch (Exception)
            {
                _lost = true;
                lost = true;
                _retry ??= new System.Threading.Timer(_ => TryAgain(), null, _retryMs, _retryMs);
            }
        }
        if (lost)
            Lost?.Invoke(null);
    }

    public void Pause()
    {
        lock (_gate)
            _wanted = false;
        _microphone.Stop();
    }

    // After any stop: ours (Pause, Dispose) or the device's. Only the
    // latter is a loss.
    private void OnStopped(Exception? reason)
    {
        bool lost;
        lock (_gate)
        {
            lost = _wanted && !_lost;
            if (lost)
            {
                _lost = true;
                _retry ??= new System.Threading.Timer(_ => TryAgain(), null, _retryMs, _retryMs);
            }
        }
        if (lost)
            Lost?.Invoke(reason);
    }

    private void TryAgain()
    {
        lock (_gate)
        {
            if (!_wanted || !_lost)
                return;
            try
            {
                _microphone.Start();
            }
            catch (Exception)
            {
                return; // still no microphone; the timer tries again
            }
            _lost = false;
            _retry?.Dispose();
            _retry = null;
        }
        Back?.Invoke();
    }

    // Stops watching and trying. The microphone itself is its owner's to
    // dispose (the voice engine).
    public void Dispose()
    {
        lock (_gate)
        {
            _wanted = false;
            _retry?.Dispose();
            _retry = null;
        }
        _microphone.Stopped -= OnStopped;
    }
}
