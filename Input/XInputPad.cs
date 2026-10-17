using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace KlycKonsol.Input;

public enum PadKey { Up, Down, Left, Right, A, B, X, Y, LB, RB, Start, Back, Guide }

[Flags]
public enum Btn : ushort
{
    Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008,
    Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080,
    LB = 0x0100, RB = 0x0200, Guide = 0x0400,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
}

public struct PadState
{
    public Btn Buttons;
    public byte LT, RT;
    public short LX, LY, RX, RY;
}

/// <summary>Bir XInput yuvasının (0-3) tanıtım bilgisi.</summary>
public sealed record PadInfo(int Slot, bool Connected, string Kind, bool Wireless, string Battery, double Deadzone);

/// <summary>
/// XInput controller'ları okur: Xbox 360 / One / Series ve XInput modundaki üçüncü parti kollar (GameSir vb.), aynı anda en çok 4 tane.
/// Hepsinin girdisi tek bir "birleşik kol"da toplanır (hangi kolla basılırsa basılsın arayüz çalışır).
/// Yön tuşları ve sol analog menü gezintisi için tekrarlı (basılı tutunca kayan) tuş olayına çevrilir.
/// Düşük güç modu: Konsol gizliyken (oyun oynanırken) arayüz iş parçacığını uyandırmadan yalnızca Guide / Start+Back aranır.
/// </summary>
public sealed class XInputPad
{
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }

    [StructLayout(LayoutKind.Sequential)]
    struct XCaps { public byte Type, SubType; public ushort Flags; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; public ushort LMotor, RMotor; }

    [StructLayout(LayoutKind.Sequential)]
    struct XBattery { public byte Type, Level; }

    [StructLayout(LayoutKind.Sequential)]
    struct XVibration { public ushort Left, Right; }

    // Ordinal 100 = XInputGetStateEx (Guide tuşunu da verir). Yoksa normal GetState'e düşeriz.
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern uint GetStateEx(uint i, out XState s);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint GetState(uint i, out XState s);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")] static extern uint GetCaps(uint i, uint flags, out XCaps c);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")] static extern uint GetBattery(uint i, byte devType, out XBattery b);
    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")] static extern uint SetVibration(uint i, ref XVibration v);

    const double NavThreshold = 0.55;
    const long RepeatDelayMs = 380;
    const long RepeatRateMs = 100;
    public const double DefaultDeadzone = 0.24;   // 7849 / 32767: Microsoft'un önerdiği sol çubuk ölü bölgesi

    readonly DispatcherTimer _timer = new(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(ActiveMs) };
    readonly Dictionary<PadKey, long> _nextRepeat = new();
    readonly HashSet<PadKey> _down = new();
    readonly bool[] _connected = new bool[4];
    readonly long[] _nextScan = new long[4];
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly object _lock = new();
    readonly WinMmPads _hid = new();   // PlayStation / Switch Pro / genel HID kollar (XInput dışı)
    Timer? _lowTimer;
    bool _hasEx = true;
    bool _lowPower;
    long _lastTick = Environment.TickCount64;
    Btn _virtualButtons;            // --debugcmd ile enjekte edilen sanal tuşlar
    short _vlx, _vly, _vrx, _vry;   // ... ve sanal çubuklar
    Btn _lowPrevGuideCombo;
    int _lowConnectedCount;

    /// <summary>Kol başına ölü bölge (0-1). 360 kollarda çubuk kayması (drift) için büyütülebilir.</summary>
    public double[] Deadzone { get; } = { DefaultDeadzone, DefaultDeadzone, DefaultDeadzone, DefaultDeadzone };

    /// <summary>
    /// false iken sol çubuk menü gezintisi (yön tuşu) olayı üretmez. Çubuk imleç olarak kullanılırken (tarayıcı içeriği, masaüstü modu)
    /// aynı hareketin ayrıca ok tuşu basmaması için kapatılır; yalnızca gerçek D-pad yön olayı verir.
    /// </summary>
    public volatile bool StickNavigation = true;

    /// <summary>Test: gerçek kolları yok say, yalnızca enjekte edilen sanal tuşları kullan (--virtualonly).</summary>
    public bool VirtualOnly { get; set; }

    /// <summary>Tüm kolların birleşik durumu.</summary>
    public PadState State { get; private set; }
    public bool Connected { get; private set; }
    public bool IsLowPower => _lowPower;
    public int ConnectedCount => _connected.Count(c => c) + _hid.Count;

    public event Action<PadKey>? Pressed;
    public event Action<bool>? ConnectionChanged;
    /// <summary>Her karede çağrılır: (birleşik durum, saniye cinsinden geçen süre).</summary>
    public event Action<PadState, double>? Tick;

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    // ---- Okuma ----------------------------------------------------------

    bool TryRead(int slot, out XState s)
    {
        if (_hasEx)
        {
            try { return GetStateEx((uint)slot, out s) == 0; }
            catch (EntryPointNotFoundException) { _hasEx = false; }
        }
        return GetState((uint)slot, out s) == 0;
    }

    /// <summary>Radyal ölü bölge: eşik altı sıfır, üstü 0..1'e yeniden ölçeklenir.</summary>
    static (short x, short y) Dead(short rx, short ry, double dz)
    {
        double x = rx / 32768.0, y = ry / 32768.0;
        var m = Math.Sqrt(x * x + y * y);
        if (m < dz) return (0, 0);
        var k = Math.Min((m - dz) / (1 - dz), 1.0) / m;
        return ((short)Math.Clamp(x * k * 32767, -32767, 32767), (short)Math.Clamp(y * k * 32767, -32767, 32767));
    }

    /// <summary>Bağlı tüm kolları oku ve tek durumda birleştir.</summary>
    bool ReadMerged(long now, out PadState merged)
    {
        merged = default;
        double bestL = 0, bestR = 0;
        var any = false;

        for (var slot = 0; slot < 4 && !VirtualOnly; slot++)
        {
            if (!_connected[slot] && now < _nextScan[slot]) continue;   // boş yuvaları saniyede bir yokla (her karede yoklamak yavaş)

            if (!TryRead(slot, out var raw))
            {
                if (_connected[slot]) { _connected[slot] = false; }
                _nextScan[slot] = now + 1000;
                continue;
            }
            _connected[slot] = true;
            any = true;

            merged.Buttons |= (Btn)raw.Buttons;
            merged.LT = Math.Max(merged.LT, raw.LT);
            merged.RT = Math.Max(merged.RT, raw.RT);

            var dz = Deadzone[slot];
            var (lx, ly) = Dead(raw.LX, raw.LY, dz);
            var (rx, ry) = Dead(raw.RX, raw.RY, dz);
            var ml = Math.Abs(lx) + Math.Abs(ly);
            var mr = Math.Abs(rx) + Math.Abs(ry);
            if (ml > bestL) { bestL = ml; merged.LX = lx; merged.LY = ly; }
            if (mr > bestR) { bestR = mr; merged.RX = rx; merged.RY = ry; }
        }

        // XInput dışı kollar (PlayStation, Switch Pro...): winmm ile, kol arka plandayken de okunur
        if (!VirtualOnly)
        {
            _hid.RescanIfDue(now, _connected.Any(c => c));
            if (_hid.TryRead(out var h))
            {
                any = true;
                merged.Buttons |= h.Buttons;
                merged.LT = Math.Max(merged.LT, h.LT); merged.RT = Math.Max(merged.RT, h.RT);
                var (hlx, hly) = Dead(h.LX, h.LY, DefaultDeadzone);
                var (hrx, hry) = Dead(h.RX, h.RY, DefaultDeadzone);
                var hl = Math.Abs(hlx) + Math.Abs(hly); var hr = Math.Abs(hrx) + Math.Abs(hry);
                if (hl > bestL) { bestL = hl; merged.LX = hlx; merged.LY = hly; }
                if (hr > bestR) { bestR = hr; merged.RX = hrx; merged.RY = hry; }
            }
        }

        if (_virtualButtons != 0) { merged.Buttons |= _virtualButtons; any = true; }
        if (_vlx != 0 || _vly != 0 || _vrx != 0 || _vry != 0)
        {
            any = true;
            if (Math.Abs(_vlx) + Math.Abs(_vly) > bestL) { merged.LX = _vlx; merged.LY = _vly; }
            if (Math.Abs(_vrx) + Math.Abs(_vry) > bestR) { merged.RX = _vrx; merged.RY = _vry; }
        }
        return any;
    }

    void Poll()
    {
        var now = Environment.TickCount64;
        var dt = Math.Min((now - _lastTick) / 1000.0, 0.1);
        _lastTick = now;

        var any = ReadMerged(now, out var st);
        if (any != Connected) { Connected = any; ConnectionChanged?.Invoke(any); }
        if (!any) { State = default; _down.Clear(); Tune(false, default, now); return; }
        State = st;

        var lx = st.LX / 32768.0;
        var ly = st.LY / 32768.0;
        var stick = StickNavigation;
        Update(PadKey.Up, st.Buttons.HasFlag(Btn.Up) || (stick && ly > NavThreshold), true, now);
        Update(PadKey.Down, st.Buttons.HasFlag(Btn.Down) || (stick && ly < -NavThreshold), true, now);
        Update(PadKey.Left, st.Buttons.HasFlag(Btn.Left) || (stick && lx < -NavThreshold), true, now);
        Update(PadKey.Right, st.Buttons.HasFlag(Btn.Right) || (stick && lx > NavThreshold), true, now);
        Update(PadKey.A, st.Buttons.HasFlag(Btn.A), false, now);
        Update(PadKey.B, st.Buttons.HasFlag(Btn.B), false, now);
        Update(PadKey.X, st.Buttons.HasFlag(Btn.X), false, now);
        Update(PadKey.Y, st.Buttons.HasFlag(Btn.Y), false, now);
        Update(PadKey.LB, st.Buttons.HasFlag(Btn.LB), false, now);
        Update(PadKey.RB, st.Buttons.HasFlag(Btn.RB), false, now);
        Update(PadKey.Start, st.Buttons.HasFlag(Btn.Start), false, now);
        Update(PadKey.Back, st.Buttons.HasFlag(Btn.Back), false, now);
        Update(PadKey.Guide, st.Buttons.HasFlag(Btn.Guide), false, now);

        Tick?.Invoke(st, dt);
        Tune(true, st, now);
    }

    long _lastActive = Environment.TickCount64;

    /// <summary>
    /// Yoklama aralığı uyarlanır: kol yokken 60 ms (takılınca ≤60 ms'de görülür), kolda girdi varken 12 ms, 2 sn boşta kalınca 30 ms.
    /// Eskiden hep 12 ms'ydi: boştaki Konsol'da tek çekirdeğin ~%1'ini yiyordu (ölçüldü). İlk tuş basışı en çok bir aralık gecikir.
    /// </summary>
    void Tune(bool any, PadState st, long now)
    {
        var active = any && (st.Buttons != 0 || st.LX != 0 || st.LY != 0 || st.RX != 0 || st.RY != 0 || st.LT > 8 || st.RT > 8 || _down.Count > 0);
        if (active) _lastActive = now;
        var ms = NextIntervalMs(any, now - _lastActive);
        if ((int)_timer.Interval.TotalMilliseconds != ms) _timer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    public const int ActiveMs = 12, IdlePadMs = 30, IdleNoPadMs = 60;

    /// <summary>Saf mantık: kol yoksa 60 ms; son girdiden beri 2 sn geçmediyse 12 ms; sonra 30 ms.</summary>
    public static int NextIntervalMs(bool padPresent, long msSinceActive) => !padPresent ? IdleNoPadMs : msSinceActive < 2000 ? ActiveMs : IdlePadMs;

    void Update(PadKey key, bool isDown, bool repeat, long now)
    {
        if (!isDown) { _down.Remove(key); return; }

        if (_down.Add(key))
        {
            _nextRepeat[key] = now + RepeatDelayMs;
            Pressed?.Invoke(key);
        }
        else if (repeat && now >= _nextRepeat[key])
        {
            _nextRepeat[key] = now + RepeatRateMs;
            Pressed?.Invoke(key);
        }
    }

    // ---- Düşük güç modu (oyun oynanırken) -------------------------------

    /// <summary>
    /// true: arayüz zamanlayıcısı durur; ayrı bir iş parçacığı saniyede ~11 kez yalnızca Guide ve Start+Back'e bakar.
    /// Böylece oyun sırasında arayüz iş parçacığı hiç uyanmaz.
    /// </summary>
    public void SetLowPower(bool on)
    {
        if (on == _lowPower) return;
        _lowPower = on;
        if (on)
        {
            _timer.Stop();
            _down.Clear();
            _lowPrevGuideCombo = 0;
            _lowTimer = new Timer(_ => LowPoll(), null, 100, 90);
        }
        else
        {
            _lowTimer?.Dispose();
            _lowTimer = null;
            _lastTick = Environment.TickCount64;
            _timer.Start();
        }
    }

    void LowPoll()
    {
        try
        {
            var now = Environment.TickCount64;
            var any = false;
            Btn buttons = 0;
            for (var slot = 0; slot < 4; slot++)
            {
                if (!_connected[slot] && now < _nextScan[slot]) continue;
                if (!TryRead(slot, out var raw)) { _connected[slot] = false; _nextScan[slot] = now + 1500; continue; }
                _connected[slot] = true; any = true;
                buttons |= (Btn)raw.Buttons;
            }
            if (!VirtualOnly)
            {
                _hid.RescanIfDue(now, any);
                if (_hid.TryRead(out var hs)) { any = true; buttons |= hs.Buttons; }
            }
            _lowConnectedCount = any ? 1 : 0;

            // Yalnızca Guide ya da Start+Back (kenar algılama)
            var combo = buttons & (Btn.Guide | Btn.Start | Btn.Back);
            var guideNow = combo.HasFlag(Btn.Guide) || (combo.HasFlag(Btn.Start) && combo.HasFlag(Btn.Back));
            var guideBefore = _lowPrevGuideCombo.HasFlag(Btn.Guide) || (_lowPrevGuideCombo.HasFlag(Btn.Start) && _lowPrevGuideCombo.HasFlag(Btn.Back));
            _lowPrevGuideCombo = combo;
            if (guideNow && !guideBefore)
                _ui.BeginInvoke(() => Pressed?.Invoke(PadKey.Guide));

            if (!any && _lowTimer != null) _lowTimer.Change(1000, 1000);          // kol yoksa saniyede bir bak
            else _lowTimer?.Change(90, 90);
        }
        catch { /* sonraki turda tekrar */ }
    }

    // ---- Bilgi, titreşim, kalibrasyon -----------------------------------

    static string KindName(byte type, byte sub) => type switch
    {
        1 => sub switch { 1 => "Gamepad", 2 => "Direksiyon", 3 => "Arcade stick", 4 => "Uçuş kolu", 5 => "Dans pedi", 6 => "Gitar", 8 => "Davul", 11 => "Arcade pad", _ => "Gamepad" },
        _ => "Bilinmiyor",
    };

    static string BatteryName(byte type, byte level) => type switch
    {
        0 => "—",
        1 => "Kablolu",
        _ => level switch { 0 => "Boş", 1 => "Düşük", 2 => "Orta", 3 => "Dolu", _ => "?" },
    };

    /// <summary>Dört yuvanın anlık özeti (kol türü, kablosuz mu, pil, ölü bölge).</summary>
    public PadInfo[] Describe()
    {
        var list = new PadInfo[4];
        for (var i = 0; i < 4; i++)
        {
            if (!_connected[i]) { list[i] = new PadInfo(i, false, "", false, "", Deadzone[i]); continue; }
            var kind = "Gamepad"; var wireless = false; var bat = "—";
            try
            {
                if (GetCaps((uint)i, 0, out var c) == 0) { kind = KindName(c.Type, c.SubType); wireless = (c.Flags & 0x2) != 0; }
                if (GetBattery((uint)i, 0, out var b) == 0) bat = BatteryName(b.Type, b.Level);
            }
            catch { }
            list[i] = new PadInfo(i, true, kind, wireless, bat, Deadzone[i]);
        }
        // XInput dışı (HID) kollar: yuva numarası 4 ve sonrası; titreşim ve kalibrasyon desteklenmez
        var extra = _hid.Describe().Select((d, i) => new PadInfo(4 + i, true, HidPadMapper.Name(d.Kind) == d.Name ? d.Name : $"{HidPadMapper.Name(d.Kind)} ({d.Name})", false, "—", DefaultDeadzone));
        return list.Concat(extra).ToArray();
    }

    /// <summary>Bağlı tüm kollarda çok kısa bir dokunma hissi (arayüz geri bildirimi).</summary>
    public async void Pulse(ushort strength = 24000, int ms = 28)
    {
        var slots = Enumerable.Range(0, 4).Where(i => _connected[i]).ToArray();
        if (slots.Length == 0) return;
        try
        {
            foreach (var i in slots) { var v = new XVibration { Left = 0, Right = strength }; SetVibration((uint)i, ref v); }
            await Task.Delay(ms);
        }
        catch { }
        finally { foreach (var i in slots) { var off = new XVibration(); try { SetVibration((uint)i, ref off); } catch { } } }
    }

    /// <summary>Kolu kısa süre titreştirir (test).</summary>
    public async Task RumbleAsync(int slot, ushort strength = 40000, int ms = 450)
    {
        if (slot < 0 || slot > 3 || !_connected[slot]) return;
        try
        {
            var v = new XVibration { Left = strength, Right = strength };
            SetVibration((uint)slot, ref v);
            await Task.Delay(ms);
        }
        finally
        {
            var off = new XVibration();
            try { SetVibration((uint)slot, ref off); } catch { }
        }
    }

    /// <summary>
    /// Çubuk kaymasını (drift) ölçer: kol boştayken çubukların en büyük sapmasına göre ölü bölgeyi ayarlar.
    /// Çağrılırken çubuklara dokunulmamalı. Yeni ölü bölgeyi döndürür.
    /// </summary>
    public async Task<double> CalibrateAsync(int slot, int ms = 1500)
    {
        if (slot < 0 || slot > 3) return DefaultDeadzone;          // HID kollarda kalibrasyon yok
        double noise = 0;
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            if (TryRead(slot, out var raw))
            {
                noise = Math.Max(noise, Math.Sqrt(Math.Pow(raw.LX / 32768.0, 2) + Math.Pow(raw.LY / 32768.0, 2)));
                noise = Math.Max(noise, Math.Sqrt(Math.Pow(raw.RX / 32768.0, 2) + Math.Pow(raw.RY / 32768.0, 2)));
            }
            await Task.Delay(10);
        }
        var dz = Math.Clamp(noise * 1.6 + 0.04, 0.12, 0.45);
        Deadzone[slot] = dz;
        return dz;
    }

    /// <summary>Test kapısı: sanal tuşları (ör. LB, Guide) gerçek yoklama hattından geçirir.</summary>
    public void InjectVirtual(Btn buttons) => _virtualButtons = buttons;
    public void InjectSticks(short lx, short ly, short rx, short ry) { _vlx = lx; _vly = ly; _vrx = rx; _vry = ry; }
}
