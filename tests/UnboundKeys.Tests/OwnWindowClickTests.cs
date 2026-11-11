namespace UnboundKeys.Tests;

// Right-click is Back in the dashboard (Fizzil, 2026-11-11), even in a
// profile that maps the right button (the isle / Pteradon: W): over one of
// UnboundKeys' own windows the click goes to the window, not the mapping.
// OwnWindowClicks keeps a click's two halves together: an up goes wherever
// its down went, even if the pointer moved off the window in between, so
// neither Windows nor the dashboard sees half a click.
internal static class OwnWindowClickTests
{
    private static bool _ok;

    public static bool Run()
    {
        _ok = true;
        Section("right-click over UnboundKeys' own windows goes to them", Halves);
        return _ok;
    }

    private static void Halves()
    {
        var clicks = new OwnWindowClicks();
        Check(clicks.Down(overOwnWindow: true), "a down over the dashboard goes to the dashboard");
        Check(clicks.Up(), "and so does its up, wherever the pointer is by then");

        Check(!clicks.Down(overOwnWindow: false), "a down anywhere else goes to the mapping as before");
        Check(!clicks.Up(), "and so does its up");

        Check(!new OwnWindowClicks().Up(), "an up with no down before it is the mapping's");

        clicks.Down(overOwnWindow: true);
        clicks.Up();
        Check(!clicks.Down(overOwnWindow: false) && !clicks.Up(), "a dashboard click does not leak into the next one");
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
