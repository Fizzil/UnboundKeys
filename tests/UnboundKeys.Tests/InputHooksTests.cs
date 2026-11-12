using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UnboundKeys.Tests;

// The real hooks (WindowsInputHooks): on a thread of their own, showing
// the app real keys but not its own, swallowing what a handler says to,
// and put back by the watchdog after an answer slow enough for Windows to
// have dropped them. The "real" key is F15 sent WITHOUT the app's own
// mark, which is what a key from a keyboard looks like to the hook; every
// one is swallowed, so nothing reaches the program in front.
internal static class InputHooksTests
{
    private static bool _ok;

    private const ushort F15 = 0x7E;
    private const ushort F14 = 0x7D;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // A key with no mark of the app's on it, as a real keyboard's.
    private static void SendUnmarked(ushort vk, bool down)
    {
        var input = new INPUT
        {
            type = 1,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0u : 2u } },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void TapUnmarked(ushort vk)
    {
        SendUnmarked(vk, true);
        SendUnmarked(vk, false);
    }

    public static bool Run()
    {
        _ok = true;
        Section("input hooks: real keys are heard on the hooks' own thread, the app's own are not", HeardOnOwnThread);
        Section("input hooks: the watchdog puts the hooks back after a slow answer", Watchdog);
        return _ok;
    }

    private static bool Within(int ms, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < ms)
        {
            if (condition())
                return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    private static void HeardOnOwnThread()
    {
        var gate = new object();
        var heard = new List<(ushort Key, bool Down, int Thread)>();
        using var hooks = new WindowsInputHooks();
        hooks.Start(
            _ => false,
            e =>
            {
                if (e.Key != F15 && e.Key != F14)
                    return false;
                lock (gate)
                    heard.Add((e.Key, e.Down, Environment.CurrentManagedThreadId));
                return true;
            });

        TapUnmarked(F15);
        bool both = Within(1000, () => { lock (gate) return heard.Count >= 2; });
        (ushort Key, bool Down, int Thread)[] seen;
        lock (gate)
            seen = heard.ToArray();
        Check(both && seen.Length == 2 && seen[0] == (F15, true, seen[0].Thread) && seen[1].Key == F15 && !seen[1].Down,
              "a real key's press and release reach the handler, in order");
        Check(seen.Length > 0 && seen.All(s => s.Thread != Environment.CurrentManagedThreadId),
              "on a thread that is not the one that started the hooks");

        // The app's own output carries its mark and is never shown.
        KeyOutput.TapKey(F14, false);
        Thread.Sleep(300);
        int count;
        lock (gate)
            count = heard.Count;
        Check(count == 2, "a key the app itself sends is not handed to the handler");
    }

    private static void Watchdog()
    {
        int heard = 0;
        bool slowNext = true;
        using var hooks = new WindowsInputHooks();
        hooks.Start(
            _ => false,
            e =>
            {
                if (e.Key != F15)
                    return false;
                if (e.Down && slowNext)
                {
                    // Slower than WindowsInputHooks.SlowAnswerMs: the kind
                    // of answer Windows removes a hook for.
                    slowNext = false;
                    Thread.Sleep(WindowsInputHooks.SlowAnswerMs + 60);
                }
                Interlocked.Increment(ref heard);
                return true;
            });

        Check(hooks.Reinstalls == 0, "freshly started, the hooks have not been put back");
        TapUnmarked(F15);
        Check(Within(3000, () => hooks.Reinstalls == 1), "after one slow answer the watchdog puts them back, within a few seconds");

        int before = Volatile.Read(ref heard);
        TapUnmarked(F15);
        Check(Within(1000, () => Volatile.Read(ref heard) >= before + 2), "and keys are heard again afterwards");
        Thread.Sleep(2500);
        Check(hooks.Reinstalls == 1, "with quick answers again, it leaves them alone");
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
