# KLYC Konsol kaldırma. Ayarların / kapakların durduğu %AppData%\KLYC-Konsol klasörü -Hepsi verilmedikçe silinmez.
# Kullanım:  powershell -ExecutionPolicy Bypass -File .\Kaldir.ps1 [-Hepsi]
param([switch]$Hepsi)
$dst = Join-Path $env:LOCALAPPDATA 'Programs\KLYC-Konsol'

Get-Process KlycKonsol, KlycGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'KLYC Konsol.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'KLYC Konsol.lnk') -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'KLYC-Konsol' -ErrorAction SilentlyContinue

# Konsol bir oyun sırasında kapatıldıysa güç planı / bildirim ayarı geri yüklensin: uygulamanın kendisi bunu açılışta yapar,
# bu yüzden kaldırmadan önce bir kez normal kapanmış olması yeterlidir.
if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
if ($Hepsi) { Remove-Item (Join-Path $env:APPDATA 'KLYC-Konsol') -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host 'KLYC Konsol kaldırıldı.'
