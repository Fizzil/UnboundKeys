using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace UnboundKeys;

// The real virtual Xbox 360 pad, plugged into the ViGEmBus driver. Steer
// goes to the left thumbstick's X axis, throttle to the right trigger and
// brake to the left trigger, the layout racing games expect by default.
// Set is called about every 15 ms from the steering timer, so it only
// clamps, writes three values and submits; and once the pad or the driver
// has gone away (the driver removed, the pad disposed) it quietly does
// nothing, because there is no one to tell on that thread.
internal sealed class ViGEmPad : IVirtualPad
{
    private readonly object _gate = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;

    public ViGEmPad()
    {
        // The constructor is allowed to throw (the caller turns that into
        // a sentence for Settings); just don't leave half a pad behind.
        try
        {
            _client = new ViGEmClient();
            _pad = _client.CreateXbox360Controller();
            _pad.AutoSubmitReport = false;
            _pad.Connect();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Set(double steer, double throttle, double brake)
    {
        lock (_gate)
        {
            if (_pad == null) return;
            try
            {
                _pad.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxis(steer));
                _pad.SetSliderValue(Xbox360Slider.RightTrigger, ToTrigger(throttle));
                _pad.SetSliderValue(Xbox360Slider.LeftTrigger, ToTrigger(brake));
                _pad.SubmitReport();
            }
            catch
            {
                // The driver went away under us; stop listening.
                _pad = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _pad?.Disconnect(); } catch { }
            _pad = null;
            try { _client?.Dispose(); } catch { }
            _client = null;
        }
    }

    // NaN compares false with everything, so it falls to the neutral value
    // instead of reaching the cast.
    private static short ToAxis(double v)
    {
        if (double.IsNaN(v)) return 0;
        if (v <= -1) return short.MinValue;
        if (v >= 1) return short.MaxValue;
        return (short)Math.Round(v * 32767);
    }

    private static byte ToTrigger(double v)
    {
        if (double.IsNaN(v) || v <= 0) return 0;
        if (v >= 1) return 255;
        return (byte)Math.Round(v * 255);
    }
}
