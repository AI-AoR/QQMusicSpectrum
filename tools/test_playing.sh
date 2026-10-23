#!/bin/bash
# 测「正在播放」时的可见态 CPU，对比不同 fps
set -u
ROOT=/d/software/QQMusicSpectrum
DIR=$ROOT/.test/perf
OUT=$ROOT/.test/perf_result.txt
PS="powershell.exe -NoProfile -ExecutionPolicy Bypass"
: > "$OUT"

rm -rf "$DIR"; mkdir -p "$DIR"
cp "$ROOT/Spectrum.exe" "$DIR/"

mkini() {  # $1=fps
  cat > "$DIR/spectrum.ini" <<EOF
[外观]
width=520
height=170
bars=48
gap=3
theme=cyber
colors=
opacity=0.92
mirror=true
peaks=true

[位置]
position=auto
align=left
margin=24
monitor=1

[音频]
sensitivity=1.0
floor=-70
top=-10
attack=0.55
release=0.16

[行为]
followqq=false
qqprocess=QQMusic
pollms=500
fps=$1
idlefps=12
autostart=false
log=false
EOF
  rm -f "$DIR/state.ini"
}

# 循环播放测试音（0.06 振幅，很轻）
start_audio() {
  $PS -Command "\$p=New-Object Media.SoundPlayer '$ROOT/tools/test_tone.wav';
    \$p.PlayLooping()" >/dev/null 2>&1 &
  echo $!
}

cd "$DIR"

for fps in 60 30 15; do
  taskkill //IM Spectrum.exe //F >/dev/null 2>&1
  sleep 1
  mkini "$fps"
  ./Spectrum.exe >/dev/null 2>&1 &
  sleep 4
  $PS -Command "\$p=Get-Process Spectrum -ErrorAction SilentlyContinue; if(-not \$p){'no proc';exit}
    \$t1=\$p.TotalProcessorTime; Start-Sleep -Seconds 8; \$p.Refresh();
    \$t2=\$p.TotalProcessorTime;
    'fps=$fps -> {0:N2}% one-core  ws={1:N1}MB' -f ((\$t2-\$t1).TotalSeconds/8*100), (\$p.WorkingSet64/1MB)" >> "$OUT"
done

# 有声播放（走真实音频链路）单独一轮
taskkill //IM Spectrum.exe //F >/dev/null 2>&1
sleep 1
mkini 60
AUDIO_PID=$(start_audio)
sleep 1
./Spectrum.exe >/dev/null 2>&1 &
sleep 4
$PS -Command "\$p=Get-Process Spectrum -ErrorAction SilentlyContinue; if(-not \$p){'no proc';exit}
  \$t1=\$p.TotalProcessorTime; Start-Sleep -Seconds 8; \$p.Refresh();
  \$t2=\$p.TotalProcessorTime;
  'fps=60 + 音频播放 -> {0:N2}% one-core  ws={1:N1}MB' -f ((\$t2-\$t1).TotalSeconds/8*100), (\$p.WorkingSet64/1MB)" >> "$OUT"

taskkill //IM Spectrum.exe //F >/dev/null 2>&1
kill $AUDIO_PID 2>/dev/null
taskkill //IM powershell.exe //FI "WINDOWTITLE eq N/A" >/dev/null 2>&1
cat "$OUT"
