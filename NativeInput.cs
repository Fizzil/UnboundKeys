using System.Runtime.InteropServices;

namespace VoicePress;

// Sends real hardware-style keystrokes via the Windows SendInput API.
// Games (most use DirectInput or raw input) ignore the simpler simulated
// key-press methods, so this lower-level approach is needed for it to work in-game.
internal static class NativeInput
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;
    // fizzil^.^ was here

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

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    // The union must include every possible event type (even the ones we never use)
    // so its marshaled size matches the real native INPUT struct exactly — Windows
    // rejects the whole call (silently, no exception) if the size passed to SendInput
    // doesn't match what it expects, which was the actual bug behind keys not appearing.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    // Which window the user is currently focused on — used to release an
    // infinite hold/repeat automatically if focus moves away from whatever
    // window it was started in (e.g. alt-tabbing out of a game). Named
    // distinctly from the unrelated Win32 GetActiveWindow (which returns the
    // calling thread's own active window, not the true system-wide one).
    public static IntPtr GetFocusedWindow() => GetForegroundWindow();

    // A browser switching tabs doesn't change the foreground window at all —
    // it's the same window handle the whole time — but most tabbed apps
    // (browsers included) do update the window's title text to match
    // whichever tab/document is now showing. Watching for that lets us catch
    // a tab switch too, not just switching to a different window entirely.
    public static string GetWindowTitle(IntPtr hWnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        GetWindowText(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static void TapKey(ushort virtualKeyCode, bool extended)
    {
        KeyDown(virtualKeyCode, extended);
        KeyUp(virtualKeyCode, extended);
    }

    public static void KeyDown(ushort virtualKeyCode, bool extended)
    {
        ushort scanCode = (ushort)MapVirtualKey(virtualKeyCode, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | (extended ? KEYEVENTF_EXTENDEDKEY : 0);

        var down = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wScan = scanCode, dwFlags = flags } }
        };

        SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
    }

    public static void KeyUp(ushort virtualKeyCode, bool extended)
    {
        ushort scanCode = (ushort)MapVirtualKey(virtualKeyCode, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP | (extended ? KEYEVENTF_EXTENDEDKEY : 0);

        var up = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wScan = scanCode, dwFlags = flags } }
        };

        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }
}
