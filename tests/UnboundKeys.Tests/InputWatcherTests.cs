namespace UnboundKeys.Tests;

// What the two watchers decide for each real mouse and key event: which
// are swallowed, which pass, and what gets pressed. Fed made-up events, as
// IInputHooks would feed real ones, so nothing here touches the real mouse
// or keyboard.
internal static class InputWatcherTests
{
    private static bool _ok;

    private const ushort KeyA = 0x41;
    private const ushort KeyB = 0x42;
    private const ushort CapsLock = 0x14;

    public static bool Run()
    {
        _ok = true;
        Section("mouse events: a mapped button is taken over, an unmapped one passes", MouseButtons);
        Section("mouse events: a right-click over UnboundKeys' own windows passes", RightOverOwnWindow);
        Section("key events: Caps Lock twice is Stop, and is never swallowed", CapsTwice);
        Section("key events: a remapped key is taken over, an untouched one passes", RemappedKey);
        return _ok;
    }

    private static MouseEvent Down(string button) => new(MouseEventKind.Down, button, 500, 500);
    private static MouseEvent Up(string button) => new(MouseEventKind.Up, button, 500, 500);

    private static void MouseButtons()
    {
        MouseMap.SwitchProfile("Default");
        foreach (var id in new[] { "middle", "wheelup", "right" })
            MouseMap.ResetToDefault(id);
        var pressed = new List<string>();
        using var mouse = new MouseInputWatcher((_, _) => false);
        mouse.ButtonPressed += pressed.Add;
        try
        {
            Check(!mouse.Handle(Down("middle")) && !mouse.Handle(Up("middle")) && pressed.Count == 0,
                  "an unmapped button's click passes through, both halves, and presses nothing");

            MouseMap.Rebind("middle", KeyA);
            Check(mouse.Handle(Down("middle")) && pressed.SequenceEqual(new[] { "middle" }),
                  "a mapped button's press is swallowed and runs its mapping, at once");
            Check(mouse.Handle(Up("middle")) && pressed.Count == 1,
                  "its release is swallowed too, and presses nothing more");

            MouseMap.Rebind("wheelup", KeyB);
            Check(mouse.Handle(Down("wheelup")) && pressed.Count == 2 && pressed[1] == "wheelup",
                  "a mapped wheel notch is swallowed and runs its mapping");
            Check(!mouse.Handle(Down("wheeldown")), "the other direction, unmapped, still scrolls");

            Check(!mouse.Handle(new MouseEvent(MouseEventKind.Move, null, 10, 10)),
                  "a pointer move passes while steering is off");
        }
        finally
        {
            MouseMap.ResetToDefault("middle");
            MouseMap.ResetToDefault("wheelup");
        }
    }

    private static void RightOverOwnWindow()
    {
        MouseMap.SwitchProfile("Default");
        bool overOwnWindow = true;
        var pressed = new List<string>();
        using var mouse = new MouseInputWatcher((_, _) => overOwnWindow);
        mouse.ButtonPressed += pressed.Add;
        try
        {
            MouseMap.Rebind("right", KeyA);
            Check(!mouse.Handle(Down("right")) && !mouse.Handle(Up("right")) && pressed.Count == 0,
                  "with the right button mapped, a right-click over the dashboard still goes to the dashboard");

            overOwnWindow = false;
            Check(mouse.Handle(Down("right")) && mouse.Handle(Up("right")) && pressed.SequenceEqual(new[] { "right" }),
                  "anywhere else it is swallowed and runs the mapping");

            overOwnWindow = true;
            bool downPassed = !mouse.Handle(Down("right"));
            overOwnWindow = false;
            Check(downPassed && !mouse.Handle(Up("right")),
                  "a click begun over the dashboard ends there, even if the pointer has left it");
        }
        finally
        {
            MouseMap.ResetToDefault("right");
        }
    }

    private static void CapsTwice()
    {
        int stops = 0;
        using var keys = new PhysicalKeyWatcher();
        keys.StopRequested += () => stops++;

        bool swallowed = keys.Handle(new KeyEvent(CapsLock, true));
        swallowed |= keys.Handle(new KeyEvent(CapsLock, false));
        Check(stops == 0, "one tap of Caps Lock does nothing");
        swallowed |= keys.Handle(new KeyEvent(CapsLock, true));
        swallowed |= keys.Handle(new KeyEvent(CapsLock, false));
        Check(stops == 1, "a second tap straight after asks for Stop");
        Check(!swallowed, "and none of it is swallowed: Caps Lock still does its own job");

        // A key held down repeats its key-down; that is one tap, not many.
        keys.Handle(new KeyEvent(CapsLock, true));
        keys.Handle(new KeyEvent(CapsLock, true));
        keys.Handle(new KeyEvent(CapsLock, true));
        keys.Handle(new KeyEvent(CapsLock, false));
        Check(stops == 1, "Caps Lock held down is one tap, however long it repeats");
    }

    private static void RemappedKey()
    {
        VirtualKeyMap.SwitchProfile("Default");
        VirtualKeyMap.ResetToDefault("va");
        var pressed = new List<string>();
        using var keys = new PhysicalKeyWatcher();
        keys.VirtualKeyPressed += pressed.Add;
        try
        {
            Check(!keys.Handle(new KeyEvent(KeyA, true)) && !keys.Handle(new KeyEvent(KeyA, false)) && pressed.Count == 0,
                  "an untouched letter types as it always did");

            VirtualKeyMap.Rebind("va", KeyB);
            Check(keys.Handle(new KeyEvent(KeyA, true)) && pressed.SequenceEqual(new[] { "va" }),
                  "a remapped letter's press is swallowed and handed on as that key's mapping");
            Check(keys.Handle(new KeyEvent(KeyA, true)) && keys.Handle(new KeyEvent(KeyA, true)) && pressed.Count == 1,
                  "held down, its repeats are swallowed and count as the one press");
            Check(keys.Handle(new KeyEvent(KeyA, false)), "its release is swallowed too");
            Check(!keys.Handle(new KeyEvent(KeyA, false)), "a release with no press before it is not");
        }
        finally
        {
            VirtualKeyMap.ResetToDefault("va");
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
