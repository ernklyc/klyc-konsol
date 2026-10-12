using System.Management;

namespace KlycKonsol.Input;

/// <summary>Dahili ekran (dizüstü) parlaklığı — WMI. Harici monitör / masaüstünde desteklenmez (Level = null).</summary>
public static class Brightness
{
    public static bool Supported { get; private set; } = true;

    public static int? Level
    {
        get
        {
            if (!Supported) return null;
            try
            {
                using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
                Supported = false;
            }
            catch { Supported = false; }
            return null;
        }
    }

    public static void Set(int percent)
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject o in s.Get()) o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Clamp(percent, 0, 100) });
        }
        catch { }
    }
}
