using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

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

// Two pseudo key codes, outside every real virtual-key code, that stand for
// the virtual pad's pedals so a mapping can hold them like any key: Mode
// Hold with Infinite on Throttle is a pedal held until the mapping is
// stopped. NativeInput.KeyDown/KeyUp hand them to Steering.SetPadPedal
// instead of sending a key.
public static class PadKeys
{
    public const ushort Throttle = 0xFF01;
    public const ushort Brake = 0xFF02;

    public static bool IsPadKey(ushort vk) => vk is Throttle or Brake;
}

public static class VirtualPadDriver
{
    public const string DownloadUrl = "https://github.com/nefarius/ViGEmBus/releases/latest";

    // True when the ViGEmBus driver is installed and answering. Opening a
    // client is the library's own test: it throws when the driver is not
    // there. Any other failure also counts as "not answering".
    public static bool IsInstalled()
    {
        try
        {
            using var client = new ViGEmClient();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // An Xbox 360 pad plugged into the driver, or null with a plain-words
    // reason for the Settings page ("The ViGEmBus driver is not installed").
    public static IVirtualPad? TryCreate(out string? problem)
    {
        try
        {
            var pad = new ViGEmPad();
            problem = null;
            return pad;
        }
        catch (VigemBusNotFoundException)
        {
            problem = "The ViGEmBus driver is not installed. Install it from " + DownloadUrl + ", then switch steering on again.";
            return null;
        }
        catch (Exception ex)
        {
            problem = "The virtual controller could not be started: " + ex.Message;
            return null;
        }
    }
}
