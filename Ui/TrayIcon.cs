using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KlycKonsol.Ui;

/// <summary>
/// Bildirim alanı simgesi (WinForms bağımlılığı olmadan, doğrudan Shell_NotifyIcon).
/// Sol tık: Konsol'u aç/gizle · Sağ tık: menü (aç/gizle, çık). Mesaj penceresi tek bir HwndSource; oyun sırasında ek yük yok.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    const int WM_TRAY = 0x8001, WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_NULL = 0, WM_CONTEXTMENU = 0x007B;
    const uint NIM_ADD = 0, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint MF_STRING = 0, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x20;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] struct PT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint ExtractIconExW(string file, int index, IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);

    readonly HwndSource _src;
    readonly Action _toggle, _exit;
    readonly Func<bool> _isOpen;
    readonly uint _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
    IntPtr _icon;
    bool _added;
    DateTime _lastToggle;

    public TrayIcon(Action toggle, Action exit, Func<bool> isOpen)
    {
        _toggle = toggle; _exit = exit; _isOpen = isOpen;
        var p = new HwndSourceParameters("KlycKonsolTray") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, WindowStyle = 0 };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
        try { ExtractIconExW(Environment.ProcessPath!, 0, IntPtr.Zero, out _icon, 1); } catch { }   // exe'ye gömülü simge
        Add();
        // Ctrl+Alt+K: kol yokken / oyun içindeyken de Konsol'u aç-gizle (başka program tuşu almışsa sessizce atlanır)
        Log($"Ctrl+Alt+K kaydı: {RegisterHotKey(_src.Handle, 1, 0x0001 | 0x0002 | 0x4000, 0x4B)}");
        _ = PromoteAsync();
    }

    /// <summary>
    /// Windows 11 yeni simgeleri "gizli simgeler" çekmecesine koyar. İlk çalıştırmada (kullanıcı kendi tercihini
    /// yapmadıysa) simgeyi görev çubuğunda görünür yapar; kullanıcı sonradan gizlerse (IsPromoted=0) dokunmaz.
    /// </summary>
    static async Task PromoteAsync()
    {
        try
        {
            await Task.Delay(1500);   // Kabuk, kaydı NIM_ADD'den biraz sonra oluşturur
            using var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", writable: true);
            if (root == null) return;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name, writable: true);
                if (k?.GetValue("ExecutablePath") is string path
                    && string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)
                    && k.GetValue("IsPromoted") == null)
                    k.SetValue("IsPromoted", 1, Microsoft.Win32.RegistryValueKind.DWord);
            }
        }
        catch { }
    }

    void Toggle()
    {
        if ((DateTime.UtcNow - _lastToggle).TotalMilliseconds < 500) return;   // çift tık iki kez tetiklemesin
        _lastToggle = DateTime.UtcNow;
        _toggle();
    }

    void Add()
    {
        if (_icon == IntPtr.Zero) { Log("simge çıkarılamadı"); return; }
        var d = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _src.Handle, uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP, uCallbackMessage = WM_TRAY, hIcon = _icon, szTip = "KLYC Konsol",
            szInfo = "", szInfoTitle = "",
        };
        _added = Shell_NotifyIconW(NIM_ADD, ref d);
        Log($"NIM_ADD={_added} hwnd={_src.Handle} icon={_icon} cb={d.cbSize}");
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && (int)wParam == 1) { Toggle(); handled = true; return IntPtr.Zero; }   // WM_HOTKEY
        if (msg == _taskbarCreated) { Add(); return IntPtr.Zero; }   // Explorer yeniden başladıysa simgeyi geri koy
        if (msg != WM_TRAY) return IntPtr.Zero;
        switch ((int)lParam)
        {
            case WM_LBUTTONUP: Toggle(); handled = true; break;
            case WM_RBUTTONUP: case WM_CONTEXTMENU: ShowMenu(); handled = true; break;
        }
        return IntPtr.Zero;
    }

    bool _menuOpen;

    void ShowMenu()
    {
        if (_menuOpen) return;   // RBUTTONUP + CONTEXTMENU art arda gelirse ikinci menü açılmasın
        _menuOpen = true;
        var menu = CreatePopupMenu();
        try
        {
            var open = _isOpen();
            AppendMenuW(menu, MF_STRING | 0x3 /* devre dışı + gri: başlık */, (UIntPtr)99, open ? "KLYC Konsol — açık" : "KLYC Konsol — arka planda / oyun modu");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)1, open ? "Konsol'u gizle (oyun modu)" : "Konsol'u aç");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)2, "Konsol'dan çık");
            GetCursorPos(out var pt);
            SetForegroundWindow(_src.Handle);   // menü, dışarı tıklanınca kapansın
            Log($"menü açılıyor ({pt.X},{pt.Y})");
            var cmd = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, _src.Handle, IntPtr.Zero);
            Log($"menü sonucu {cmd} (hata {Marshal.GetLastWin32Error()})");
            PostMessageW(_src.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (cmd == 1) Toggle();
            else if (cmd == 2) _exit();
        }
        finally { DestroyMenu(menu); _menuOpen = false; }
    }

    static void Log(string text)
    {
        try { File.AppendAllText(Path.Combine(Library.AppPaths.Dir, "tray.log"), $"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}"); } catch { }
    }

    public void Dispose()
    {
        try { UnregisterHotKey(_src.Handle, 1); } catch { }
        if (_added)
        {
            var d = new NOTIFYICONDATA { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _src.Handle, uID = 1, szTip = "", szInfo = "", szInfoTitle = "" };
            Shell_NotifyIconW(NIM_DELETE, ref d);
            _added = false;
        }
        _src.Dispose();
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
    }
}
