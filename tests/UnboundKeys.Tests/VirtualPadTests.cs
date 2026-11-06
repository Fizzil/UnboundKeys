namespace UnboundKeys.Tests;

// The virtual Xbox pad (see VirtualPad.cs and ViGEmPad.cs). It needs the
// ViGEmBus driver, which is not on every machine the tests run on, so
// without it the checks are skipped and the section passes. With it, a
// real pad is plugged in and unplugged; nothing is read back, because the
// pad only has to take what it is given without throwing.
internal static class VirtualPadTests
{
    private static bool _ok;

    public static bool Run()
    {
        _ok = true;
        Section("virtual pad: the driver and the pad", Pad);
        return _ok;
    }

    private static void Pad()
    {
        if (!VirtualPadDriver.IsInstalled())
        {
            Console.WriteLine("  The ViGEmBus driver is not installed on this machine; the pad checks are skipped.");
            return;
        }

        IVirtualPad? pad = VirtualPadDriver.TryCreate(out var problem);
        try
        {
            Check(pad != null && problem == null, "a pad is created with no problem");
            if (pad == null) return;

            pad.Set(0.5, 1, 0);
            Check(true, "Set(0.5, 1, 0) does not throw");
            pad.Set(-2, 3, -1);
            Check(true, "Set with everything out of range does not throw");
            pad.Dispose();
            Check(true, "Dispose does not throw");
            pad.Set(0, 0, 0);
            Check(true, "Set after Dispose does nothing and does not throw");

            pad = VirtualPadDriver.TryCreate(out problem);
            Check(pad != null && problem == null, "the pad can be created again after it was disposed");
        }
        finally
        {
            pad?.Dispose();
        }
    }

    private static void Section(string name, Action body)
    {
        Console.WriteLine($"--- {name} ---");
        try
        {
            body();
        }
        catch (Exception ex)
        {
            Check(false, $"threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  [{(condition ? "ok" : "FAIL")}] {what}");
        _ok &= condition;
    }
}
