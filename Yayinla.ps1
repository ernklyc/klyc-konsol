# KLYC Konsol - dagitim paketlerini uretir:
#   dist\KLYC-Konsol-Setup-v<surum>.exe          Kurulum dosyasi (Velopack; kendi kendine guncelleme icin gerekli)
#   dist\KLYC-Konsol-<surum>-win-x64.zip         Kurulumsuz paket (Kur.ps1 ile ya da elle)
#   dist\velopack\*                              Guncelleme paketleri (releases.win.json, *.nupkg, RELEASES): GitHub surumune YUKLENMELI
# Kullanim:  powershell -ExecutionPolicy Bypass -File .\Yayinla.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csproj = Join-Path $root 'KlycKonsol.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$stage = Join-Path $root 'dist\KLYC-Konsol'
$pub = Join-Path $root 'dist\publish'
$vel = Join-Path $root 'dist\velopack'
$zip = Join-Path $root "dist\KLYC-Konsol-$version-win-x64.zip"
$setup = Join-Path $root "dist\KLYC-Konsol-Setup-v$version.exe"

if (Test-Path (Join-Path $root 'dist')) { Remove-Item (Join-Path $root 'dist') -Recurse -Force }
New-Item -ItemType Directory -Force $stage, $pub, $vel | Out-Null

Write-Host "Derleniyor (v$version)..."
# Cerceveye bagli: hedef bilgisayarda .NET 8 Desktop Runtime gerekir (Setup.exe yoksa kendisi kurar; Kur.ps1 kontrol eder).
dotnet publish $csproj -c Release -r win-x64 --self-contained false -o $pub -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish basarisiz' }

# --- Velopack kurulum + guncelleme paketi
$vpkDir = Join-Path $root '.vpk'
$vpk = Join-Path $vpkDir 'vpk.exe'
if (-not (Test-Path $vpk)) {
    Write-Host 'vpk araci kuruluyor (bir kez)...'
    dotnet tool install vpk --tool-path $vpkDir --version 1.2.161
    if ($LASTEXITCODE -ne 0) { throw 'vpk kurulamadi' }
}
& $vpk pack --packId KLYC-Konsol --packTitle 'KLYC Konsol' --packVersion $version --packDir $pub --mainExe KlycKonsol.exe --icon (Join-Path $root 'Assets\klyc.ico') --framework net8.0-x64-desktop --outputDir $vel
if ($LASTEXITCODE -ne 0) { throw 'vpk pack basarisiz' }
Copy-Item (Join-Path $vel 'KLYC-Konsol-win-Setup.exe') $setup

# --- Kurulumsuz zip (eski yol): Kur.ps1 / Kaldir.ps1 ile
Copy-Item "$pub\*" $stage -Recurse
Copy-Item (Join-Path $root 'Kur.ps1'), (Join-Path $root 'Kaldir.ps1'), (Join-Path $root 'README.md') $stage
Compress-Archive -Path "$stage\*" -DestinationPath $zip -Force

$mb = { param($f) [math]::Round((Get-Item $f).Length / 1MB, 1) }
Write-Host "Hazir:"
Write-Host "  $setup ($(& $mb $setup) MB)"
Write-Host "  $zip ($(& $mb $zip) MB)"
Write-Host "  $vel  (guncelleme paketleri: bu klasordeki *.nupkg, releases.win.json, RELEASES, assets.win.json da surume yuklenmeli)"
