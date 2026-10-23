// 可移植性支持层：注册表 / 多显示器 / DPI / 定时器降级 / Shell 调用。
// 设计原则：代码里不出现任何绝对路径，全部从 exe 所在目录、注册表和系统查询推导；
// 所有新系统 API 都带降级路径，保证在 Windows 7 + .NET 4.0 以上仍可运行。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace QQMSpectrum
{
    internal static partial class Win32
    {
        // ---------------- 常量 ----------------
        public const int WM_SETTINGCHANGE = 0x001A;

        public static readonly IntPtr HKEY_CURRENT_USER = new IntPtr(unchecked((int)0x80000001));
        public static readonly IntPtr HKEY_LOCAL_MACHINE = new IntPtr(unchecked((int)0x80000002));

        public const int KEY_READ = 0x20019;
        public const int KEY_SET_VALUE = 0x0002;
        public const int KEY_WOW64_32KEY = 0x0100;
        public const int KEY_WOW64_64KEY = 0x0200;
        public const int REG_SZ = 1;
        public const int ERROR_SUCCESS = 0;

        public const int SPI_GETWORKAREA = 0x0030;
        public const int LOGPIXELSX = 88;
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        public const uint MONITORINFOF_PRIMARY = 0x00000001;

        // ---------------- 结构体 ----------------
        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        // ---------------- advapi32 ----------------
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegOpenKeyExW(IntPtr hKey, string subKey, int options, int samDesired, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegQueryValueExW(IntPtr hKey, string valueName, IntPtr reserved,
            out int type, StringBuilder data, ref int dataSize);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegSetValueExW(IntPtr hKey, string valueName, int reserved,
            int type, string data, int dataSize);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int RegDeleteValueW(IntPtr hKey, string valueName);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern int RegCloseKey(IntPtr hKey);

        // ---------------- user32（显示器 / DPI） ----------------
        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SystemParametersInfoW(int action, int param, ref RECT rect, int winIni);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowW(string className, string windowName);

        // ---------------- gdi32 ----------------
        [DllImport("gdi32.dll")]
        public static extern int GetDeviceCaps(IntPtr dc, int index);

        // ---------------- kernel32 / winmm ----------------
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWaitableTimerW(IntPtr attrs, bool manualReset, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int processId);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint ms);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint ms);

        // ---------------- 进程枚举（增量检测用） ----------------
        // 只用 EnumProcesses 拿 PID 数组（一次系统调用，微秒级），
        // 只有「新出现的 PID」才用 QueryFullProcessImageName 查名字，
        // 避免 CreateToolhelp32Snapshot 每次都枚举全部进程（实测 ~9ms）。
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool EnumProcesses(uint[] pids, uint size, out uint needed);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags,
            StringBuilder buffer, ref uint size);

        // ---------------- shell32 ----------------
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr ShellExecuteW(IntPtr hwnd, string op, string file, string args, string dir, int show);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

        // ---------------- 辅助：DPI ----------------
        /// <summary>系统 DPI（96 = 100%）。新系统用 GetDpiForSystem，旧系统退回 GetDeviceCaps。</summary>
        public static int GetSystemDpi()
        {
            try
            {
                uint dpi = GetDpiForSystem();
                if (dpi >= 72 && dpi <= 480) return (int)dpi;
            }
            catch (Exception) { }
            try
            {
                IntPtr dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    int d = GetDeviceCaps(dc, LOGPIXELSX);
                    ReleaseDC(IntPtr.Zero, dc);
                    if (d >= 72 && d <= 480) return d;
                }
            }
            catch (Exception) { }
            return 96;
        }

        /// <summary>缩放系数：96 DPI 时为 1.0。</summary>
        public static float GetScale()
        {
            return GetSystemDpi() / 96f;
        }

        // ---------------- 辅助：显示器 ----------------
        /// <summary>按「主显示器优先」排序返回所有显示器的信息。</summary>
        public static List<MONITORINFO> GetMonitors()
        {
            List<MONITORINFO> primary = new List<MONITORINFO>();
            List<MONITORINFO> others = new List<MONITORINFO>();
            MonitorEnumProc proc = delegate(IntPtr hMon, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                MONITORINFO mi = new MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfoW(hMon, ref mi))
                {
                    if ((mi.dwFlags & MONITORINFOF_PRIMARY) != 0) primary.Add(mi);
                    else others.Add(mi);
                }
                return true;
            };
            try { EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero); }
            catch (Exception) { }
            others.Sort(delegate(MONITORINFO a, MONITORINFO b)
            {
                if (a.rcMonitor.Left != b.rcMonitor.Left) return a.rcMonitor.Left.CompareTo(b.rcMonitor.Left);
                return a.rcMonitor.Top.CompareTo(b.rcMonitor.Top);
            });
            List<MONITORINFO> all = new List<MONITORINFO>(primary.Count + others.Count);
            all.AddRange(primary);
            all.AddRange(others);
            return all;
        }

        /// <summary>取指定显示器的可用区域（不含任务栏）。monitor 为 1 起编号，0 或越界时回落到主显示器。</summary>
        public static RECT GetWorkArea(int monitor)
        {
            List<MONITORINFO> list = GetMonitors();
            int idx = monitor - 1;
            if (idx < 0 || idx >= list.Count) idx = 0;
            if (list.Count > 0) return list[idx].rcWork;

            RECT work = new RECT();
            work.Right = GetSystemMetrics(SM_CXSCREEN);
            work.Bottom = GetSystemMetrics(SM_CYSCREEN);
            try { SystemParametersInfoW(SPI_GETWORKAREA, 0, ref work, 0); }
            catch (Exception) { }
            return work;
        }

        // ---------------- 辅助：高精度定时器 ----------------
        /// <summary>
        /// 优先用 CreateWaitableTimerExW 的高精度标志（Win10 1803+，不改变系统时钟精度）；
        /// 旧系统退回普通定时器 + timeBeginPeriod(1)，保证 60fps 不被 15.6ms 量化。
        /// </summary>
        public static IntPtr CreateFrameTimer(out bool highResolution)
        {
            highResolution = false;
            try
            {
                IntPtr h = CreateWaitableTimerExW(IntPtr.Zero, null,
                    CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
                if (h != IntPtr.Zero) { highResolution = true; return h; }
            }
            catch (Exception) { }
            try
            {
                IntPtr h = CreateWaitableTimerW(IntPtr.Zero, false, null);
                if (h != IntPtr.Zero) { timeBeginPeriod(1); return h; }
            }
            catch (Exception) { }
            return IntPtr.Zero;
        }

        // ---------------- 辅助：注册表 ----------------
        public static string RegReadString(IntPtr root, string subKey, string valueName)
        {
            IntPtr key;
            if (RegOpenKeyExW(root, subKey, 0, KEY_READ, out key) != ERROR_SUCCESS) return null;
            try
            {
                int type = 0;
                int size = 1024;
                StringBuilder sb = new StringBuilder(1024);
                if (RegQueryValueExW(key, valueName, IntPtr.Zero, out type, sb, ref size) != ERROR_SUCCESS) return null;
                return sb.ToString();
            }
            finally { RegCloseKey(key); }
        }

        public static bool RegValueExists(IntPtr root, string subKey, string valueName)
        {
            return RegReadString(root, subKey, valueName) != null;
        }

        public static bool RegWriteString(IntPtr root, string subKey, string valueName, string data)
        {
            IntPtr key;
            if (RegOpenKeyExW(root, subKey, 0, KEY_SET_VALUE, out key) != ERROR_SUCCESS) return false;
            try
            {
                int bytes = (data.Length + 1) * 2;
                return RegSetValueExW(key, valueName, 0, REG_SZ, data, bytes) == ERROR_SUCCESS;
            }
            finally { RegCloseKey(key); }
        }

        public static bool RegDeleteValue(IntPtr root, string subKey, string valueName)
        {
            IntPtr key;
            if (RegOpenKeyExW(root, subKey, 0, KEY_SET_VALUE, out key) != ERROR_SUCCESS) return false;
            try { return RegDeleteValueW(key, valueName) == ERROR_SUCCESS; }
            finally { RegCloseKey(key); }
        }

        // ---------------- 辅助：Shell ----------------
        public static void ShellOpen(string path)
        {
            try { ShellExecuteW(IntPtr.Zero, "open", path, null, null, 1); }
            catch (Exception) { }
        }

        /// <summary>从任意 exe/ico 里取第一个图标；失败返回 IntPtr.Zero。</summary>
        public static IntPtr LoadFirstIcon(string file)
        {
            try
            {
                IntPtr large, small;
                if (ExtractIconExW(file, 0, out large, out small, 1) > 0)
                {
                    if (large != IntPtr.Zero) { if (small != IntPtr.Zero) DestroyIcon(small); return large; }
                    return small;
                }
            }
            catch (Exception) { }
            return IntPtr.Zero;
        }
    }
}
