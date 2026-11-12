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
        Section("steering: the pointer's offset becomes the pad", Pad);
        Section("steering: a word with Steering on is not a plain tap", Behavior);
        Section("steering: pushing up or down presses a push", Pushes);
        Section("steering: the pad's pedals can be held by keys", PadPedals);
        Section("steering: pushes are mappings of their own", PushIds);
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

    private static void Pad()
    {
        // The band's lines: 300 px sideways, 150 px up or down.
        var c = PadValues.FromOffset(0, 0, 300, 150);
        Check(c == (0.0, 0.0, 0.0), "the centre gives all zeros");
        // The curve (see PadValues): gentle near the centre, full at the line.
        Check(PadValues.FromOffset(-150, 0, 300, 150).Steer == -0.25, "half left steers a quarter (the curve)");
        Check(PadValues.FromOffset(300, 0, 300, 150).Steer == 1.0, "the line is full lock");
        Check(PadValues.FromOffset(-900, 0, 300, 150).Steer == -1.0, "past the left line is clamped to -1");
        Check(PadValues.FromOffset(900, 0, 300, 150).Steer == 1.0, "past the right line is clamped to +1");
        // Height means nothing to the car (Fizzil: up and down as pedals
        // got in the way of holding the middle button).
        var up = PadValues.FromOffset(0, -75, 300, 150);
        Check(up.Throttle == 0.0 && up.Brake == 0.0, "up gives no throttle and no brake");
        var down = PadValues.FromOffset(0, 150, 300, 150);
        Check(down.Brake == 0.0 && down.Throttle == 0.0, "down gives nothing either");
        Check(PadValues.FromOffset(0, 0, 300, 150, padThrottle: true).Throttle == 1.0, "the Throttle key held gives full throttle at the centre");
        Check(PadValues.FromOffset(0, -75, 300, 150, padThrottle: true).Throttle == 1.0, "the Throttle key held gives full throttle whatever the height");
        var both = PadValues.FromOffset(0, 75, 300, 150, padThrottle: true);
        Check(both.Throttle == 1.0 && both.Brake == 0.0, "the Throttle key with the pointer down: full throttle, no brake");
        Check(PadValues.FromOffset(50, 50, 0, 0) == (0.0, 0.0, 0.0), "a line at 0 gives zeros, not a division error");
    }

    // The band's line is 150 px: a third is 50 px, a quarter 37.5 px.
    private static void Pushes()
    {
        var p = new PushState();
        Check(p.Update(0, 150) == PushEvent.None, "at the centre nothing happens");
        Check(p.Update(-40, 150) == PushEvent.None, "up, but not yet a third of the way: nothing");
        Check(p.Update(-51, 150) == PushEvent.UpPressed, "up past a third presses Push up");
        Check(p.Update(-60, 150) == PushEvent.None, "still up: no second press");
        Check(p.Update(-45, 150) == PushEvent.None, "back inside a third but outside a quarter: stays pressed");
        Check(p.Update(-38, 150) == PushEvent.None, "just outside a quarter: still pressed");
        Check(p.Update(-37, 150) == PushEvent.UpReleased, "inside a quarter releases Push up");
        Check(p.Update(-45, 150) == PushEvent.None, "after a release, a third has to be passed again");

        var d = new PushState();
        Check(d.Update(80, 150) == PushEvent.DownPressed, "down past a third presses Push down");
        Check(d.Update(45, 150) == PushEvent.None, "down, between a quarter and a third: stays pressed");
        Check(d.Update(10, 150) == PushEvent.DownReleased, "back near the centre releases Push down");

        // A wobble at the line does not chatter.
        var w = new PushState();
        int presses = 0, releases = 0;
        foreach (int dy in new[] { 49, 52, 48, 51, 47, 53, 46, 50, 52 })
        {
            var e = w.Update(dy, 150);
            if (e.HasFlag(PushEvent.DownPressed)) presses++;
            if (e.HasFlag(PushEvent.DownReleased)) releases++;
        }
        Check(presses == 1 && releases == 0, "a wobble around the third presses once and never lets go");

        // Up and down are never pressed together; crossing over swaps them.
        var s = new PushState();
        s.Update(-100, 150);
        var swap = s.Update(100, 150);
        Check(swap.HasFlag(PushEvent.UpReleased) && swap.HasFlag(PushEvent.DownPressed),
            "a change of sign releases one and presses the other");
        Check(s.UpPressed == false && s.DownPressed == true, "only one of the two is pressed after the swap");
        var back = s.Update(-3, 150);
        Check(back == PushEvent.DownReleased && !s.UpPressed && !s.DownPressed, "through the middle: released, and not pressed the other way");
        s.Update(-100, 150);
        var small = s.Update(30, 150);
        Check(small == PushEvent.UpReleased && !s.DownPressed, "crossing to the other side but short of a third only releases");

        var z = new PushState();
        Check(z.Update(500, 0) == PushEvent.None && !z.DownPressed, "a line of 0 never presses");
        Check(z.Update(-500, 0) == PushEvent.None && !z.UpPressed, "a line of 0 never presses upwards either");

        var r = new PushState();
        r.Update(100, 150);
        Check(r.Release() == PushEvent.DownReleased && !r.DownPressed, "Release lets go of whatever is pressed");
        Check(r.Release() == PushEvent.None, "and does nothing when nothing is");
        var r2 = new PushState();
        r2.Update(100, 150);
        Check(r2.Update(100, 0) == PushEvent.DownReleased, "the line going to 0 while pressed releases");
    }

    private static void PadPedals()
    {
        var thr = PadValues.FromOffset(0, 0, 300, 150, padThrottle: true, padBrake: false);
        Check(thr.Throttle == 1.0 && thr.Brake == 0.0, "the pad throttle key gives full throttle");
        var brk = PadValues.FromOffset(0, 0, 300, 150, padThrottle: false, padBrake: true);
        Check(brk.Throttle == 0.0 && brk.Brake == 1.0, "the pad brake key gives full brake");
        var both = PadValues.FromOffset(0, 0, 300, 150, padThrottle: true, padBrake: true);
        Check(both.Throttle == 1.0 && both.Brake == 1.0, "the Throttle and Brake keys work together");
        var on = PadValues.FromOffset(-150, 0, 300, 150, padThrottle: true, padBrake: false);
        Check(on.Steer == -0.25 && on.Throttle == 1.0, "steering is untouched by the pedals");
        Check(PadValues.FromOffset(0, 0, 300, 150) == (0.0, 0.0, 0.0), "the pedals default to off");
    }

    private static void PushIds()
    {
        Check(SteeringMap.Ids.Contains("pushup") && SteeringMap.Ids.Contains("pushdown"), "pushup and pushdown are the steering map's ids");
        Check(!MouseCatalog.Buttons.Any(b => b.Id.StartsWith("push")), "the pushes are not in the drawn mouse's buttons");
        Check(MouseCatalog.LabelFor("pushup") == "Move up" && MouseCatalog.LabelFor("pushdown") == "Move down", "their labels");
        Check(KeyCatalog.DisplayNameFor(PadKeys.Throttle) == "Throttle" && KeyCatalog.DisplayNameFor(PadKeys.Brake) == "Brake", "the pad keys have names");
        Check(KeyCatalog.Groups.Any(g => g.Category == "Controller" && g.Keys.Length == 2), "the Controller group lists both");
        Check(!KeyMap.IsExtendedKey(PadKeys.Throttle) && !KeyMap.IsExtendedKey(PadKeys.Brake), "pad keys are not extended keys");
        // A pedal key held before steering comes on is remembered, so the
        // mapping that holds it applies the moment steering is on (Fizzil
        // pressed Mouse 5, then started steering, and had no throttle).
        Features.SetSteering(true);
        Steering.SetPadPedal(PadKeys.Throttle, true);
        Check(Steering.PadThrottleHeld, "a pad key held while steering is off is remembered");
        Steering.SetPadPedal(PadKeys.Throttle, false);
        Check(!Steering.PadThrottleHeld, "and letting go clears it");

        // Settings > Features: with the Steering feature switched off, a
        // pedal is not pressed and steering does not come on.
        Features.SetSteering(false);
        Steering.SetPadPedal(PadKeys.Throttle, true);
        Check(!Steering.PadThrottleHeld, "with the feature switched off a pad key is not pressed");
        Steering.SetOn(true);
        Check(!Steering.IsOn, "and steering does not come on");
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
