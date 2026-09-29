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
    // it up in KeyMap.Words. "press menu" is the same kind of thing: it
    // brings the dashboard up, hands-free — the way back in while a game
    // is running, now that there's no overlay icon on screen.
    public const string StopWord = "stop";
    public const string MenuWord = "menu";
    // "press fade" toggles Fade — the spoken way out of a dimmed screen,
    // where the Fade switch itself is the hardest thing to find (Fizzil
    // has no physical Caps Lock to double-tap).
    public const string FadeWord = "fade";

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
        words.Add(MenuWord);
        words.Add(FadeWord);
        string grammar = JsonSerializer.Serialize(words);
        _recognizer = new VoskRecognizer(_model, SampleRate, grammar);

        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
        };
        _waveIn.DataAvailable += OnDataAvailable;
    }

    // Acting on partial results (Fizzil: the delay). The recognizer decides
    // a phrase is finished only after a pause in speech, several hundred
    // milliseconds after the last word; but it reports its running guess
    // after every audio chunk (100 ms), and with this small a grammar the
    // guess is usually right as soon as the phrase is complete. So: when the
    // running guess is a whole command and has read the same for two
    // chunks in a row, fire it then, and when the finished result arrives
    // fire only if it names a different command (the guess was wrong and
    // the user's actual word still deserves its press).
    private string _lastPartial = "";
    private int _partialRepeats;
    private string? _firedFromPartial;

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            HandleFinal(TextOf(_recognizer.Result(), "text"));
        else
            HandlePartial(TextOf(_recognizer.PartialResult(), "partial"));
    }

    private void HandlePartial(string text)
    {
        if (text.Length == 0)
        {
            _lastPartial = "";
            _partialRepeats = 0;
            return;
        }

        _partialRepeats = text == _lastPartial ? _partialRepeats + 1 : 1;
        _lastPartial = text;

        string? command = CommandOf(text);
        bool fire = command != null && _firedFromPartial == null && _partialRepeats >= 2;
        if (fire)
        {
            _firedFromPartial = command;
            CommandRecognized?.Invoke(command!);
        }
        VoiceHeard.Report(text, final: false, fire ? command : null);
    }

    private void HandleFinal(string text)
    {
        string? command = CommandOf(text);
        bool fire = command != null && command != _firedFromPartial;
        if (fire)
            CommandRecognized?.Invoke(command!);
        if (text.Length > 0)
            VoiceHeard.Report(text, final: true, fire ? command : _firedFromPartial);

        _firedFromPartial = null;
        _lastPartial = "";
        _partialRepeats = 0;
    }

    private static string TextOf(string resultJson, string property)
    {
        using var doc = JsonDocument.Parse(resultJson);
        return doc.RootElement.TryGetProperty(property, out var value) ? (value.GetString() ?? "").Trim() : "";
    }

    // "press <word>" for a word this app knows, else null. Anything else the
    // recognizer produces (a lone "press", two commands run together) is
    // not a command.
    private static string? CommandOf(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[0] != "press")
            return null;

        var spoken = parts[1].Trim();
        bool known = KeyMap.Words.ContainsKey(spoken)
            || string.Equals(spoken, StopWord, StringComparison.OrdinalIgnoreCase)
            || string.Equals(spoken, MenuWord, StringComparison.OrdinalIgnoreCase)
            || string.Equals(spoken, FadeWord, StringComparison.OrdinalIgnoreCase);
        return known ? spoken : null;
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
