# KLYC Konsol kurulumu (yönetici izni gerekmez): %LocalAppData%\Programs\KLYC-Konsol
# Kullanım:  powershell -ExecutionPolicy Bypass -File .\Kur.ps1 [-Otomatik] [-Masaustu]
#   -Otomatik : Windows açılınca Konsol da açılsın
#   -Masaustu : Masaüstüne kısayol koy
param([switch]$Otomatik, [switch]$Masaustu)
$ErrorActionPreference = 'Stop'
$src = $PSScriptRoot
$dst = Join-Path $env:LOCALAPPDATA 'Programs\KLYC-Konsol'

# .NET 8 Desktop Runtime var mı?
$rt = & dotnet --list-runtimes 2>$null | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 8\.' }
if (-not $rt) {
    Write-Warning '.NET 8 Desktop Runtime bulunamadı. Şunu çalıştırıp yeniden dene:  winget install Microsoft.DotNet.DesktopRuntime.8'
    exit 1
}

# Çalışıyorsa kapat (güncelleme senaryosu)
Get-Process KlycKonsol, KlycGuard -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800

New-Item -ItemType Directory -Force $dst | Out-Null
Get-ChildItem $src -Exclude 'Kur.ps1' | Copy-Item -Destination $dst -Recurse -Force
Copy-Item (Join-Path $src 'Kur.ps1') $dst -Force   # kaldırma/güncelleme için yanında dursun

$exe = Join-Path $dst 'KlycKonsol.exe'
$ws = New-Object -ComObject WScript.Shell
function New-Link($path) {
    $l = $ws.CreateShortcut($path); $l.TargetPath = $exe; $l.WorkingDirectory = $dst; $l.IconLocation = $exe; $l.Description = 'KLYC Konsol'; $l.Save()
}
New-Link (Join-Path ([Environment]::GetFolderPath('Programs')) 'KLYC Konsol.lnk')
if ($Masaustu) { New-Link (Join-Path ([Environment]::GetFolderPath('Desktop')) 'KLYC Konsol.lnk') }
if ($Otomatik) {
    Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'KLYC-Konsol' -Value "`"$exe`" --startup"
}

Write-Host "Kuruldu: $dst"
Start-Process $exe
