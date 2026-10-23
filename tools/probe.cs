using System;
using System.Text;
using System.Runtime.InteropServices;

internal static class Probe
{
    private delegate bool EnumProc(IntPtr h, IntPtr p);

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr v);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string cls, string title);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static int _found;

    private static bool Callback(IntPtr h, IntPtr p)
    {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        if (pid != (uint)(int)p) return true;

        StringBuilder sb = new StringBuilder(256);
        GetClassNameW(h, sb, 256);
        RECT r;
        GetWindowRect(h, out r);
        Console.WriteLine("  hwnd=0x{0:X} class={1} rect=({2},{3}) size={4}x{5} visible={6} ex=0x{7:X}",
            h.ToInt64(), sb.ToString(), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            IsWindowVisible(h), GetWindowLong(h, -20));
        _found++;
        return true;
    }

    private static int Main(string[] args)
    {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { }

        if (args.Length > 0 && args[0] == "class")
        {
            IntPtr h = FindWindowW("QQMSpectrumOverlayWnd", null);
            if (h == IntPtr.Zero) { Console.WriteLine("FindWindow: NOT FOUND"); return 1; }
            RECT r;
            GetWindowRect(h, out r);
            Console.WriteLine("FindWindow: hwnd=0x{0:X} rect=({1},{2}) size={3}x{4} visible={5}",
                h.ToInt64(), r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, IsWindowVisible(h));
            return 0;
        }

        if (args.Length < 1) { Console.WriteLine("usage: probe <pid>|class"); return 2; }
        int pid2;
        if (!int.TryParse(args[0], out pid2)) { Console.WriteLine("bad pid"); return 2; }

        Console.WriteLine("pid={0}", pid2);
        EnumWindows(Callback, new IntPtr(pid2));
        if (_found == 0) Console.WriteLine("  no top-level windows for this process");
        return 0;
    }
}
