namespace KlycKonsol.Input;

/// <summary>XInput dışı (HID) kol türleri.</summary>
public enum HidPadKind { Generic, DualShock4, DualSense, SwitchPro }

/// <summary>
/// XInput'a uymayan kollar (PlayStation DualShock 4 / DualSense, Nintendo Switch Pro) Windows'ta genel HID oyun kolu olarak görünür.
/// Bu sınıf, bu kolların ham düğme/çubuk dizisini Konsol'un Xbox biçimli <see cref="PadState"/>'ine çevirir (konuma göre: alt düğme = A).
/// Saf mantık: Windows API'sine dokunmaz, sahte dizilerle sınanır.
/// <para>
/// Düzenler Windows'un HID betimleyicisinden gelen sıradır (1. düğme dizin 0): DS4/DualSense: Kare, Çarpı, Daire, Üçgen, L1, R1, L2, R2, Share/Create, Options, L3, R3, PS, Dokunmatik;
/// Switch Pro: B, A, Y, X, L, R, ZL, ZR, Eksi, Artı, L3, R3, Home, Capture. Eksenler: X, Y (sol çubuk), Z, Rz (sağ çubuk), Rx, Ry (L2, R2 analog; yoksa düğmeden türetilir).
/// Bu eşleme belgelenmiş betimleyicilerden yazıldı; her kol modeliyle gerçek donanımda doğrulanmadı (genel kol için tahmindir).
/// </para>
/// </summary>
public static class HidPadMapper
{
    public const ushort SonyVendor = 0x054C, NintendoVendor = 0x057E;

    /// <summary>Üretici/ürün kimliğinden kol türü.</summary>
    public static HidPadKind Detect(ushort vendor, ushort product) => (vendor, product) switch
    {
        (SonyVendor, 0x05C4 or 0x09CC or 0x0BA0) => HidPadKind.DualShock4,          // DS4 v1, v2, USB dongle
        (SonyVendor, 0x0CE6 or 0x0DF2) => HidPadKind.DualSense,                     // DualSense, DualSense Edge
        (NintendoVendor, 0x2009 or 0x2006 or 0x2007) => HidPadKind.SwitchPro,       // Pro Controller (Joy-Con L/R tek başına: sınırlı)
        _ => HidPadKind.Generic,
    };

    public static string Name(HidPadKind k) => k switch
    {
        HidPadKind.DualShock4 => "DualShock 4", HidPadKind.DualSense => "DualSense", HidPadKind.SwitchPro => "Switch Pro", _ => "Genel kol",
    };

    // GameControllerSwitchPosition değerleri (Windows.Gaming.Input): Center=0, Up=1, UpRight=2, Right=3, DownRight=4, Down=5, DownLeft=6, Left=7, UpLeft=8
    public static Btn Dpad(int switchPosition) => switchPosition switch
    {
        1 => Btn.Up, 2 => Btn.Up | Btn.Right, 3 => Btn.Right, 4 => Btn.Down | Btn.Right, 5 => Btn.Down, 6 => Btn.Down | Btn.Left, 7 => Btn.Left, 8 => Btn.Up | Btn.Left, _ => 0,
    };

    static bool B(IReadOnlyList<bool> b, int i) => i >= 0 && i < b.Count && b[i];

    /// <summary>0..1 ekseni (0,5 = orta) → -32767..32767.</summary>
    public static short Stick(double v, bool invert = false)
    {
        var x = Math.Clamp(v * 2 - 1, -1, 1);
        if (invert) x = -x;
        return (short)Math.Round(x * 32767);
    }

