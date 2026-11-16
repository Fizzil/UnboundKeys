using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnboundKeys;

// Every key and mouse button a mapping can send, by name. The app's logic
// and its files speak in these names; what a name means to the system
// (which code to send, how to hear it) is the system's translator's
// business (WindowsKeys on Windows).
//
// The numbers behind the names are the app's own. They are the ones its
// older files used for keys, so an old file's number is read straight
// into its name (see KeyJsonConverter); and those numbers were Windows',
// which is why the Windows translator is short. Another system's
// translator is a table from these names to its own codes.
//
// None is "no key": an unmapped mouse button, a double press with nothing
// picked. It is never sent.
[JsonConverter(typeof(KeyJsonConverter))]
public enum Key : ushort
{
    None = 0,

    // Mouse buttons: a mapping can click as well as type.
    LeftClick = 0x01,
    RightClick = 0x02,
    MiddleClick = 0x04,
    Mouse4 = 0x05,
    Mouse5 = 0x06,

    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Ctrl = 0x11,
    Alt = 0x12,
    CapsLock = 0x14,
    Escape = 0x1B,
    Space = 0x20,

    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Insert = 0x2D,
    Delete = 0x2E,

    Digit0 = 0x30, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9,

    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    Win = 0x5B,
    RightWin = 0x5C,

    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,

    Semicolon = 0xBA,
    EqualSign = 0xBB,
    Comma = 0xBC,
    Minus = 0xBD,
    Period = 0xBE,
    Slash = 0xBF,
    Backtick = 0xC0,
    OpenBracket = 0xDB,
    Backslash = 0xDC,
    CloseBracket = 0xDD,
    Quote = 0xDE,

    // The virtual pad's pedals (see PadKeys): keys to the rest of the app,
    // but no system key.
    Throttle = 0xFF01,
    Brake = 0xFF02,
}

public static class KeyNames
{
    // Whether the key is one the app has a name for. A number from an old
    // file that it has no name for is kept as it is, and written back as
    // that number, so nothing is lost by not knowing it.
    public static bool HasName(Key key) => Enum.IsDefined(key);

    // The digit keys and the letter keys, by their character.
    public static Key Digit(int digit) => (Key)((int)Key.Digit0 + digit);
    public static Key Letter(char letter) => (Key)((int)Key.A + (char.ToUpperInvariant(letter) - 'A'));
    public static Key Function(int number) => (Key)((int)Key.F1 + (number - 1));

    // The key with that name, whatever its capitals; None for a name the
    // app does not know (a key from a newer version), which reads as "not
    // mapped" and never as some other key.
    public static Key Parse(string? name) =>
        name != null && name.Length > 0 && !char.IsAsciiDigit(name[0]) && name[0] != '-' && name[0] != '+'
        && Enum.TryParse<Key>(name, ignoreCase: true, out var key) && Enum.IsDefined(key)
            ? key
            : Key.None;
}

// A key in a file is its name ("A", "F5", "Throttle"). Files written
// before 4.16.0 hold a number instead, and those are still read: the
// number is the key (see Key). Reading one sets SawNumber, which is how
// Settings knows a file is an old one to upgrade. A key with no name
// (only ever a number from an old file) is written back as its number.
internal sealed class KeyJsonConverter : JsonConverter<Key>
{
    // Set on the thread that read a number where a named key would now
    // be written as its name. Settings clears it before a read.
    [ThreadStatic] internal static bool SawNumber;

    public override Key Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (!reader.TryGetUInt16(out ushort number))
                    throw new JsonException("A key's number is out of range.");
                var key = (Key)number;
                if (KeyNames.HasName(key))
                    SawNumber = true;
                return key;
            case JsonTokenType.String:
                return KeyNames.Parse(reader.GetString());
            default:
                throw new JsonException("A key is a name or a number.");
        }
    }

    public override void Write(Utf8JsonWriter writer, Key value, JsonSerializerOptions options)
    {
        if (KeyNames.HasName(value))
            writer.WriteStringValue(value.ToString());
        else
            writer.WriteNumberValue((ushort)value);
    }
}
