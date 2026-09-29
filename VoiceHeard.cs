namespace UnboundKeys;

// What the microphone just made of the last words, for the Voice page's
// "Heard" line (Fizzil: "did it hear me?" answered without looking at
// the game). VoiceEngine reports here from the microphone's thread; the
// page hops to its own. Text is the recognizer's own words ("press four"),
// partial while still being spoken, final once it has decided; Command is
// the key word that fired, if one did.
public static class VoiceHeard
{
    public static event Action<string, bool, string?>? Changed;

    public static void Report(string text, bool final, string? command) =>
        Changed?.Invoke(text, final, command);
}
