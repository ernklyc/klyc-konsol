param([string]$Exe = '', [int]$Port = 9333, [int]$Http = 8765)
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Net.Http
if (-not $Exe) { $Exe = (Get-ChildItem (Join-Path $PSScriptRoot '..\..\bin\Release') -Recurse -Filter 'KlycKonsol.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
$exe = $Exe
$root = Join-Path $env:TEMP ('konsol-br-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
$data = Join-Path $root 'data'; $pulse = Join-Path $root 'pulse'; $dl = Join-Path $root 'dl'
New-Item -ItemType Directory -Force $data, $pulse, $dl | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'library.json'), '{"Entries":[]}')
[IO.File]::WriteAllText((Join-Path $data 'state.json'), '{"ControllerOnly":true,"CheckUpdates":false,"GuidePromptShown":true,"UseGuard":false,"Language":"tr"}')
$env:KLYC_DATA_DIR = $data; $env:KLYC_PULSE_DIR = $pulse; $env:KLYC_DOWNLOADS_DIR = $dl
$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$Port"
$ok = 0; $fail = 0
function Check($name, $cond, $detail = '') { if ($cond) { $script:ok++; "  [GECTI] $name" } else { $script:fail++; "  [KALDI] $name  $detail" } }

# Yerel sunucu: /file.bin (indirme, 3 MB), /ua (kimlik yansıt)
$srv = Start-Job -ArgumentList $Http -ScriptBlock {
    param($p)
    $l = New-Object Net.HttpListener; $l.Prefixes.Add("http://127.0.0.1:$p/"); $l.Start()
    $bytes = New-Object byte[] (3MB); (New-Object Random 7).NextBytes($bytes)
    while ($true) {
        $c = $l.GetContext(); $r = $c.Response
        if ($c.Request.Url.AbsolutePath -eq '/file.bin') {
            $r.ContentType = 'application/octet-stream'; $r.AddHeader('Content-Disposition', 'attachment; filename="rapor test.bin"'); $r.ContentLength64 = $bytes.Length
            $r.OutputStream.Write($bytes, 0, $bytes.Length)
        } else {
            $b = [Text.Encoding]::UTF8.GetBytes('<html><title>UA</title><body>' + $c.Request.UserAgent + '</body></html>'); $r.ContentType = 'text/html'; $r.OutputStream.Write($b, 0, $b.Length)
        }
        $r.Close()
    }
}
$guardCmd = Get-CimInstance Win32_Process -Filter "Name='KlycGuard.exe'" | Select-Object -First 1 -ExpandProperty CommandLine
$k = Start-Process $exe -ArgumentList '--debugcmd', '--virtualonly', '--noextras' -PassThru
function Cmd($c) { [IO.File]::WriteAllText((Join-Path $data 'cmd.txt'), $c); Start-Sleep 2.5 }
function Pages() { try { (Invoke-RestMethod "http://127.0.0.1:$Port/json" -TimeoutSec 5) | ForEach-Object { $_ } | Where-Object { $_.type -eq "page" } } catch { @() } }
function Eval($wsUrl, $expr) {
    $ws = New-Object Net.WebSockets.ClientWebSocket; $ct = [Threading.CancellationToken]::None
    $ws.ConnectAsync([Uri]$wsUrl, $ct).Wait()
    $msg = '{"id":1,"method":"Runtime.evaluate","params":{"expression":' + ($expr | ConvertTo-Json) + ',"returnByValue":true}}'
    $buf = [Text.Encoding]::UTF8.GetBytes($msg)
    $ws.SendAsync((New-Object ArraySegment[byte] -ArgumentList @(, $buf)), 'Text', $true, $ct).Wait()
    $rb = New-Object byte[] 65536; $sb = New-Object Text.StringBuilder
    do { $res = $ws.ReceiveAsync((New-Object ArraySegment[byte] -ArgumentList @(, $rb)), $ct).Result; [void]$sb.Append([Text.Encoding]::UTF8.GetString($rb, 0, $res.Count)) } until ($res.EndOfMessage)
    $ws.Dispose()
    ($sb.ToString() | ConvertFrom-Json).result.result.value
}
try {
    Start-Sleep 9
    # 1) Normal sekme: TV kimliği YOK
    Cmd "url:http://127.0.0.1:$Http/ua"; Start-Sleep 5
    $p = Pages | Where-Object { $_.url -like "*127.0.0.1:$Http/ua*" } | Select-Object -First 1
    Check 'normal sekme açıldı (uzaktan hata ayıklama ile görüldü)' ($null -ne $p) ((Pages | ForEach-Object { $_.url }) -join ' | ')
    if ($p) { $ua = Eval $p.webSocketDebuggerUrl 'navigator.userAgent'; Check 'normal sekmede PlayStation kimliği YOK' ($ua -notmatch 'PlayStation') $ua }

    # 2) YouTube TV: TV kimliği + tam arayüz
    Cmd 'url:https://www.youtube.com/tv'; Start-Sleep 14
    $yt = Pages | Where-Object { $_.url -like '*youtube.com/tv*' } | Select-Object -First 1
    Check 'YouTube TV sekmesi açıldı' ($null -ne $yt) ((Pages | ForEach-Object { $_.url }) -join ' | ')
    if ($yt) {
        $ua = Eval $yt.webSocketDebuggerUrl 'navigator.userAgent'
        Check 'YouTube TV sekmesi TV kimliğiyle açıldı' ($ua -match 'PlayStation') $ua
        $len = Eval $yt.webSocketDebuggerUrl 'document.documentElement.outerHTML.length'
        Check 'TV arayüzünün tamamı geldi (sayfa >100 KB)' ([int]$len -gt 100000) "uzunluk=$len"
        $isLeanback = Eval $yt.webSocketDebuggerUrl "!!document.querySelector('ytlr-app, [class*=ytlr]') || document.documentElement.outerHTML.indexOf('ytlr') >= 0"
        Check 'sayfada ytlr (TV uygulaması) var' ($isLeanback -eq $true)
    }
    # 3) Başka siteye geçince TV kimliği bırakılır (aynı sekmede)
    #    (yeni sekme açılır; önceki normal sekme yine kimliksiz)
    Cmd "url:http://127.0.0.1:$Http/ua"; Start-Sleep 5
    $uas = Pages | Where-Object { $_.url -like "*127.0.0.1:$Http/ua*" }
    $allClean = $true; foreach ($u in $uas) { $v = Eval $u.webSocketDebuggerUrl 'navigator.userAgent'; if ($v -match 'PlayStation') { $allClean = $false } }
    Check 'yerel sayfalar hiçbir sekmede TV kimliği almadı' $allClean

    # 4) İndirme
    Cmd "url:http://127.0.0.1:$Http/file.bin"; Start-Sleep 6
    $files = Get-ChildItem $dl -ErrorAction SilentlyContinue
    Check 'indirme İndirilenler klasörüne düştü' (@($files).Count -ge 1) ((Get-ChildItem $dl | ForEach-Object Name) -join ',')
    if (@($files).Count -ge 1) {
        $f = $files | Select-Object -First 1
        Check 'dosya adı temizlendi ve tam boyutta (3 MB)' ($f.Length -eq 3MB -and $f.Name -eq 'rapor test.bin') "$($f.Name) $($f.Length)"
    }
    Cmd "url:http://127.0.0.1:$Http/file.bin"; Start-Sleep 6
    $files2 = Get-ChildItem $dl -ErrorAction SilentlyContinue
    Check 'ikinci indirme üzerine yazmadı: "(2)" adıyla indi' (@($files2).Count -eq 2 -and ($files2.Name -contains 'rapor test (2).bin')) ((Get-ChildItem $dl | ForEach-Object Name) -join ',')

    # 5) İndirme menüde görünür (araç çubuğu → menü)
    Cmd 'tab:1'; Cmd 'Start'; Cmd 'Right Right Right Right Right'; Cmd 'A'; Cmd 'Down Down Down Down Down Down Down Down Down'; Start-Sleep 1
    $win = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $k.Id
    $names = @(); foreach ($w in $win.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) { $names += foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) { try { $e.Current.Name } catch { } } }
    Check 'menüde İNDİRMELER bölümü var' ($names -contains 'İNDİRMELER')
    Check 'menüde indirilen dosya adı var' (@($names | Where-Object { $_ -like 'rapor test*' }).Count -ge 1) (($names | Where-Object { $_ -like '*rapor*' }) -join '|')
    Check 'menüde YouTube TV satırı var' (@($names | Where-Object { $_ -like '*YouTube TV (kolla izle)*' }).Count -ge 1)
}
finally {
    if ($k -and -not $k.HasExited) { Stop-Process -Id $k.Id -Force -ErrorAction SilentlyContinue }
    Get-Process msedgewebview2 -ErrorAction SilentlyContinue | Where-Object { $_.Path -and (Get-CimInstance Win32_Process -Filter "ProcessId=$($_.Id)").CommandLine -like "*$root*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-Job $srv -ErrorAction SilentlyContinue; Remove-Job $srv -Force -ErrorAction SilentlyContinue
    if ($guardCmd -and -not (Get-Process KlycGuard -ErrorAction SilentlyContinue) -and $guardCmd -match '^"([^"]+)"\s*(.*)$') { Start-Process $Matches[1] -ArgumentList $Matches[2] }
    Start-Sleep 2
    try { [IO.Directory]::Delete($root, $true) } catch { }
}
"SONUC: $ok gecti, $fail kaldi"
