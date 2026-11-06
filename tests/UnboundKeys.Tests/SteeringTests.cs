namespace UnboundKeys.Tests;

// Steering (see Steering.cs): the rubber band that holds the pointer
// near the middle of the screen while steering is on. RubberBand is the
// pure part: told about real pointer moves and clock ticks, it answers
// where the pointer should be put instead (past the line: back to the
// line; idle: a step home) or null for "leave it". No real pointer is
// touched here.
internal static class SteeringTests
{
    private static bool _ok;

    public static bool Run()
    {
        _ok = true;
        Section("steering: the line the pointer cannot pass", Line);
        Section("steering: letting go springs the pointer home", Spring);
        Section("steering: a word with Steering on is not a plain tap", Behavior);
        return _ok;
    }

    // Centre (1000, 500), the line 300 px out sideways and 150 px up or down.
    private static RubberBand Band() => new(1000, 500, 300, 150);

    private static void Line()
    {
        var band = Band();
        Check(band.Moved(900, 500, 0.0) == null, "a move inside the band is left alone");
        Check(band.Moved(700, 500, 0.1) == null, "a move exactly to the line is left alone");
        Check(band.Moved(650, 480, 0.2) == (700, 480), "past the line on the left: back to the line, same height");
        Check(band.Moved(1350, 520, 0.3) == (1300, 520), "past the line on the right: back to the line");
        Check(band.Moved(1000, 400, 0.4) == null, "up to the line is left alone");
        Check(band.Moved(1000, 50, 0.5) == (1000, 350), "past the line upwards: back to the line (the throttle's full)");
        Check(band.Moved(1000, 900, 0.6) == (1000, 650), "past the line downwards: back to the line (the brake's full)");
        Check(band.Moved(600, 100, 0.7) == (700, 350), "a corner push is held on both lines at once");
    }

    private static void Spring()
    {
        var band = Band();
        band.Moved(800, 500, 1.00);
        Check(band.Tick(800, 500, 1.05) == null, "still pushing a moment ago: no spring");
        Check(band.Tick(800, 500, 1.09) == null, "not yet a tenth of a second idle");

        var step = band.Tick(800, 500, 1.20);
        Check(step != null, "idle for a tenth of a second: the spring pulls");
        Check(step != null && step.Value.X > 800 && step.Value.X < 1000, "a step towards the centre, not all the way");
        Check(step != null && step.Value.Y == 500, "already centred in height stays so");

        var later = band.Tick(step!.Value.X, 500, 1.22);
        Check(later != null && later.Value.X > step.Value.X, "the next tick comes closer still");

        Check(band.Tick(1000, 500, 1.30) == null, "home already: nothing to do");
        Check(band.Tick(999, 501, 1.32) == (1000, 500), "within a pixel of home: land exactly on it");

        band.Moved(1100, 600, 2.00);
        Check(band.Tick(1100, 600, 2.02) == null, "a fresh push stops the spring");
        var both = band.Tick(1100, 600, 2.20);
        Check(both != null && both.Value.X < 1100 && both.Value.Y < 600, "the spring pulls sideways and up and down together");

        // Switched on with the pointer off-centre and never moved: it goes
        // home on its own.
        var fresh = Band();
        Check(fresh.Tick(1200, 500, 0.0) == null, "the first instant after switching on is still too soon");
        Check(fresh.Tick(1200, 500, 0.2) != null, "then an unmoved pointer is pulled home");
    }

    private static void Behavior()
    {
        Check(new KeyBehavior().IsPlainTap(), "a fresh behaviour is a plain tap");
        Check(!new KeyBehavior { Steering = true }.IsPlainTap(), "Steering on makes it customized");
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
