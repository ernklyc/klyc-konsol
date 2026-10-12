param([string]$Exe = '', [string]$ShotDir = '', [switch]$NoApply)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class Disp {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public uint dmFields; public int dmPositionX, dmPositionY; public uint dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName; public ushort dmLogPixels;
    public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string d, int m, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsExW(string d, ref DEVMODE dm, IntPtr h, uint f, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public static string Current() { var dm = new DEVMODE(); dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettingsW(null, -1, ref dm); return dm.dmPelsWidth + "x" + dm.dmPelsHeight + "@" + dm.dmDisplayFrequency; }
  public static int Set(int w, int h, int hz) { var dm = new DEVMODE(); dm.dmSize = (ushort)Marshal.SizeOf(typeof(DEVMODE)); EnumDisplaySettingsW(null, -1, ref dm); dm.dmPelsWidth = (uint)w; dm.dmPelsHeight = (uint)h; dm.dmDisplayFrequency = (uint)hz; dm.dmFields = 0x80000 | 0x100000 | 0x400000; return ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, 1, IntPtr.Zero); }
}
"@
[Disp]::SetProcessDPIAware() | Out-Null
if (-not $Exe) { $Exe = (Get-ChildItem (Join-Path $PSScriptRoot '..\..\bin\Release') -Recurse -Filter 'KlycKonsol.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
$konsolExe = $Exe
$root = Join-Path $env:TEMP ('konsol-disp-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$data = Join-Path $root 'data'; New-Item -ItemType Directory -Force $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'library.json'), '{"Entries":[]}')
[IO.File]::WriteAllText((Join-Path $data 'state.json'), '{"ControllerOnly":true,"CheckUpdates":false,"GuidePromptShown":true,"UseGuard":false,"Language":"tr"}')
$env:KLYC_DATA_DIR = $data; $env:KLYC_PULSE_DIR = (Join-Path $root 'pulse')
$guardCmd = Get-CimInstance Win32_Process -Filter "Name='KlycGuard.exe'" | Select-Object -First 1 -ExpandProperty CommandLine
$orig = [Disp]::Current()
$m = [regex]::Match($orig, '(\d+)x(\d+)@(\d+)'); $ow = [int]$m.Groups[1].Value; $oh = [int]$m.Groups[2].Value; $ohz = [int]$m.Groups[3].Value
$ok = 0; $fail = 0
function Check($name, $cond, $detail = '') { if ($cond) { $script:ok++; "  [GECTI] $name" } else { $script:fail++; "  [KALDI] $name  $detail" } }
function Cmd($c) { [IO.File]::WriteAllText((Join-Path $data 'cmd.txt'), $c); Start-Sleep 2.4 }
function Names($proc) { for ($try = 0; $try -lt 5; $try++) { try { return (Names1 $proc) } catch { Start-Sleep -Milliseconds 700 } }; @() }
function Names1($proc) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $proc.Id
    $n = @(); foreach ($w in $r.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) { $n += foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { try { $e.Current.Name } catch { } } }
    $n
}
function MainRect($proc) {
    $r = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $proc.Id
    foreach ($w in $r.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) { $b = $w.Current.BoundingRectangle; if ($b.Width -gt 500) { return $b } }
    $null
}
function Shot($name) { if ($ShotDir) { $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height; $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size); $g.Dispose(); $bmp.Save((Join-Path $ShotDir "$name.png")); $bmp.Dispose() } }
$k = $null
"Başlangıç ekran kipi: $orig"
try {
    $k = Start-Process $konsolExe -ArgumentList '--debugcmd', '--virtualonly', '--noextras' -PassThru
    Start-Sleep 9
    $rect = MainRect $k
    Check 'Konsol tam ekran açıldı (ekran boyutunda)' ($rect -and [math]::Abs($rect.Width - $ow) -lt 20 -and [math]::Abs($rect.Height - $oh) -lt 20) "rect=$($rect.Width)x$($rect.Height) ekran=${ow}x$oh"
    Cmd 'tab:2'
    Cmd ((1..10 | ForEach-Object { 'Down' }) -join ' ')
    Start-Sleep 5
    Cmd 'A'
    Start-Sleep 1
    $names = Names $k
    Check 'Ekran ve ses paneli açıldı' (($names -contains 'EKRAN') -and ($names -contains 'SES ÇIKIŞI')) (($names | Where-Object { $_ -like '*EKRAN*' -or $_ -like '*SES*' }) -join '|')
    Check 'şu anki ekran kipi listede' (@($names | Where-Object { $_ -like "*${ow}×${oh}*" }).Count -ge 1) (($names | Where-Object { $_ -like '*×*' }) -join '|')
    $nDev = @($names | Where-Object { $_ -like '✓ Kullanımda' -or $_ -like 'A: bunu kullan' }).Count
    "  (ses aygıtı sayısı: $nDev)"
    if ($NoApply) { Get-Content (Join-Path $data "konsol.log") | Select-String "ses" | ForEach-Object { "LOG: " + $_.Line }; return }
    # Çözünürlük satırına in
    $downs = [math]::Max(0, $nDev - 1) + 2   # son aygıt + Şu an + Çözünürlük
    Cmd ((1..$downs | ForEach-Object { 'Down' }) -join ' ')
    Start-Sleep 4
    Cmd 'Right'
    $names = Names $k
    $applyRow = $names | Where-Object { $_ -like '*→*' } | Select-Object -First 1
    Check 'yeni çözünürlük seçildi (Uygula satırı eski → yeni gösteriyor)' ($null -ne $applyRow) (($names | Where-Object { $_ -like '*×*' }) -join '|')
    "  (uygula satırı: $applyRow)"
    Shot 'disp_1_secim'
    if ($applyRow) {
        Cmd 'Down Down'; Start-Sleep 3
        Cmd 'A'          # Uygula
        Start-Sleep 4
        $now = [Disp]::Current()
        Check 'ekran kipi gerçekten değişti' ($now -ne $orig) "şimdi=$now"
        $mm = [regex]::Match($now, '(\d+)x(\d+)@')
        $nw = [int]$mm.Groups[1].Value; $nh = [int]$mm.Groups[2].Value
        $names = Names $k
        Check 'onay ekranı göründü (Bu ayarı koru / Geri al)' (($names -contains 'Bu ayarı koru') -and ($names -contains 'Geri al')) (($names | Where-Object { $_ -like '*koru*' -or $_ -like '*Geri*' }) -join '|')
        Start-Sleep 2
        $rect = MainRect $k
        Check 'Konsol penceresi yeni ekran boyutuna uydu' ($rect -and [math]::Abs($rect.Width - $nw) -lt 20 -and [math]::Abs($rect.Height - $nh) -lt 20) "rect=$($rect.Width)x$($rect.Height) yeni=${nw}x$nh"
        Shot 'disp_2_yeni_kip'
        "  (onay beklenirken hiçbir şeye basılmıyor: 15 sn sonra eski kipe dönmeli)"
        $t0 = Get-Date; $back = $false
        while (((Get-Date) - $t0).TotalSeconds -lt 30) { Start-Sleep -Milliseconds 700; if ([Disp]::Current() -eq $orig) { $back = $true; break } }
        $secs = [math]::Round(((Get-Date) - $t0).TotalSeconds, 1)
        Check 'süre dolunca ekran kendiliğinden eski kipe döndü' $back "bekleme=$secs sn; şimdi=$([Disp]::Current())"
        "  (geri dönüş süresi: ~$($secs + 4) sn, hedef 15 sn)"
        Start-Sleep 3
        $rect = MainRect $k
        Check 'geri dönünce Konsol yine ekran boyutunda' ($rect -and [math]::Abs($rect.Width - $ow) -lt 20 -and [math]::Abs($rect.Height - $oh) -lt 20) "rect=$($rect.Width)x$($rect.Height) ekran=${ow}x$oh"
        Shot 'disp_3_geri'
        $log = Join-Path $data 'konsol.log'
        if (Test-Path $log) { Get-Content $log | Select-String 'ekran|\[ses\]' | ForEach-Object { "  log: $($_.Line)" } }
    }
}
finally {
    if ($k -and -not $k.HasExited) { Stop-Process -Id $k.Id -Force -ErrorAction SilentlyContinue }
    $cur = [Disp]::Current()
    if ($cur -ne $orig) { "GÜVENLİK: ekran kipi eski haline getiriliyor ($cur → $orig)"; [void][Disp]::Set($ow, $oh, $ohz); Start-Sleep 2 }
    "Son ekran kipi: $([Disp]::Current()) (başlangıç: $orig)"
    if ($guardCmd -and -not (Get-Process KlycGuard -ErrorAction SilentlyContinue) -and $guardCmd -match '^"([^"]+)"\s*(.*)$') { Start-Process $Matches[1] -ArgumentList $Matches[2] }
    Start-Sleep 1; try { [IO.Directory]::Delete($root, $true) } catch { }
}
"SONUC: $ok gecti, $fail kaldi"
