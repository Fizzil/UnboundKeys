using System.IO;

namespace UnboundKeys;

// The click each on-screen key makes — on the on-screen keyboard and the
// rename keyboard alike (Fizzil's ask): a mouse on a drawn key gives no
// feel of its own, so the sound is the confirmation that a press landed.
// The sound is a short tick synthesized once, in code, rather than a
// shipped .wav: a burst of low-passed noise that dies in a few
// milliseconds (the click) under a brief falling tone (the body). Playing
// it is the system's business (ISoundOut: WindowsSoundOut on Windows);
// each new click cuts the last one short, which at 35 ms nobody hears.
// The Keyboard page's "Key click sound" switch turns it off.

// The system's side: play this WAV now, cutting short one still playing.
// The same array is handed over every time, so it can be prepared once.
internal interface ISoundOut
{
    void Play(byte[] wav);
}

public static class KeyClick
{
    private const int SampleRate = 44100;
    private static readonly object Gate = new();
    private static byte[]? _wav;
    private static ISoundOut _out = new WindowsSoundOut();

    // Swaps the sound output in use and hands back the old one (the tests).
    internal static ISoundOut Use(ISoundOut output)
    {
        var old = _out;
        _out = output;
        return old;
    }
    private static bool _enabled = Settings.LoadKeyClickSound();

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Settings.SaveKeyClickSound(value);
        }
    }

    public static void Play()
    {
        if (!_enabled)
            return;
        try
        {
            byte[] wav;
            lock (Gate)
                wav = _wav ??= Synthesize();
            _out.Play(wav);
        }
        catch
        {
            // No sound device, or one that refuses: the key itself still works.
        }
    }

    // 35 ms of 16-bit mono at 44.1 kHz.
    private static byte[] Synthesize()
    {
        const double seconds = 0.035;
        int count = (int)(SampleRate * seconds);
        var samples = new short[count];
        var random = new Random(7); // fixed seed: the same click every launch
        double lowPassed = 0;
        double phase = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / SampleRate;

            // The click: white noise through a one-pole low-pass (takes
            // the hiss off), fading out in about 3 ms.
            double noise = random.NextDouble() * 2 - 1;
            lowPassed += 0.35 * (noise - lowPassed);
            double click = lowPassed * Math.Exp(-t / 0.003);

            // The body: a tone that starts near 1.4 kHz, settles to 900 Hz
            // and fades over about 10 ms — the "thock" of a key bottoming out.
            double frequency = 900 + 500 * Math.Exp(-t / 0.004);
            phase += 2 * Math.PI * frequency / SampleRate;
            double body = Math.Sin(phase) * Math.Exp(-t / 0.009);

            double fadeOut = Math.Min(1, (seconds - t) / 0.005); // no tick at the very end
            double value = 0.3 * (0.9 * click + 0.7 * body) * fadeOut;
            samples[i] = (short)(Math.Clamp(value, -1, 1) * short.MaxValue);
        }
        return Wav(samples);
    }

    // A canonical PCM WAV: the 44-byte RIFF header, then the samples.
    private static byte[] Wav(short[] samples)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        int dataBytes = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);             // format chunk size
        writer.Write((short)1);       // PCM
        writer.Write((short)1);       // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2); // bytes per second
        writer.Write((short)2);       // bytes per sample frame
        writer.Write((short)16);      // bits per sample
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (short sample in samples)
            writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }
}
