using Microsoft.Win32;

namespace KlycKonsol.Library;

/// <summary>Konsol'un orta tuş (Guide) davranışıyla ilgili Windows kullanıcı ayarı.</summary>
public static class WindowsSettings
{
    const string GameBarKey = @"Software\Microsoft\GameBar";
    const string NexusValue = "GamepadNexusChordEnabled";

    /// <summary>Xbox/Guide tuşuna basınca Windows Game Bar açılıyor mu? (Değer yoksa Windows varsayılanı: açık.)</summary>
    public static bool GameBarOpensOnGuide()
    {
        using var k = Registry.CurrentUser.OpenSubKey(GameBarKey);
        return k?.GetValue(NexusValue) is not int v || v != 0;
    }

    public static bool SetGameBarOpensOnGuide(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(GameBarKey);
            k.SetValue(NexusValue, on ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }
}
