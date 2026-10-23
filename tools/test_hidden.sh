#!/bin/bash
# 只测「隐藏态」CPU，用不同参数隔离渲染线程 vs 监视线程
set -u
ROOT=/d/software/QQMusicSpectrum
DIR=$ROOT/.test/hid
OUT=$ROOT/.test/hid_result.txt
PS="powershell.exe -NoProfile -ExecutionPolicy Bypass"
: > "$OUT"

rm -rf "$DIR"; mkdir -p "$DIR"
cp "$ROOT/Spectrum.exe" "$DIR/"

mkini() {  # $1=fps $2=pollms
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
followqq=true
qqprocess=NoSuchThing
pollms=$2
fps=$1
idlefps=12
autostart=false
log=true
EOF
  rm -f "$DIR/state.ini"
}

measure() {  # $1=label
  $PS -Command "\$p=Get-Process Spectrum -ErrorAction SilentlyContinue; if(-not \$p){'no proc';exit}
    Start-Sleep -Milliseconds 500;
    \$t1=\$p.TotalProcessorTime; Start-Sleep -Seconds 8; \$p.Refresh();
    \$t2=\$p.TotalProcessorTime;
    '{0} -> {1:N2}% one-core  ws={2:N1}MB' -f '$1', ((\$t2-\$t1).TotalSeconds/8*100), (\$p.WorkingSet64/1MB)" >> "$OUT"
}

cd "$DIR"

for cfg in "60 500" "10 500" "60 10000" "10 10000"; do
  set -- $cfg
  taskkill //IM Spectrum.exe //F >/dev/null 2>&1
  sleep 1
  mkini "$1" "$2"
  ./Spectrum.exe >/dev/null 2>&1 &
  sleep 3
  measure "fps=$1 pollms=$2"
done

taskkill //IM Spectrum.exe //F >/dev/null 2>&1
cat "$OUT"
