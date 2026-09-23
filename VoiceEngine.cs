using System.IO;
using System.Text.Json;
using NAudio.Wave;
using Vosk;

namespace UnboundKeys;

// Offline, small-vocabulary speech recognition via Vosk — replaces the
// built-in Windows System.Speech (SAPI) engine, which couldn't reliably
// tell "press one" apart from ordinary conversation: with only ~11 phrases
// loaded, SAPI would force almost any short utterance into whichever one
// it was phonetically closest to, rather than truly rejecting it.
//
// Vosk has no microphone capture of its own — NAudio pulls raw audio off
// the default input device and feeds it in. The recognizer's vocabulary is
// constrained to just "press" plus the key words (see the grammar built
// below), so it's not merely unlikely but actually *impossible* for it to
// transcribe anything else — a much stronger guarantee than SAPI's
// best-effort phrase matching ever gave us.
public sealed class VoiceEngine : IDisposable
{
    // "press stop" is a safety-net word, not a real key mapping — recognized
    // the same way as any other "press <word>" command, but Program.cs treats
    // it specially (releases every infinite hold/repeat) instead of looking
    // it up in KeyMap.Words.
    public const string StopWord = "stop";

    // Fired with the recognized key word (e.g. "one", "escape") whenever a
    // "press <word>" command is heard.
    public event Action<string>? CommandRecognized;

    private const int SampleRate = 16000;

    private readonly Model _model;
    private readonly VoskRecognizer _recognizer;
    private readonly WaveInEvent _waveIn;

    public VoiceEngine()
    {
        Vosk.Vosk.SetLogLevel(-1); // Vosk logs verbosely to the console by default; not useful here.

        // Copied alongside the exe at build/publish time (see the .csproj) —
        // it's a folder of data files, not a single assembly, so it can't be
        // embedded into the single-file publish the way the code itself is.
        string modelPath = Path.Combine(AppContext.BaseDirectory, "VoskModel");
        if (!Directory.Exists(modelPath))
        {
            throw new InvalidOperationException(
                $"Couldn't find the speech recognition model at:\n{modelPath}\n\n" +
                "The VoskModel folder needs to sit next to UnboundKeys's exe.");
        }

        _model = new Model(modelPath);

        // A JSON list of every word the recognizer is allowed to output —
        // not fixed two-word phrases, just the flat set of individual words.
        // Whatever gets said, Vosk can only ever transcribe it as some
        // sequence of these; the actual "press <word>" shape is enforced
        // afterward in HandleResult.
        var words = new List<string> { "press" };
        words.AddRange(KeyMap.Words.Keys);
        words.Add(StopWord);
        string grammar = JsonSerializer.Serialize(words);
        _recognizer = new VoskRecognizer(_model, SampleRate, grammar);

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
        };
        _waveIn.DataAvailable += OnDataAvailable;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Returns true once it's decided a phrase is finished (on a pause in
        // speech) — Result() then has the finished text. False means it's
        // still only a partial/in-progress guess, not worth acting on yet.
        if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            HandleResult(_recognizer.Result());
    }

    private void HandleResult(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        if (!doc.RootElement.TryGetProperty("text", out var textProperty))
            return;

        var text = textProperty.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] != "press")
            return;

        var spoken = parts[1].Trim();
        if (KeyMap.Words.ContainsKey(spoken) || string.Equals(spoken, StopWord, StringComparison.OrdinalIgnoreCase))
            CommandRecognized?.Invoke(spoken);
    }

    public void Start() => _waveIn.StartRecording();
    public void Pause() => _waveIn.StopRecording();
    public void Resume() => _waveIn.StartRecording();

    public void Dispose()
    {
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.Dispose();
        _recognizer.Dispose();
        _model.Dispose();
    }
}
