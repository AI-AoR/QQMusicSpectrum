param([string]$Proc = "Spectrum")

Add-Type -Namespace Dpi -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
public delegate bool EnumProc(IntPtr h, IntPtr p);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

[Dpi.N]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$p = Get-Process -Name $Proc -ErrorAction SilentlyContinue
if (-not $p) { Write-Output "process not running"; exit 1 }
$target = [uint32]$p.Id
Write-Output ("pid={0}" -f $target)

$found = 0
$cb = [Dpi.N+EnumProc]{
    param($h, $l)
    $pid2 = [uint32]0
    [Dpi.N]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
    if ($pid2 -eq $target) {
        $sb = New-Object System.Text.StringBuilder 256
        [Dpi.N]::GetClassNameW($h, $sb, 256) | Out-Null
        $r = New-Object Dpi.N+RECT
        [Dpi.N]::GetWindowRect($h, [ref]$r) | Out-Null
        $vis = [Dpi.N]::IsWindowVisible($h)
        $ex = [Dpi.N]::GetWindowLong($h, -20)
        Write-Output ("  hwnd=0x{0:X} class={1} rect=({2},{3}) size={4}x{5} visible={6} ex=0x{7:X}" -f `
            [int64]$h, $sb.ToString(), $r.Left, $r.Top, ($r.Right - $r.Left), ($r.Bottom - $r.Top), $vis, $ex)
        $script:found++
    }
    return $true
}
[Dpi.N]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($script:found -eq 0) { Write-Output "  no top-level windows for this process" }