    public static byte Trigger(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    /// <summary>Ham HID okumasını Konsol'un durumuna çevirir. Eksik dizi/öğe yok sayılır (hata vermez).</summary>
    public static PadState Map(HidPadKind kind, IReadOnlyList<bool> buttons, int dpadSwitch, IReadOnlyList<double> axes)
    {
        var s = new PadState();
        double Ax(int i, double dflt = 0.5) => i >= 0 && i < axes.Count ? axes[i] : dflt;
        s.Buttons = Dpad(dpadSwitch);

        if (kind is HidPadKind.DualShock4 or HidPadKind.DualSense)
        {
            if (B(buttons, 1)) s.Buttons |= Btn.A;      // Çarpı
            if (B(buttons, 2)) s.Buttons |= Btn.B;      // Daire
            if (B(buttons, 0)) s.Buttons |= Btn.X;      // Kare
            if (B(buttons, 3)) s.Buttons |= Btn.Y;      // Üçgen
            if (B(buttons, 4)) s.Buttons |= Btn.LB;
            if (B(buttons, 5)) s.Buttons |= Btn.RB;
            if (B(buttons, 8)) s.Buttons |= Btn.Back;   // Share / Create
            if (B(buttons, 9)) s.Buttons |= Btn.Start;  // Options
            if (B(buttons, 10)) s.Buttons |= Btn.LS;
            if (B(buttons, 11)) s.Buttons |= Btn.RS;
            if (B(buttons, 12)) s.Buttons |= Btn.Guide; // PS tuşu
            s.LX = Stick(Ax(0)); s.LY = Stick(Ax(1), invert: true);
            s.RX = Stick(Ax(2)); s.RY = Stick(Ax(3), invert: true);
            s.LT = axes.Count > 4 ? Trigger(Ax(4, 0)) : (byte)(B(buttons, 6) ? 255 : 0);
            s.RT = axes.Count > 5 ? Trigger(Ax(5, 0)) : (byte)(B(buttons, 7) ? 255 : 0);
            if (B(buttons, 6) && s.LT == 0) s.LT = 255;
            if (B(buttons, 7) && s.RT == 0) s.RT = 255;
        }
        else if (kind == HidPadKind.SwitchPro)
        {
            if (B(buttons, 0)) s.Buttons |= Btn.A;      // B (alt konum)
            if (B(buttons, 1)) s.Buttons |= Btn.B;      // A (sağ konum)
            if (B(buttons, 2)) s.Buttons |= Btn.X;      // Y (sol konum)
            if (B(buttons, 3)) s.Buttons |= Btn.Y;      // X (üst konum)
            if (B(buttons, 4)) s.Buttons |= Btn.LB;
            if (B(buttons, 5)) s.Buttons |= Btn.RB;
            if (B(buttons, 8)) s.Buttons |= Btn.Back;   // Eksi
            if (B(buttons, 9)) s.Buttons |= Btn.Start;  // Artı
            if (B(buttons, 10)) s.Buttons |= Btn.LS;
            if (B(buttons, 11)) s.Buttons |= Btn.RS;
            if (B(buttons, 12)) s.Buttons |= Btn.Guide; // Home
            s.LX = Stick(Ax(0)); s.LY = Stick(Ax(1), invert: true);
            s.RX = Stick(Ax(2)); s.RY = Stick(Ax(3), invert: true);
            s.LT = (byte)(B(buttons, 6) ? 255 : 0); s.RT = (byte)(B(buttons, 7) ? 255 : 0);   // ZL / ZR dijital
        }
        else
        {
            // Genel HID kol: yaygın DirectInput düzeni (tahmin). 1=A 2=B 3=X 4=Y 5=LB 6=RB 7=Back 8=Start 9=L3 10=R3
            if (B(buttons, 0)) s.Buttons |= Btn.A;
            if (B(buttons, 1)) s.Buttons |= Btn.B;
            if (B(buttons, 2)) s.Buttons |= Btn.X;
            if (B(buttons, 3)) s.Buttons |= Btn.Y;
            if (B(buttons, 4)) s.Buttons |= Btn.LB;
            if (B(buttons, 5)) s.Buttons |= Btn.RB;
            if (B(buttons, 6)) s.Buttons |= Btn.Back;
            if (B(buttons, 7)) s.Buttons |= Btn.Start;
            if (B(buttons, 8)) s.Buttons |= Btn.LS;
            if (B(buttons, 9)) s.Buttons |= Btn.RS;
            s.LX = Stick(Ax(0)); s.LY = Stick(Ax(1), invert: true);
            s.RX = Stick(Ax(2)); s.RY = Stick(Ax(3), invert: true);
        }
        return s;
    }
}
