# End-to-end test for Spectrum.exe
# 1. screenshot before  2. play test tone  3. launch fake QQMusic.exe  4. launch Spectrum.exe
# 5. screenshot after   6. sample CPU/memory  7. cleanup
$ErrorActionPreference = 'Continue'
$root = 'D:\software\QQMusicSpectrum'
$exe = Join-Path $root 'Spectrum.exe'
$fake = Join-Path $root '.test\QQMusic.exe'
$wav = Join-Path $root 'test_tone.wav'
$shotDir = Join-Path $root '.test'

Add-Type -AssemblyName System.Drawing
function Shot($path) {
    $b = New-Object System.Drawing.Bitmap 1600, 1000
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen(0, 0, 0, 0, $b.Size)
    $b.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $b.Dispose()
    Write-Host "shot -> $path"
}

# clean previous run state so we exercise first-run behaviour
Remove-Item (Join-Path $root 'state.ini') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $root 'spectrum.ini') -ErrorAction SilentlyContinue

Get-Process -Name 'Spectrum' -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process -Name 'QQMusic' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*QQMusicSpectrum*' } | Stop-Process -Force

Start-Sleep -Milliseconds 400
Shot (Join-Path $shotDir 'shot_before.bmp')

# fake QQ music process
$qq = Start-Process -FilePath $fake -PassThru
Start-Sleep -Milliseconds 600
Write-Host "fake QQMusic pid=$($qq.Id)"

# test tone on the default render device -> WASAPI loopback picks it up
$player = New-Object System.Media.SoundPlayer $wav
$player.PlayLooping()
Start-Sleep -Milliseconds 500

$sp = Start-Process -FilePath $exe -PassThru
Write-Host "spectrum pid=$($sp.Id)"
Start-Sleep -Seconds 4

Shot (Join-Path $shotDir 'shot_after.bmp')

# resource sampling over 10s while audio plays
$cores = [Environment]::ProcessorCount
$p1 = Get-Process -Id $sp.Id
$c1 = $p1.TotalProcessorTime.TotalSeconds
$w1 = $p1.WorkingSet64
$h1 = $p1.HandleCount
Start-Sleep -Seconds 10
$p2 = Get-Process -Id $sp.Id
$c2 = $p2.TotalProcessorTime.TotalSeconds
$w2 = $p2.WorkingSet64
$h2 = $p2.HandleCount
$cpu = (($c2 - $c1) / 10.0 / $cores) * 100.0
$cpuTotal = (($c2 - $c1) / 10.0) * 100.0

Write-Host ""
Write-Host "=== active (audio playing, 60fps) ==="
Write-Host ("working set : {0:N1} MB -> {1:N1} MB (peak {2:N1} MB)" -f ($w1/1MB), ($w2/1MB), ($p2.PeakWorkingSet64/1MB))
Write-Host ("private     : {0:N1} MB" -f ($p2.PrivateMemorySize64/1MB))
Write-Host ("cpu         : {0:N2}% of one core / {1:N2}% of all {2} cores" -f $cpuTotal, $cpu, $cores)
Write-Host ("handles     : {0} -> {1}" -f $h1, $h2)
Write-Host ("threads     : {0}" -f $p2.Threads.Count)

# idle measurement: stop the sound, let it settle
$player.Stop()
Start-Sleep -Seconds 3
$p3 = Get-Process -Id $sp.Id
$c3 = $p3.TotalProcessorTime.TotalSeconds
Start-Sleep -Seconds 10
$p4 = Get-Process -Id $sp.Id
$c4 = $p4.TotalProcessorTime.TotalSeconds
Write-Host ""
Write-Host "=== idle (silence) ==="
Write-Host ("working set : {0:N1} MB" -f ($p4.WorkingSet64/1MB))
Write-Host ("cpu         : {0:N3}% of one core / {1:N3}% of all cores" -f ((($c4-$c3)/10.0)*100.0), ((($c4-$c3)/10.0/$cores)*100.0))

# follow-qq behaviour: kill the fake QQ process, the overlay should hide
Shot (Join-Path $shotDir 'shot_playing.bmp')
Stop-Process -Id $qq.Id -Force
Start-Sleep -Seconds 3
Shot (Join-Path $shotDir 'shot_afterqq.bmp')

# autostart entry written?
$run = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'QQMusicSpectrum' -ErrorAction SilentlyContinue
Write-Host ""
Write-Host ("autostart   : " + $(if ($run) { $run.QQMusicSpectrum } else { '<not set>' }))

# cleanup
Stop-Process -Id $sp.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300
Write-Host "cleaned up"
