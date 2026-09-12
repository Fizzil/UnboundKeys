using System.Speech.Recognition;

namespace VoicePress;

public sealed class VoiceEngine : IDisposable
{
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

        var choices = new Choices();
        foreach (var word in KeyMap.Words.Keys)
            choices.Add(word);

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
        if (KeyMap.Words.ContainsKey(spoken))
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
