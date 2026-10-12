using System.Diagnostics;

namespace KlycKonsol.Input;

/// <summary>
/// Masaüstü modu: controller'ı fare + klavye gibi kullanır.
/// Sol analog = imleç (LT: hassas, RT: hızlı), sağ analog = kaydırma, A = sol tık, B = sağ tık.
/// </summary>
public sealed class DesktopMode
{
    const double Dead = 0.18;
    const double CursorSpeed = 1300;   // px/sn, tam itişte
    const double ScrollNotchesPerSec = 14;

    /// <summary>false ise B sağ tık yapmaz (tarayıcıda B = geri).</summary>
    public bool BIsRightClick { get; set; } = true;

    bool _a, _b, _blocked;
    public static long Ticks, Moves, Failures;
    public static int LastError, LastX, LastY;
    double _remX, _remY, _scrollY, _scrollX;

    /// <summary>A/B bırakılana kadar tıklamayı yok say (menüden çıkarken yanlışlıkla tıklamasın).</summary>
    public void Block() => _blocked = true;

    public void Reset()
    {
        _blocked = false;
        if (_a) Native.MouseButton(Native.LEFTUP);
        if (_b) Native.MouseButton(Native.RIGHTUP);
        _a = _b = false;
        _remX = _remY = _scrollX = _scrollY = 0;
    }

    public void Update(in PadState s, double dt)
    {
        Ticks++;
        // İmleç
        var (mx, my) = Stick(s.LX, s.LY);
        if (mx != 0 || my != 0)
        {
            var speed = CursorSpeed * (s.LT > 60 ? 0.25 : 1) * (s.RT > 60 ? 2.2 : 1);
            _remX += mx * speed * dt;
            _remY -= my * speed * dt;
            var ix = (int)_remX;
            var iy = (int)_remY;
            _remX -= ix;
            _remY -= iy;
            if ((ix != 0 || iy != 0) && Native.GetCursorPos(out var p))
            {
                Moves++;
                if (!Native.SetCursorPos(p.X + ix, p.Y + iy)) { Failures++; LastError = System.Runtime.InteropServices.Marshal.GetLastWin32Error(); }
                LastX = p.X + ix; LastY = p.Y + iy;
            }
        }

        // Kaydırma
        var (sx, sy) = Stick(s.RX, s.RY);
        _scrollY += sy * ScrollNotchesPerSec * dt;
        _scrollX += sx * ScrollNotchesPerSec * dt;
        if (Math.Abs(_scrollY) >= 0.25) { var d = (int)(_scrollY * 120); Native.Wheel(d, false); _scrollY -= d / 120.0; }
        if (Math.Abs(_scrollX) >= 0.25) { var d = (int)(_scrollX * 120); Native.Wheel(d, true); _scrollX -= d / 120.0; }

        // Tıklamalar (basılı tutma = sürükleme)
        var a = s.Buttons.HasFlag(Btn.A);
        var b = BIsRightClick && s.Buttons.HasFlag(Btn.B);
        if (_blocked)
        {
            if (!s.Buttons.HasFlag(Btn.A) && !s.Buttons.HasFlag(Btn.B)) _blocked = false;
            a = b = false;
        }
        if (a != _a) { Native.MouseButton(a ? Native.LEFTDOWN : Native.LEFTUP); _a = a; }
        if (b != _b) { Native.MouseButton(b ? Native.RIGHTDOWN : Native.RIGHTUP); _b = b; }
    }

    /// <summary>Tek seferlik tuşlar (yön tuşları, Enter, ekran klavyesi...).</summary>
    public void HandleKey(PadKey key)
    {
        switch (key)
        {
            case PadKey.Up: Native.Tap(Native.VK_UP); break;
            case PadKey.Down: Native.Tap(Native.VK_DOWN); break;
            case PadKey.Left: Native.Tap(Native.VK_LEFT); break;
            case PadKey.Right: Native.Tap(Native.VK_RIGHT); break;
            case PadKey.X: Native.Tap(Native.VK_RETURN); break;
            case PadKey.LB: Native.Chord(Native.VK_MENU, Native.VK_LEFT); break;   // geri
            case PadKey.RB: Native.Chord(Native.VK_MENU, Native.VK_RIGHT); break;  // ileri
            case PadKey.Start: Native.Tap(Native.VK_LWIN); break;
            case PadKey.Y: ToggleKeyboard(); break;
        }
    }

    static void ToggleKeyboard()
    {
        var running = Process.GetProcessesByName("osk");
        if (running.Length > 0)
        {
            foreach (var p in running) { try { p.Kill(); } catch { } }
            return;
        }
        try { Process.Start(new ProcessStartInfo("osk.exe") { UseShellExecute = true }); } catch { }
    }

    static (double x, double y) Stick(short rawX, short rawY)
    {
        double x = rawX / 32768.0, y = rawY / 32768.0;
        var mag = Math.Sqrt(x * x + y * y);
        if (mag < Dead) return (0, 0);
        var scaled = Math.Min((mag - Dead) / (1 - Dead), 1);
        var curve = scaled * scaled;           // hassas orta bölge, hızlı uç
        return (x / mag * curve, y / mag * curve);
    }
}
