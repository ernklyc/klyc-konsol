using System.Runtime.InteropServices;

namespace KlycKonsol.Input;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);

    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
    const uint WHEEL = 0x0800, HWHEEL = 0x1000;
    const uint KEYUP = 0x0002, EXTENDED = 0x0001;

    public const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B, VK_F4 = 0x73;
    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_LWIN = 0x5B;

    static void Send(INPUT i) => SendInput(1, new[] { i }, Marshal.SizeOf<INPUT>());

    public static void MouseButton(uint flag) =>
        Send(new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flag } } });

    public static void Wheel(int delta, bool horizontal) =>
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion { mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = horizontal ? HWHEEL : WHEEL } },
        });

    static bool IsExtended(int vk) => vk is VK_LEFT or VK_UP or VK_RIGHT or VK_DOWN;

    static void Key(int vk, bool up) =>
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = (up ? KEYUP : 0) | (IsExtended(vk) ? EXTENDED : 0) } },
        });

    public static void Tap(int vk) { Key(vk, false); Key(vk, true); }

    public const int VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1, VK_MEDIA_PLAY_PAUSE = 0xB3;

    /// <summary>Odaktaki pencereye bir karakter yazar (klavye düzeninden bağımsız, Unicode).</summary>
    public static void TypeChar(char c)
    {
        const uint UNICODE = 0x0004;
        Send(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE } } });
        Send(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = UNICODE | KEYUP } } });
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    /// <summary>Pil yüzdesi (0-100) ve şarjda mı; pil yoksa null.</summary>
    public static (int percent, bool charging)? Battery()
    {
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryLifePercent is 0 or > 100) return null;
        return (s.BatteryLifePercent, s.ACLineStatus == 1);
    }

    /// <summary>Ctrl+Shift+Esc: Görev Yöneticisi.</summary>
    public static void TaskManager()
    {
        Key(VK_CONTROL, false); Key(VK_SHIFT, false);
        Tap(VK_ESCAPE);
        Key(VK_SHIFT, true); Key(VK_CONTROL, true);
    }

    public static void Chord(int modifier, int vk)
    {
        Key(modifier, false);
        Tap(vk);
        Key(modifier, true);
    }

    /// <summary>Windows ön plana çıkarma kısıtını Alt tuşu hilesiyle aşar.</summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(hWnd);
        keybd_event(VK_MENU, 0, KEYUP, UIntPtr.Zero);
    }
}
