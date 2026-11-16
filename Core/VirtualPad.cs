namespace UnboundKeys;

// The virtual Xbox controller Steering drives (see Steering.cs): what the
// game sees instead of a keyboard. The pad exists only while steering is
// on. This file is the contract the rest of the app builds against; what
// makes a pad is the system's (on Windows the ViGEmBus driver, see
// ViGEmPad.cs). A system with nothing to make one has no steering, and
// the rest of the app does not mind.
public interface IVirtualPad : IDisposable
{
    // steer runs -1 (full left) to +1 (full right); throttle and brake run
    // 0 (off) to 1 (full). Anything outside is clamped. Called from the
    // steering timer about every 15 ms, so it must be cheap and must not
    // throw: a pad that has gone away just stops listening.
    void Set(double steer, double throttle, double brake);
}

// Two pseudo key codes, outside every real virtual-key code, that stand for
// the virtual pad's pedals so a mapping can hold them like any key: Mode
// Hold with Infinite on Throttle is a pedal held until the mapping is
// stopped. KeyOutput.KeyDown/KeyUp hand them to Steering.SetPadPedal
// instead of sending a key.
public static class PadKeys
{
    public const Key Throttle = Key.Throttle;
    public const Key Brake = Key.Brake;

    public static bool IsPadKey(Key key) => key is Throttle or Brake;
}

// What makes virtual pads on this system.
internal interface IVirtualPadDriver
{
    // Where to get the driver, for the Steering page's button.
    string DownloadUrl { get; }

    // True when the driver is installed and answering.
    bool IsInstalled();

    // A pad plugged into the driver, or null with a plain-words reason for
    // the Steering page ("The ViGEmBus driver is not installed").
    IVirtualPad? TryCreate(out string? problem);
}

public static class VirtualPadDriver
{
    // A system that brought no driver: no pad, said plainly.
    private sealed class None : IVirtualPadDriver
    {
        public string DownloadUrl => "";
        public bool IsInstalled() => false;

        public IVirtualPad? TryCreate(out string? problem)
        {
            problem = "This system has no virtual controller.";
            return null;
        }
    }

    private static IVirtualPadDriver _driver = new None();

    // Swaps the driver in use and hands back the old one.
    internal static IVirtualPadDriver Use(IVirtualPadDriver driver)
    {
        var old = _driver;
        _driver = driver;
        return old;
    }

    public static string DownloadUrl => _driver.DownloadUrl;
    public static bool IsInstalled() => _driver.IsInstalled();
    public static IVirtualPad? TryCreate(out string? problem) => _driver.TryCreate(out problem);
}
