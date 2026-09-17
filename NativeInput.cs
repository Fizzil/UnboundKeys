using System.Runtime.InteropServices;

namespace VoicePress;

// Sends real hardware-style keystrokes via the Windows SendInput API.
// Games (most use DirectInput or raw input) ignore the simpler simulated
// key-press methods, so this lower-level approach is needed for it to work in-game.
internal static class NativeInput
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    // The Win32 virtual-key codes Windows itself reserves for these mouse
    // buttons (VK_LBUTTON, VK_RBUTTON, VK_MBUTTON, VK_XBUTTON1/2) — see
    // KeyCatalog.MouseButtons. KeyDown/KeyUp below recognize these and send
    // a real mouse click via MOUSEINPUT instead of a keyboard scan code.
    private const ushort VK_LBUTTON = 0x01;
    private const ushort VK_RBUTTON = 0x02;
    private const ushort VK_MBUTTON = 0x04;
    private const ushort VK_XBUTTON1 = 0x05;
    private const ushort VK_XBUTTON2 = 0x06;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_XDOWN = 0x0080;
    private const uint MOUSEEVENTF_XUP = 0x0100;
    private const uint XBUTTON1 = 0x0001;
    private const uint XBUTTON2 = 0x0002;

    // Tags every mouse click VoicePress itself sends, via MOUSEINPUT's
    // dwExtraInfo field — a field that exists specifically for this kind
    // of "mark an injected event as mine" use. MouseInputWatcher's hook
    // checks for this exact value so it only ignores VoicePress's own
    // synthetic clicks — not every software-injected event in general.
    // That distinction matters: many gaming mice run vendor driver
    // software that relays real physical input through the same
    // SendInput-style injection path, so a blanket "ignore anything
    // injected" check would also swallow genuinely real clicks/scrolls
    // from a mouse like that, not just VoicePress's own output.
    internal static readonly IntPtr InjectedByVoicePress = (IntPtr)0x56505253; // "VPRS"
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
        if (IsMouseButtonVk(virtualKeyCode))
        {
            SendMouseButtonEvent(virtualKeyCode, down: true);
            return;
        }

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
        if (IsMouseButtonVk(virtualKeyCode))
        {
            SendMouseButtonEvent(virtualKeyCode, down: false);
            return;
        }

        ushort scanCode = (ushort)MapVirtualKey(virtualKeyCode, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP | (extended ? KEYEVENTF_EXTENDEDKEY : 0);

        var up = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wScan = scanCode, dwFlags = flags } }
        };

        SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
    }

    private static bool IsMouseButtonVk(ushort vk) =>
        vk is VK_LBUTTON or VK_RBUTTON or VK_MBUTTON or VK_XBUTTON1 or VK_XBUTTON2;

    // A click always lands wherever the real OS cursor currently is — same
    // as a real click, since this is the same SendInput a hardware mouse's
    // driver ultimately goes through too.
    private static void SendMouseButtonEvent(ushort vk, bool down)
    {
        uint flags = vk switch
        {
            VK_LBUTTON => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            VK_RBUTTON => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            VK_MBUTTON => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            VK_XBUTTON1 or VK_XBUTTON2 => down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP,
            _ => 0,
        };
        uint mouseData = vk switch
        {
            VK_XBUTTON1 => XBUTTON1,
            VK_XBUTTON2 => XBUTTON2,
            _ => 0,
        };

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = mouseData, dwExtraInfo = InjectedByVoicePress } }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }
}
