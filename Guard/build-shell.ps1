# KLYC Guard'i derler (MSVC gerekir: Visual Studio / Build Tools "C++ ile masaustu gelistirme").
# Cikti: Guard\KlycShell.exe (depoya da eklidir; yalnizca kaynagi degistirince yeniden derle)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'vswhere bulunamadi: Visual Studio / Build Tools kurulu degil' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'MSVC (C++ araclari) bulunamadi' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
$obj = Join-Path $env:TEMP 'klycshell-obj'
New-Item -ItemType Directory -Force $obj | Out-Null
$bat = Join-Path $env:TEMP 'klycshell-build.bat'
@(
  '@echo off',
  "call `"$vcvars`" >nul",
  "cl /nologo /O1 /MT /GS- /utf-8 /W3 /Fo$obj\ /Fe`"$here\KlycShell.exe`" `"$here\KlycShell.c`" /link /SUBSYSTEM:WINDOWS /OPT:REF /OPT:ICF user32.lib shell32.lib kernel32.lib ole32.lib"
) | Set-Content $bat -Encoding ASCII
& cmd.exe /c $bat
if ($LASTEXITCODE -ne 0) { throw 'derleme basarisiz' }
Get-Item (Join-Path $here 'KlycShell.exe') | ForEach-Object { "{0}  {1} KB" -f $_.Name, [math]::Round($_.Length / 1KB, 1) }
