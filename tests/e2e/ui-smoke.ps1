#requires -Version 5.1
<#
  KLYC Konsol — otomatik arayüz duman testi (CI'da ve elle çalışır).
  Gerçek KlycKonsol.exe'yi YALITILMIŞ veri klasörüyle açar (KLYC_DATA_DIR, sahte oyun, sanal kol), Windows UI Automation ile ekranı okur:
  kütüphane, sekmeler, Ayarlar satırları, paneller ve (ikinci turda) İngilizce arayüzde Türkçe metin kalmadığını denetler.
  Kullanıcının gerçek verisine, kolu ya da ayarlarına dokunmaz. Başarısızlıkta çıkış kodu 1.

  Kullanım:  powershell -ExecutionPolicy Bypass -File tests\e2e\ui-smoke.ps1 [-Exe <KlycKonsol.exe>] [-ShotDir <klasör>]
#>
param(
    [string]$Exe = '',
    [string]$ShotDir = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class SmokeDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[SmokeDpi]::SetProcessDPIAware() | Out-Null

if (-not $Exe) {
    $Exe = Get-ChildItem (Join-Path $PSScriptRoot '..\..\bin\Release') -Recurse -Filter 'KlycKonsol.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not (Test-Path $Exe)) { Write-Error "KlycKonsol.exe bulunamadı: $Exe"; exit 2 }

$ok = 0; $fail = 0
function Check($name, $cond, $detail = '') {
    if ($cond) { $script:ok++; "  [GEÇTİ] $name" } else { $script:fail++; "  [KALDI] $name  $detail" }
}

function New-Sandbox($lang) {
    $root = Join-Path ([IO.Path]::GetTempPath()) ('konsol-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    $data = Join-Path $root 'data'; $game = Join-Path $root 'FakeGame'
    New-Item -ItemType Directory -Force $data, $game | Out-Null
    Copy-Item "$env:WINDIR\System32\PING.EXE" (Join-Path $game 'FakeGame.exe')
    $lib = @{ Entries = @(@{ Title = 'FakeGame'; Target = (Join-Path $game 'FakeGame.exe'); Args = '-t 127.0.0.1'; Kind = 'game'; Controller = $true }) } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $data 'library.json'), $lib)
    [IO.File]::WriteAllText((Join-Path $data 'state.json'), '{"LastPlayed":{"user:FakeGame":9999999999},"SupportOverride":{"user:FakeGame":"full"},"ControllerOnly":true,"CheckUpdates":false,"GuidePromptShown":true,"UseGuard":false,"Language":"' + $lang + '"}')
    [pscustomobject]@{ Root = $root; Data = $data }
}

function Get-Names($proc) {
    for ($try = 0; $try -lt 5; $try++) {
        try {
            $r = [System.Windows.Automation.AutomationElement]::RootElement
            $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $proc.Id
            $n = @()
            foreach ($w in $r.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
                $n += foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { try { $e.Current.Name } catch { } }
            }
            return @($n | Where-Object { $_ })
        } catch { Start-Sleep -Milliseconds 600 }
    }
    @()
}

function Send-Cmd($sb, $text) { [IO.File]::WriteAllText((Join-Path $sb.Data 'cmd.txt'), $text); Start-Sleep -Milliseconds 2600 }

function Save-Shot($name) {
    if (-not $ShotDir) { return }
    New-Item -ItemType Directory -Force $ShotDir | Out-Null
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size); $g.Dispose()
    $bmp.Save((Join-Path $ShotDir "$name.png")); $bmp.Dispose()
}

function Run-Pass($lang, $expect) {
    "== Tur: dil=$lang"
    $sb = New-Sandbox $lang
    $env:KLYC_DATA_DIR = $sb.Data
    $env:KLYC_PULSE_DIR = Join-Path $sb.Root 'pulse'
    $env:KLYC_DOWNLOADS_DIR = Join-Path $sb.Root 'dl'
    $proc = $null
    try {
        $proc = Start-Process $Exe -ArgumentList '--debugcmd', '--virtualonly', '--noextras' -PassThru
        # pencere ve ilk kütüphane
        $names = @(); $t0 = Get-Date
        while (((Get-Date) - $t0).TotalSeconds -lt 45) { Start-Sleep -Seconds 1; $names = Get-Names $proc; if (@($names | Where-Object { $_ -match '^[0-9]+ (oyun|games)' }).Count -ge 1) { break } }
        Check "Konsol açıldı ve süreç yaşıyor" (-not $proc.HasExited)
        Check "Kütüphane yüklendi: durum satırında oyun sayısı var (en az sahte oyun)" (@($names | Where-Object { $_ -match '^[0-9]+ (oyun|games)' }).Count -ge 1) (($names | Select-Object -First 15) -join '|')
        Check "Sekme adları ($($expect.Library) / $($expect.Browser) / $($expect.Settings))" (($names -contains $expect.Library) -and ($names -contains $expect.Browser) -and ($names -contains $expect.Settings)) (($names | Select-Object -First 20) -join '|')
        Check "Süzgeç çipleri görünüyor ($($expect.Recent))" ($names -contains $expect.Recent)
        Save-Shot "smoke_${lang}_library"

        # Ayarlar
        Send-Cmd $sb 'tab:2'
        $names = Get-Names $proc
        Check "Ayarlar açıldı: başlık ve temel satırlar" (($names -contains $expect.Settings) -and ($names -contains $expect.RowDisplay) -and ($names -contains $expect.RowLanguage)) (($names | Select-Object -First 25) -join '|')
        Check "Ayarlar'da Pulse bölümü var" (($names -contains 'PULSE') -and ($names -contains $expect.RowPulseMode))
        Save-Shot "smoke_${lang}_settings"

        # Ekran ve ses paneli: 10 aşağı → A (yalnızca paneli açar, hiçbir şeyi değiştirmez); sonra B ile kapat
        Send-Cmd $sb ((1..10 | ForEach-Object { 'Down' }) -join ' ')
        Start-Sleep -Seconds 4
        Send-Cmd $sb 'A'
        $names = Get-Names $proc
        Check "Ekran ve ses paneli açıldı" (($names -contains $expect.PanelDisplay) -and ($names -contains $expect.PanelSound)) (($names | Where-Object { $_ -match 'Ekran|Display|SES|SOUND' }) -join '|')
        Check "Panelde geçerli ekran kipi (çözünürlük × yenileme) listeleniyor" (@($names | Where-Object { $_ -match '^\d{3,5}×\d{3,5} · \d+ Hz$' }).Count -ge 1)
        Save-Shot "smoke_${lang}_display_panel"
        Send-Cmd $sb 'B'

        # Tarayıcı sekmesi
        Send-Cmd $sb 'tab:1'
        Start-Sleep -Seconds 3
        $names = Get-Names $proc
        Check "Tarayıcı sekmesi açıldı (araç çubuğu/ipuçları)" (@($names | Where-Object { $_ -match 'KLYC|Browser|Tarayıcı' }).Count -ge 1)

        # Dil taraması: Türkçe seçiliyken İngilizce, İngilizce seçiliyken Türkçe harf kalmamalı (ağaçtaki tüm metinler)
        Send-Cmd $sb 'tab:2'
        $names = Get-Names $proc
        if ($lang -eq 'en') {
            $tr = [regex]'[çğıöşüÇĞİÖŞÜ]'
            $bad = @($names | Where-Object { $tr.IsMatch($_) -and $_ -notmatch 'Web içeriği|Türkçe|Dil / Language' })
            Check "İngilizce arayüzde Türkçe harfli metin kalmadı" ($bad.Count -eq 0) (($bad | Select-Object -First 8) -join ' | ')
        }

        # Çökme günlüğü olmamalı
        $crash = Join-Path $sb.Data 'crash.log'
        Check "crash.log oluşmadı" (-not (Test-Path $crash)) $(if (Test-Path $crash) { (Get-Content $crash -TotalCount 12) -join "`n" } else { '' })
        Check "Süreç hâlâ yaşıyor" (-not $proc.HasExited)
    }
    finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 1
        try { [IO.Directory]::Delete($sb.Root, $true) } catch { }
    }
}

# Kullanıcının bekçisi (varsa) test sırasında durmuş olabilir; CI'da yoktur. Burada hiçbir şey durdurulmaz/başlatılmaz.
Run-Pass 'tr' @{ Library = 'Kütüphane'; Browser = 'Tarayıcı'; Settings = 'Ayarlar'; Recent = 'Son oynananlar'; RowDisplay = 'Ekran ve ses'; RowLanguage = 'Dil / Language'; RowPulseMode = 'Pulse modu'; PanelDisplay = 'EKRAN'; PanelSound = 'SES ÇIKIŞI' }
Run-Pass 'en' @{ Library = 'Library'; Browser = 'Browser'; Settings = 'Settings'; Recent = 'Recent'; RowDisplay = 'Display and sound'; RowLanguage = 'Dil / Language'; RowPulseMode = 'Pulse mode'; PanelDisplay = 'DISPLAY'; PanelSound = 'SOUND OUTPUT' }

"SONUÇ: $ok geçti, $fail kaldı"
if ($fail -gt 0) { exit 1 }
