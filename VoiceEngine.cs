using System.Speech.Recognition;

namespace VoicePress;

public sealed class VoiceEngine : IDisposable
{
    // "press stop" is a safety-net word, not a real key mapping — recognized
    // the same way as any other "press <word>" command, but Program.cs treats
    // it specially (releases every infinite hold/repeat) instead of looking
    // it up in KeyMap.Words.
    public const string StopWord = "stop";

    // Fired with the recognized key word (e.g. "one", "escape") whenever a
    // "press <word>" command is heard with high enough confidence.
    public event Action<string>? CommandRecognized;

    private readonly SpeechRecognitionEngine _engine;

    // The exact-phrase grammar (only "press <key word>" matches at all) is what mainly
    // keeps ordinary conversation from triggering a key press, so this doesn't need to be
    // strict too — real test data showed correctly-heard commands often scoring ~55-65%.
    private const float ConfidenceThreshold = 0.45f;

    public VoiceEngine()
    {
        _engine = new SpeechRecognitionEngine();
        _engine.SetInputToDefaultAudioDevice();

        // With only ~11 possible phrases loaded, the engine's default
        // behavior is to force almost any short utterance into whichever of
        // them it's phonetically closest to, rather than truly rejecting
        // off-grammar speech — which is what let ordinary talk (nothing
        // like "press" or a number) trigger commands. This is a native SAPI
        // setting (not exposed as a typed property in System.Speech) that
        // makes the engine itself reject weak matches before they ever
        // become a SpeechRecognized event, instead of relying only on the
        // post-hoc Confidence check below. 0-100 scale; may need further
        // tuning against real background speech.
        _engine.UpdateRecognizerSetting("CFGConfidenceRejectionThreshold", 60);

        var choices = new Choices();
        foreach (var word in KeyMap.Words.Keys)
            choices.Add(word);
        choices.Add(StopWord);

        var builder = new GrammarBuilder();
        builder.Append("press");
        builder.Append(choices);

        var grammar = new Grammar(builder);
        _engine.LoadGrammarAsync(grammar);
        _engine.SpeechRecognized += OnSpeechRecognized;
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence < ConfidenceThreshold)
            return;

        var parts = e.Result.Text.Split(' ', 2);
        if (parts.Length != 2)
            return;

        var spoken = parts[1].Trim();
        if (KeyMap.Words.ContainsKey(spoken) || string.Equals(spoken, StopWord, StringComparison.OrdinalIgnoreCase))
            CommandRecognized?.Invoke(spoken);
    }

    public void Start() => _engine.RecognizeAsync(RecognizeMode.Multiple);
    public void Pause() => _engine.RecognizeAsyncStop();
    public void Resume() => _engine.RecognizeAsync(RecognizeMode.Multiple);

    public void Dispose()
    {
        _engine.SpeechRecognized -= OnSpeechRecognized;
        _engine.Dispose();
    }
}
