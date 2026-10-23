#!/bin/bash
# 验证「随 QQ 音乐出现/隐藏」与各状态下的 CPU 占用
set -u
ROOT=/d/software/QQMusicSpectrum
DIR=$ROOT/.test/vis
PS="powershell.exe -NoProfile -ExecutionPolicy Bypass"

rm -rf "$DIR"; mkdir -p "$DIR"
cp "$ROOT/Spectrum.exe" "$DIR/"
cp "$ROOT/.test/QQTestFake.exe" "$DIR/" 2>/dev/null

cat > "$DIR/spectrum.ini" <<'EOF'
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
qqprocess=QQTestFake
pollms=500
fps=60
idlefps=12
autostart=false
log=true
EOF

cpu() {  # $1 = 秒数
  $PS -Command "\$p=Get-Process Spectrum -ErrorAction SilentlyContinue; if(-not \$p){'no proc';exit}
    \$t1=\$p.TotalProcessorTime; Start-Sleep -Seconds $1; \$p.Refresh();
    \$t2=\$p.TotalProcessorTime;
    \$pct=(\$t2-\$t1).TotalSeconds/$1*100;
    '{0:N2}% one-core  ws={1:N1}MB  priv={2:N1}MB  thr={3} hnd={4}' -f \$pct,(\$p.WorkingSet64/1MB),(\$p.PrivateMemorySize64/1MB),\$p.Threads.Count,\$p.HandleCount"
}

cd "$DIR"
./Spectrum.exe &
sleep 3

echo "== 1) QQ 未运行（应隐藏）=="
$ROOT/tools/probe.exe class
cpu 8

echo
echo "== 2) 启动假 QQ（应显示）=="
./QQTestFake.exe &
sleep 3
$ROOT/tools/probe.exe class
cpu 8

echo
echo "== 3) 关掉假 QQ（应隐藏）=="
taskkill //IM QQTestFake.exe //F >/dev/null 2>&1
sleep 3
$ROOT/tools/probe.exe class
cpu 8

echo
echo "== 日志 =="
cat spectrum.log 2>/dev/null | tail -14
