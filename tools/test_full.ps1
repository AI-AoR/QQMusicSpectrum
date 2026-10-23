# Full end-to-end test for QQMusicSpectrum.
# Every phase writes its own screenshot / numbers; analysis is done afterwards in Python.
$ErrorActionPreference = 'Continue'
$root  = 'D:\software\QQMusicSpectrum'
$exe   = Join-Path $root 'Spectrum.exe'
$fake  = Join-Path $root '.test\QQTestFake.exe'
$wav   = Join-Path $root 'test_tone.wav'
$shots = Join-Path $root '.test'
$ini   = Join-Path $root 'spectrum.ini'
$state = Join-Path $root 'state.ini'
$log   = Join-Path $root 'spectrum.log'

Add-Type -AssemblyName System.Drawing
$utf8 = New-Object System.Text.UTF8Encoding $false

function Shot($name) {
    $path = Join-Path $shots $name
    $b = New-Object System.Drawing.Bitmap 1600, 1000
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen(0, 0, 0, 0, $b.Size)
    $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $b.Dispose()
    Write-Host ("  shot -> " + $name)
}

function WriteIni($text) {
    [System.IO.File]::WriteAllText($ini, $text, $utf8)
}

function KillApp() {
    Get-Process -Name 'Spectrum' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

function CpuMem($procId, $seconds) {
    $p1 = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if (-not $p1) { return $null }
    $c1 = $p1.TotalProcessorTime.TotalSeconds
    Start-Sleep -Seconds $seconds
    $p2 = Get-Process -Id $procId -ErrorAction SilentlyContinue
    if (-not $p2) { return $null }
    $c2 = $p2.TotalProcessorTime.TotalSeconds
    return [pscustomobject]@{
        CpuOneCore = (($c2 - $c1) / $seconds) * 100.0
        Ws         = $p2.WorkingSet64 / 1MB
        Private    = $p2.PrivateMemorySize64 / 1MB
        Handles    = $p2.HandleCount
        Threads    = $p2.Threads.Count
        CpuSec     = ($c2 - $c1)
    }
}

$cores = [Environment]::ProcessorCount

Write-Host "=== phase 0: cleanup ==="
KillApp
Get-Process -Name 'QQTestFake' -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $state -ErrorAction SilentlyContinue
Remove-Item $log -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

Write-Host ""
Write-Host "=== phase 1: built-in selftest (FFT + 6 themes) ==="
$st = Start-Process -FilePath $exe -ArgumentList '--selftest' -Wait -PassThru
Write-Host ("  exit code = " + $st.ExitCode)
Get-Content (Join-Path $root 'selftest.txt') -Encoding UTF8 | ForEach-Object { "  " + $_ }

Write-Host ""
Write-Host "=== phase 2: rendering, always visible (followqq=false) ==="
WriteIni @'
[appearance]
width=520
height=170
bars=48
gap=3
minheight=2
theme=cyber
opacity=0.92
mirror=true
peaks=true

[position]
position=auto
align=left
margin=24
monitor=1

[audio]
floor=-70
top=-10
attack=0.55
release=0.16

[behavior]
followqq=false
qqprocess=QQMusic
pollms=2000
fps=60
idlefps=12
autostart=false
log=true
'@

Shot 'p2_before.bmp'
$sp = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
Shot 'p2_rest.bmp'
$rest = CpuMem $sp.Id 8
Write-Host ("  idle/silent : cpu {0:N3}% one-core, ws {1:N1} MB, handles {2}, threads {3}" -f $rest.CpuOneCore, $rest.Ws, $rest.Handles, $rest.Threads)

$player = New-Object System.Media.SoundPlayer $wav
$player.PlayLooping()
Start-Sleep -Seconds 2
Shot 'p2_play1.bmp'
$act = CpuMem $sp.Id 8
Write-Host ("  audio playing: cpu {0:N2}% one-core, ws {1:N1} MB, private {2:N1} MB, handles {3}, threads {4}" -f $act.CpuOneCore, $act.Ws, $act.Private, $act.Handles, $act.Threads)
Shot 'p2_play2.bmp'
Start-Sleep -Milliseconds 700
Shot 'p2_play3.bmp'
$player.Stop()
$player.Dispose()
KillApp

Write-Host ""
Write-Host "=== phase 3: follow-qq show/hide (qqprocess=QQTestFake) ==="
WriteIni @'
[appearance]
width=520
height=170
bars=48
gap=3
minheight=2
theme=aurora
opacity=0.92
mirror=true
peaks=true

[position]
position=auto
align=right
margin=24
monitor=1

[audio]
floor=-70
top=-10
attack=0.55
release=0.16

[behavior]
followqq=true
qqprocess=QQTestFake
pollms=1000
fps=60
idlefps=12
autostart=false
log=true
'@

Shot 'p3_noqq.bmp'
$sp = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
Shot 'p3_hidden.bmp'

$qq = Start-Process -FilePath $fake -PassThru
Write-Host ("  fake qq started pid=" + $qq.Id)
Start-Sleep -Seconds 3
Shot 'p3_shown.bmp'
$shown = CpuMem $sp.Id 6
Write-Host ("  overlay visible: cpu {0:N3}% one-core, ws {1:N1} MB" -f $shown.CpuOneCore, $shown.Ws)

Stop-Process -Id $qq.Id -Force
Start-Sleep -Seconds 3
Shot 'p3_hidden2.bmp'
$hid = CpuMem $sp.Id 8
Write-Host ("  overlay hidden : cpu {0:N3}% one-core, ws {1:N1} MB" -f $hid.CpuOneCore, $hid.Ws)
KillApp

Write-Host ""
Write-Host "=== phase 4: autostart self-heal ==="
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$name = 'QQMusicSpectrum'
$bogus = '"C:\old-place\Spectrum.exe" --tray'
Set-ItemProperty -Path $runKey -Name $name -Value $bogus
Write-Host ("  planted bogus entry: " + $bogus)
WriteIni @'
[appearance]
width=520
height=170
bars=48
gap=3
minheight=2
theme=cyber
opacity=0.92
mirror=true
peaks=true

[position]
position=auto
align=left
margin=24
monitor=1

[audio]
floor=-70
top=-10
attack=0.55
release=0.16

[behavior]
followqq=true
qqprocess=QQMusic
pollms=2000
fps=60
idlefps=12
autostart=true
log=false
'@
$sp = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
$fixed = (Get-ItemProperty -Path $runKey -Name $name -ErrorAction SilentlyContinue).$name
Write-Host ("  entry after start  : " + $fixed)
Write-Host ("  self-healed        : " + $(if ($fixed -like '*QQMusicSpectrum\Spectrum.exe*') { 'YES' } else { 'NO' }))

Write-Host ""
Write-Host "=== phase 5: 30s stability while playing ==="
$player2 = New-Object System.Media.SoundPlayer $wav
$player2.PlayLooping()
$series = @()
for ($i = 0; $i -lt 6; $i++) {
    Start-Sleep -Seconds 5
    $p = Get-Process -Id $sp.Id -ErrorAction SilentlyContinue
    if (-not $p) { Write-Host "  !! process died at sample $i"; break }
    $series += [pscustomobject]@{ T = ($i + 1) * 5; Ws = $p.WorkingSet64 / 1MB; Handles = $p.HandleCount; Threads = $p.Threads.Count }
    Write-Host ("  t={0,2}s  ws={1,6:N1} MB  handles={2,3}  threads={3}" -f (($i + 1) * 5), ($p.WorkingSet64 / 1MB), $p.HandleCount, $p.Threads.Count)
}
$player2.Stop()
$player2.Dispose()
if ($series.Count -ge 2) {
    $drift = $series[-1].Ws - $series[0].Ws
    $hdrift = $series[-1].Handles - $series[0].Handles
    Write-Host ("  ws drift over 30s: {0:N2} MB   handle drift: {1}" -f $drift, $hdrift)
    Write-Host ("  verdict: " + $(if ([math]::Abs($drift) -lt 3 -and [math]::Abs($hdrift) -lt 20) { 'stable (no leak)' } else { 'CHECK - growth detected' }))
}

Write-Host ""
Write-Host "=== phase 6: log ==="
if (Test-Path $log) { Get-Content $log -Encoding UTF8 | ForEach-Object { "  " + $_ } } else { Write-Host "  (no log file)" }

KillApp
Get-Process -Name 'QQTestFake' -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host ""
Write-Host "done"
