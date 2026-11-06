namespace UnboundKeys;

// The virtual Xbox controller Steering drives (see Steering.cs): what the
// game sees instead of a keyboard. The pad exists only while steering is
// on. Behind it is the ViGEmBus driver (github.com/nefarius/ViGEmBus,
// the one DS4Windows uses; discontinued in 2023 but still signed and
// still loading), through the Nefarius.ViGEm.Client library. This file
// is the contract the rest of the app builds against; the driver-facing
// implementation lives beside it.
public interface IVirtualPad : IDisposable
{
    // steer runs -1 (full left) to +1 (full right); throttle and brake run
    // 0 (off) to 1 (full). Anything outside is clamped. Called from the
    // steering timer about every 15 ms, so it must be cheap and must not
    // throw: a pad that has gone away just stops listening.
    void Set(double steer, double throttle, double brake);
}

public static class VirtualPadDriver
{
    public const string DownloadUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";

    // True when the ViGEmBus driver is installed and answering.
    public static bool IsInstalled() => false;

    // An Xbox 360 pad plugged into the driver, or null with a plain-words
    // reason for the Settings page ("The ViGEmBus driver is not installed").
    public static IVirtualPad? TryCreate(out string? problem)
    {
        problem = "The virtual controller is not built yet.";
        return null;
    }
}
