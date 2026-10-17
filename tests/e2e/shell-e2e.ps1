#requires -Version 5.1
<#
  KLYC Shell (Konsol modu gözetmeni) denemesi: SAHTE Konsol ve SAHTE masaüstü ile geri dönüş mantığını gerçek süreçlerle sınar.
  Gerçek explorer.exe'ye, kayıt defterine ya da oturuma dokunmaz.
  Senaryolar: sağlıklı Konsol · Konsol sonlanınca yeniden başlatma · çökme döngüsünde masaüstüne dönüş · Konsol yokken masaüstü · masaüstü açıkken Konsol'u zorlamama.
#>
param([string]$Shell = '')
$ErrorActionPreference = 'Stop'
if (-not $Shell) { $Shell = Join-Path $PSScriptRoot '..\..\Guard\KlycShell.exe' }
$Shell = (Resolve-Path $Shell).Path
if (Get-Process KlycKonsol, KlycGuard, KlycShell -ErrorAction SilentlyContinue) { Write-Error 'Gerçek Konsol/bekçi çalışıyor: önce kapat (test aynı ad işlemleri sonlandırır).'; exit 2 }
$ok = 0; $fail = 0
function Check($name, $cond, $detail = '') { if ($cond) { $script:ok++; "  [GEÇTİ] $name" } else { $script:fail++; "  [KALDI] $name  $detail" } }

$root = Join-Path ([IO.Path]::GetTempPath()) ('shell-e2e-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Force (Join-Path $root 'k'), (Join-Path $root 'd'), (Join-Path $root 'src') | Out-Null
# Sahte uygulama: yanındaki behavior.txt "stay" (sonsuza dek bekle) ya da "crash" (1 sn sonra çık); her başlayışta started.txt'ye satır ekler
$src = @'
using System; using System.IO; using System.Threading;
class P { static void Main() {
  var dir = AppDomain.CurrentDomain.BaseDirectory;
  var me = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);
  File.AppendAllText(Path.Combine(dir, "started.txt"), DateTime.Now.ToString("HH:mm:ss.fff") + "\r\n");
  var b = File.Exists(Path.Combine(dir, "behavior.txt")) ? File.ReadAllText(Path.Combine(dir, "behavior.txt")).Trim() : "stay";
  if (b == "crash") { Thread.Sleep(700); return; }
  Thread.Sleep(Timeout.Infinite);
} }
'@
$cs = Join-Path $root 'src\fake.cs'; Set-Content $cs $src -Encoding UTF8
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
function Make-Fake($exePath) { & $csc /nologo /target:winexe /out:$exePath $cs | Out-Null; if (-not (Test-Path $exePath)) { throw "derlenemedi: $exePath" } }
$kExe = Join-Path $root 'k\KlycKonsol.exe'; $dExe = Join-Path $root 'd\FakeDesk.exe'
Make-Fake $kExe; Make-Fake $dExe
function Starts($dir) { $f = Join-Path $dir 'started.txt'; if (Test-Path $f) { @(Get-Content $f).Count } else { 0 } }
function Reset-Fakes() {
    Get-Process KlycKonsol, FakeDesk, KlycShell -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
    foreach ($d in 'k', 'd') { foreach ($f in 'started.txt', 'behavior.txt') { $p = Join-Path $root "$d\$f"; if (Test-Path $p) { [IO.File]::Delete($p) } } }
}
function Start-Shell($extra) { Start-Process $Shell -ArgumentList (@('--konsol', "`"$kExe`"", '--desktop', "`"$dExe`"", '--poll', '300', '--miss', '2', '--window', '30') + $extra) -PassThru }
try {
    # 1) Sağlıklı Konsol: bir kez başlar, masaüstü açılmaz
    Reset-Fakes; Set-Content (Join-Path $root 'k\behavior.txt') 'stay'
    $sh = Start-Shell @('--max', '3'); Start-Sleep 4
    Check 'sağlıklı: Konsol bir kez başlatıldı' ((Starts (Join-Path $root 'k')) -eq 1) "baslama=$(Starts (Join-Path $root 'k'))"
    Check 'sağlıklı: masaüstü başlatılmadı' ((Starts (Join-Path $root 'd')) -eq 0)

    # 2) Konsol dışarıdan sonlandırılırsa yeniden başlatılır (bekçi devri kısa boşluğuna 2 yoklama tolerans)
    Get-Process KlycKonsol | Stop-Process -Force; Start-Sleep 3
    Check 'Konsol sonlanınca yeniden başlatıldı' ((Starts (Join-Path $root 'k')) -eq 2) "baslama=$(Starts (Join-Path $root 'k'))"
    Check 'yeniden başlatmada masaüstü hâlâ açılmadı' ((Starts (Join-Path $root 'd')) -eq 0)
    Check 'gözetmen sürekli yaşıyor' (-not $sh.HasExited)

    # 3) Çökme döngüsü: en çok 3 başlatma, sonra masaüstü; gözetmen çıkmaz
    Reset-Fakes; Set-Content (Join-Path $root 'k\behavior.txt') 'crash'
    $sh = Start-Shell @('--max', '3'); Start-Sleep 14
    $kc = Starts (Join-Path $root 'k'); $dc = Starts (Join-Path $root 'd')
    Check 'çökme döngüsü: Konsol en çok 3 kez denendi' ($kc -eq 3) "baslama=$kc"
    Check 'çökme döngüsü sonunda masaüstü başlatıldı' ($dc -ge 1) "masaustu=$dc"
    Check 'masaüstü ayakta' ($null -ne (Get-Process FakeDesk -ErrorAction SilentlyContinue))
    Check 'gözetmen çıkmadı (çıkarsa Windows oturumu kapatırdı)' (-not $sh.HasExited)
    $kc2 = Starts (Join-Path $root 'k'); Start-Sleep 3
    Check 'masaüstü açıkken Konsol zorla başlatılmıyor' ((Starts (Join-Path $root 'k')) -eq $kc2)

    # 4) Konsol dosyası yoksa doğrudan masaüstü
    Reset-Fakes
    $sh = Start-Process $Shell -ArgumentList @('--konsol', "`"$(Join-Path $root 'yok\KlycKonsol.exe')`"", '--desktop', "`"$dExe`"", '--poll', '300') -PassThru; Start-Sleep 3
    Check 'Konsol dosyası yok: masaüstü hemen açıldı' ((Starts (Join-Path $root 'd')) -eq 1 -and (Starts (Join-Path $root 'k')) -eq 0)
    Check 'Konsol dosyası yok: gözetmen yaşıyor' (-not $sh.HasExited)

    # 5) İkinci kopya hemen çıkar (tek örnek)
    $second = Start-Process $Shell -ArgumentList @('--konsol', "`"$kExe`"") -PassThru; Start-Sleep 2
    Check 'ikinci gözetmen kopyası kendiliğinden çıktı' ($second.HasExited)
}
finally {
    Get-Process KlycKonsol, FakeDesk, KlycShell -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 1; try { [IO.Directory]::Delete($root, $true) } catch { }
}
"SONUÇ: $ok geçti, $fail kaldı"
if ($fail -gt 0) { exit 1 }
