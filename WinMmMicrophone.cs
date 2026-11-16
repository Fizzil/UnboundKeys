using NAudio.Wave;

namespace UnboundKeys;

// The default microphone on Windows, read through WinMM (NAudio's
// WaveInEvent): 16-bit mono at the rate asked for, which is what the
// speech recognizer takes.
internal sealed class WinMmMicrophone : IMicrophone
{
    private readonly WaveInEvent _waveIn;

    public event Action<byte[], int>? Data;
    public event Action<Exception?>? Stopped;

    public WinMmMicrophone(int sampleRate)
    {
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(sampleRate, 16, 1) };
        _waveIn.DataAvailable += OnData;
        _waveIn.RecordingStopped += OnStopped;
    }

    private void OnData(object? sender, WaveInEventArgs e) => Data?.Invoke(e.Buffer, e.BytesRecorded);
    private void OnStopped(object? sender, StoppedEventArgs e) => Stopped?.Invoke(e.Exception);

    public void Start() => _waveIn.StartRecording();
    public void Stop() => _waveIn.StopRecording();

    public void Dispose()
    {
        _waveIn.RecordingStopped -= OnStopped;
        _waveIn.DataAvailable -= OnData;
        _waveIn.Dispose();
    }
}
