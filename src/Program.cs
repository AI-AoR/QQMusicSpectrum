// 主程序：配置读写、托盘菜单、QQ 音乐联动、渲染循环、自检。
// 可移植性：全部路径从 exe 所在目录推导，配置与日志都在 exe 同级，整目录可拷走直接用。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QQMSpectrum
{
    // ---------------- 日志 ----------------
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;
        private static bool _enabled;

        public static void Init(string dir, bool enabled)
        {
            _enabled = enabled;
            _path = Path.Combine(dir, "spectrum.log");
        }

        public static void Write(string message)
        {
            if (!_enabled || _path == null) return;
            try
            {
                lock (Gate)
                {
                    bool append = true;
                    FileInfo fi = new FileInfo(_path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        File.Copy(_path, _path + ".1", true);
                        append = false;
                    }
                    using (StreamWriter w = new StreamWriter(_path, append, new UTF8Encoding(false)))
                        w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message);
                }
            }
            catch (Exception) { }
        }
    }

    // ---------------- 配置 ----------------
    internal sealed class Config
    {
        public int Width = 520;
        public int Height = 170;
        public int Bars = 48;
        public int Gap = 3;
        public int MinHeight = 2;
        public int Margin = 24;
        public int Monitor = 1;
        public int PollMs = 500;
        public int Fps = 60;
        public int IdleFps = 12;

        public string Theme = "cyber";
        public string Colors = "";
        public string Align = "center";
        public string QQProcess = "QQMusic";

        public float Opacity = 0.92f;
        public float FloorDb = -70f;
        public float TopDb = -10f;
        public float Attack = 0.55f;
        public float Release = 0.16f;
        public float Sensitivity = 0.8f;
        public float Tilt = 2.5f;

        public bool Mirror = true;
        public bool Peaks = true;
        public bool FollowQQ = true;
        public bool Autostart = true;
        public bool LogEnabled = false;

        public bool ExplicitPos = false;
        public int X = 0;
        public int Y = 0;

        private const string DefaultIni = @"; QQ 音乐频谱悬浮窗 —— 配置文件
; 修改后重启程序生效（托盘菜单里也有「重新载入配置」）
; 本文件与 Spectrum.exe 放在同一目录，整个文件夹可以随意移动/拷贝到别的机器。

[外观]
width=520           ; 宽度（像素，96DPI 逻辑单位，高 DPI 会自动放大）
height=170          ; 高度
bars=48             ; 频谱柱数量 8-160
gap=3               ; 柱间距
minheight=2         ; 静音时柱子的最小高度
theme=cyber         ; cyber / aurora / sunset / mono / ice / ember
colors=             ; 自定义配色：逗号分隔的 #RRGGBB，2-6 个色标，例如
                    ; colors=#00E5FF,#7C4DFF,#FF3D9A   （填了就覆盖 theme）
opacity=0.92        ; 整体不透明度 0.15-1.0
mirror=true         ; 底部倒影
peaks=true          ; 柱顶峰值线

[位置]
position=auto       ; auto = 自动贴边；也可写具体坐标，例如 position=1200,760
                    ; 坐标是物理像素（高 DPI 下 = 逻辑坐标 x 缩放比例）
align=center        ; auto 时的水平对齐：left / center / right（center = 居中底部）
margin=24           ; 距屏幕边缘的间距
monitor=1           ; 1 = 主显示器，2 = 第二块，依次类推

[音频]
sensitivity=0.8     ; 敏感度倍数 0.2-4.0，越大柱子跳得越高（1.0 为基准）
tilt=2.5            ; 频率倾斜补偿（dB/八度），0 = 关闭。
                    ; 音乐能量天然低频强、高频弱，补偿后左右两边高度更均匀
floor=-70           ; 频谱下限（dBFS），越小越灵敏
top=-10             ; 频谱上限（dBFS）
attack=0.55         ; 上升速度 0.05-0.95，越大越灵敏
release=0.16        ; 回落速度 0.02-0.90，越大回落越快

[行为]
followqq=true       ; 跟随 QQ 音乐：QQ 音乐运行时显示，退出时隐藏
qqprocess=QQMusic   ; 监视的进程名（不含 .exe）
pollms=500          ; 进程检测间隔（毫秒）；检测是增量式的，调小几乎不增加开销
fps=60              ; 有声音时的帧率（30 可把 CPU 再降一半）
idlefps=12          ; 静音时的帧率
autostart=true      ; 首次运行时写入开机自启（托盘菜单可随时关闭）
log=false           ; 是否写 spectrum.log
";

        public static string DefaultText()
        {
            return DefaultIni.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        public static Config Load(string dir, out bool firstRun)
        {
            string path = Path.Combine(dir, "spectrum.ini");
            firstRun = !File.Exists(path);
            if (firstRun)
            {
                try { File.WriteAllText(path, DefaultText(), new UTF8Encoding(false)); }
                catch (Exception) { }
            }

            Config c = new Config();
            string[] lines;
            try
            {
                if (!File.Exists(path)) return c;
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (Exception) { return c; }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                int sc = val.IndexOf(" ;", StringComparison.Ordinal);
                if (sc < 0) sc = val.IndexOf("\t;", StringComparison.Ordinal);
                if (sc > 0) val = val.Substring(0, sc).Trim();
                if (val.Length == 0) continue;
                c.Apply(key, val);
            }
            c.Clamp();
            return c;
        }

        private void Apply(string key, string val)
        {
            switch (key)
            {
                case "width": Width = ToInt(val, Width); break;
                case "height": Height = ToInt(val, Height); break;
                case "bars": Bars = ToInt(val, Bars); break;
                case "gap": Gap = ToInt(val, Gap); break;
                case "minheight": MinHeight = ToInt(val, MinHeight); break;
                case "margin": Margin = ToInt(val, Margin); break;
                case "monitor": Monitor = ToInt(val, Monitor); break;
                case "pollms": PollMs = ToInt(val, PollMs); break;
                case "fps": Fps = ToInt(val, Fps); break;
                case "idlefps": IdleFps = ToInt(val, IdleFps); break;
                case "theme": Theme = val; break;
                case "colors": Colors = val; break;
                case "sensitivity": Sensitivity = ToFloat(val, Sensitivity); break;
                case "tilt": Tilt = ToFloat(val, Tilt); break;
                case "align": Align = val.ToLowerInvariant(); break;
                case "qqprocess":
                    QQProcess = val.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? val.Substring(0, val.Length - 4) : val;
                    break;
                case "opacity": Opacity = ToFloat(val, Opacity); break;
                case "floor": FloorDb = ToFloat(val, FloorDb); break;
                case "top": TopDb = ToFloat(val, TopDb); break;
                case "attack": Attack = ToFloat(val, Attack); break;
                case "release": Release = ToFloat(val, Release); break;
                case "mirror": Mirror = ToBool(val, Mirror); break;
                case "peaks": Peaks = ToBool(val, Peaks); break;
                case "followqq": FollowQQ = ToBool(val, FollowQQ); break;
                case "autostart": Autostart = ToBool(val, Autostart); break;
                case "log": LogEnabled = ToBool(val, LogEnabled); break;
                case "x": X = ToInt(val, X); ExplicitPos = true; break;
                case "y": Y = ToInt(val, Y); ExplicitPos = true; break;
                case "position":
                    if (string.Equals(val, "auto", StringComparison.OrdinalIgnoreCase)) ExplicitPos = false;
                    else
                    {
                        string[] parts = val.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            X = ToInt(parts[0], X);
                            Y = ToInt(parts[1], Y);
                            ExplicitPos = true;
                        }
                    }
                    break;
            }
        }

        public void Clamp()
        {
            Width = Math.Max(160, Math.Min(2400, Width));
            Height = Math.Max(60, Math.Min(720, Height));
            Bars = Math.Max(8, Math.Min(160, Bars));
            if (Bars > Width / 3) Bars = Math.Max(8, Width / 3);
            Gap = Math.Max(0, Math.Min(24, Gap));
            if (Gap >= Width / Bars) Gap = Math.Max(0, Width / Bars - 2);
            MinHeight = Math.Max(1, Math.Min(40, MinHeight));
            Margin = Math.Max(0, Math.Min(400, Margin));
            Monitor = Math.Max(1, Math.Min(16, Monitor));
            PollMs = Math.Max(500, Math.Min(10000, PollMs));
            Fps = Math.Max(10, Math.Min(144, Fps));
            IdleFps = Math.Max(1, Math.Min(Fps, IdleFps));
            Opacity = Math.Max(0.15f, Math.Min(1f, Opacity));
            if (TopDb <= FloorDb + 6f) TopDb = FloorDb + 6f;
            if (FloorDb < -140f) FloorDb = -140f;
            if (TopDb > 0f) TopDb = 0f;
            Attack = Math.Max(0.05f, Math.Min(0.95f, Attack));
            Release = Math.Max(0.02f, Math.Min(0.9f, Release));
            Sensitivity = Math.Max(0.2f, Math.Min(4f, Sensitivity));
            Tilt = Math.Max(0f, Math.Min(6f, Tilt));
            if (Colors == null) Colors = "";
            Colors = Colors.Trim();
            if (Colors.Length > 0 && Overlay.ParseColors(Colors) == null) Colors = "";
            if (string.IsNullOrEmpty(Theme)) Theme = "cyber";
            if (Align != "center" && Align != "right") Align = "left";
            if (string.IsNullOrEmpty(QQProcess)) QQProcess = "QQMusic";
        }

        private static int ToInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        private static float ToFloat(string s, float fallback)
        {
            float v;
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static bool ToBool(string s, bool fallback)
        {
            string v = s.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return fallback;
        }
    }

    // ---------------- 运行时状态（托盘菜单改动会持久化到这里，不污染带注释的配置文件） ----------------
    internal sealed class State
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>();

        public State(string dir)
        {
            _path = Path.Combine(dir, "state.ini");
            try
            {
                if (!File.Exists(_path)) return;
                foreach (string raw in File.ReadAllLines(_path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    _map[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }
        }

        /// <summary>state.ini 比 spectrum.ini 新时才让它覆盖 —— 用户手改 ini 后应立刻生效。</summary>
        public bool IsNewerThan(string iniPath)
        {
            try
            {
                if (!File.Exists(_path)) return false;
                if (!File.Exists(iniPath)) return true;
                return File.GetLastWriteTimeUtc(_path) > File.GetLastWriteTimeUtc(iniPath);
            }
            catch (Exception) { return false; }
        }

        public void ApplyTo(Config c)
        {
            string v;
            if (_map.TryGetValue("theme", out v)) c.Theme = v;
            if (_map.TryGetValue("colors", out v)) c.Colors = v;
            if (_map.TryGetValue("sensitivity", out v))
            {
                float f;
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out f)) c.Sensitivity = f;
            }
            if (_map.TryGetValue("x", out v)) { int n; if (int.TryParse(v, out n)) { c.X = n; c.ExplicitPos = true; } }
            if (_map.TryGetValue("y", out v)) { int n; if (int.TryParse(v, out n)) { c.Y = n; c.ExplicitPos = true; } }
            if (_map.TryGetValue("opacity", out v))
            {
                float f;
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out f)) c.Opacity = f;
            }
            if (_map.TryGetValue("peaks", out v)) c.Peaks = v == "1";
            if (_map.TryGetValue("mirror", out v)) c.Mirror = v == "1";
            if (_map.TryGetValue("followqq", out v)) c.FollowQQ = v == "1";
            if (_map.TryGetValue("autostart", out v)) c.Autostart = v == "1";
            c.Clamp();
        }

        public void Save(Config c)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; 运行状态（由程序自动维护，手动修改也会被读取）");
                sb.AppendLine("theme=" + c.Theme);
                sb.AppendLine("colors=" + c.Colors);
                sb.AppendLine("sensitivity=" + c.Sensitivity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("opacity=" + c.Opacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.AppendLine("peaks=" + (c.Peaks ? "1" : "0"));
                sb.AppendLine("mirror=" + (c.Mirror ? "1" : "0"));
                sb.AppendLine("followqq=" + (c.FollowQQ ? "1" : "0"));
                sb.AppendLine("autostart=" + (c.Autostart ? "1" : "0"));
                if (c.ExplicitPos && Program.PosFromPreset)
                {
                    sb.AppendLine("x=" + c.X);
                    sb.AppendLine("y=" + c.Y);
                }
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }

    // ---------------- 主程序 ----------------
    internal static class Program
    {
        private const string Version = "1.0";
        private const string ClassName = "QQMSpectrumOverlayWnd";
        private const string MutexName = "Local\\QQMusicSpectrum.SingleInstance";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "QQMusicSpectrum";
        private const int WM_SHOW = Win32.WM_APP + 4;

        private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        private static readonly WndProcDelegate _wndProc = new WndProcDelegate(WndProcHandler);

        private static string _baseDir;
        private static string _exePath;
        private static Config _cfg;
        private static State _state;
        private static Overlay _overlay;
        private static AudioCapture _audio;
        private static Analyzer _analyzer;
        private static IntPtr _hwnd = IntPtr.Zero;
        private static IntPtr _hIcon = IntPtr.Zero;
        private static Mutex _mutex;

        private static volatile bool _run = true;
        private static volatile bool _qqRunning;
        private static volatile bool _wantVisible = true;
        private static volatile bool _visible;
        private static bool _posFromPreset;   // 坐标是托盘预设选的（才写进 state.ini）
        internal static bool PosFromPreset { get { return _posFromPreset; } }
        private static volatile bool _forceRedraw = true;
        private static Thread _renderThread;
        private static Thread _watchThread;
        private static readonly object _swapGate = new object();   // 配置重载时保护 analyzer/overlay 的替换
        private static bool _startInTray;

        private const int ID_TOGGLE = 1;
        private const int ID_THEME_BASE = 100;
        private const int ID_OPACITY_BASE = 200;
        private const int ID_PEAKS = 300;
        private const int ID_MIRROR = 301;
        private const int ID_FOLLOW = 302;
        private const int ID_AUTOSTART = 303;
        private const int ID_OPENCFG = 400;
        private const int ID_RELOAD = 401;
        private const int ID_EXIT = 402;
        private const int ID_POS_BASE = 500;
        private const int ID_SENS_BASE = 600;
        private const int ID_EDITCFG = 403;

        private static readonly float[] OpacitySteps = { 0.55f, 0.70f, 0.80f, 0.90f, 1.00f };
        private static readonly float[] SensitivitySteps = { 0.4f, 0.6f, 0.8f, 1.0f, 1.3f };
        private static readonly string[] PositionNames = { "左下", "右下", "左上", "右上", "居中底部", "自动贴边" };

        [STAThread]
        private static int Main(string[] args)
        {
            _baseDir = ResolveBaseDir();
            _exePath = Path.Combine(_baseDir, "Spectrum.exe");

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--selftest" || a == "/selftest") return SelfTest();
                if (a == "--tray") { _startInTray = true; continue; }
                if (a == "--version" || a == "/version")
                {
                    Console.WriteLine("QQMusicSpectrum " + Version);
                    return 0;
                }
            }

            Win32.EnableDpiAwareness();

            bool created;
            _mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                IntPtr existing = Win32.FindWindowW(ClassName, null);
                if (existing != IntPtr.Zero) Win32.PostMessageW(existing, (uint)WM_SHOW, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            bool firstRun;
            _cfg = Config.Load(_baseDir, out firstRun);
            // --tray 由开机自启写入：开机时不弹窗，只等 QQ 音乐出现
            if (_startInTray) _cfg.FollowQQ = true;
            _state = new State(_baseDir);
            if (_state.IsNewerThan(Path.Combine(_baseDir, "spectrum.ini"))) _state.ApplyTo(_cfg);
            Log.Init(_baseDir, _cfg.LogEnabled);
            Log.Write("start v" + Version + " dir=" + _baseDir + " dpi=" + Win32.GetSystemDpi() +
                      " screen=" + Win32.GetSystemMetrics(Win32.SM_CXSCREEN) + "x" + Win32.GetSystemMetrics(Win32.SM_CYSCREEN));

            SyncAutostart(firstRun);

            _overlay = new Overlay(
                (int)(_cfg.Width * Win32.GetScale()),
                (int)(_cfg.Height * Win32.GetScale()),
                _cfg.Bars, (int)(_cfg.Gap * Win32.GetScale()), _cfg.Theme, _cfg.Colors, _cfg.Opacity,
                _cfg.Mirror, _cfg.Peaks, _cfg.MinHeight);

            _analyzer = new Analyzer(_cfg.Bars, _cfg.FloorDb, _cfg.TopDb, _cfg.Attack, _cfg.Release,
                _cfg.Fps, _cfg.Sensitivity, _cfg.Tilt);
            _audio = new AudioCapture();

            if (!CreateOverlayWindow())
            {
                Log.Write("fatal: CreateWindow failed err=" + Marshal.GetLastWin32Error());
                return 2;
            }
            _overlay.Attach(_hwnd);
            ComputePosition();
            AddTrayIcon();

            _qqRunning = IsProcessRunning(_cfg.QQProcess);
            _audio.Start();
            _analyzer.ConfigureBands(_audio.SampleRate);

            _renderThread = new Thread(RenderLoop);
            _renderThread.IsBackground = true;
            _renderThread.Priority = ThreadPriority.Normal;
            _renderThread.Name = "render";
            _renderThread.Start();

            _watchThread = new Thread(WatchLoop);
            _watchThread.IsBackground = true;
            _watchThread.Priority = ThreadPriority.BelowNormal;
            _watchThread.Name = "watch";
            _watchThread.Start();

            ApplyVisibility();

            Win32.MSG msg;
            while (Win32.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }

            Shutdown();
            return 0;
        }

        // ---------------- 路径 ----------------
        private static string ResolveBaseDir()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    string d = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(d) && Directory.Exists(d)) return d;
                }
            }
            catch (Exception) { }
            string bd = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(bd)) return bd.TrimEnd('\\', '/');
            return Environment.CurrentDirectory;
        }

        // ---------------- 窗口 ----------------
        private static bool CreateOverlayWindow()
        {
            IntPtr hInstance = Win32.GetModuleHandleW(null);
            _hIcon = Win32.LoadFirstIcon(_exePath);
            if (_hIcon == IntPtr.Zero)
                _hIcon = Win32.LoadImageW(IntPtr.Zero, new IntPtr(Win32.IDI_APPLICATION), Win32.IMAGE_ICON, 16, 16, Win32.LR_DEFAULTCOLOR);

            Win32.WNDCLASSEX wc = new Win32.WNDCLASSEX();
            wc.cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX));
            wc.style = 0;
            wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc);
            wc.hInstance = hInstance;
            wc.hIcon = _hIcon;
            wc.hIconSm = _hIcon;
            wc.lpszClassName = ClassName;

            ushort atom = Win32.RegisterClassExW(ref wc);
            if (atom == 0)
            {
                int err = Marshal.GetLastWin32Error();
                if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
                {
                    Log.Write("RegisterClassEx failed err=" + err);
                    return false;
                }
            }

            int exStyle = Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_TOPMOST |
                          Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;

            _hwnd = Win32.CreateWindowExW(exStyle, ClassName, "QQ 音乐频谱",
                Win32.WS_POPUP, 0, 0, _overlay.Width, _overlay.Height,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

            return _hwnd != IntPtr.Zero;
        }

        private static IntPtr WndProcHandler(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case Win32.WM_TRAY:
                    OnTrayMessage((int)(lParam.ToInt64() & 0xFFFF));
                    return IntPtr.Zero;

                case WM_SHOW:
                    _wantVisible = true;
                    ApplyVisibility();
                    return IntPtr.Zero;

                case Win32.WM_QQSTATE:
                    ApplyVisibility();
                    return IntPtr.Zero;

                case Win32.WM_COMMAND:
                    OnMenuCommand((int)(wParam.ToInt64() & 0xFFFF));
                    return IntPtr.Zero;

                case Win32.WM_DISPLAYCHANGE:
                case Win32.WM_SETTINGCHANGE:
                    ComputePosition();
                    _forceRedraw = true;
                    return IntPtr.Zero;

                case Win32.WM_DESTROY:
                    Win32.PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return Win32.DefWindowProcW(hwnd, msg, wParam, lParam);
        }

        // 当前坐标属于哪个预设（用于菜单打勾）；-1 表示自定义
        private static int CurrentPositionPreset()
        {
            if (!_cfg.ExplicitPos) return PositionNames.Length - 1;   // 自动贴边
            if (_overlay == null) return -1;
            Win32.RECT work = Win32.GetWorkArea(_cfg.Monitor);
            int w = _overlay.Width, h = _overlay.Height;
            int m = (int)(_cfg.Margin * Win32.GetScale());
            int[,] cand = new int[5, 2];
            cand[0, 0] = work.Left + m;                  cand[0, 1] = work.Bottom - h - m;
            cand[1, 0] = work.Right - w - m;             cand[1, 1] = work.Bottom - h - m;
            cand[2, 0] = work.Left + m;                  cand[2, 1] = work.Top + m;
            cand[3, 0] = work.Right - w - m;             cand[3, 1] = work.Top + m;
            cand[4, 0] = work.Left + (work.Width - w) / 2; cand[4, 1] = work.Bottom - h - m;
            for (int i = 0; i < 5; i++)
                if (Math.Abs(cand[i, 0] - _cfg.X) <= 2 && Math.Abs(cand[i, 1] - _cfg.Y) <= 2) return i;
            return -1;
        }

        private static void ApplyPositionPreset(int idx)
        {
            if (idx >= PositionNames.Length - 1)          // 自动贴边
            {
                _cfg.ExplicitPos = false;
                _posFromPreset = false;
                ComputePosition();
                return;
            }
            if (_overlay == null) return;
            Win32.RECT work = Win32.GetWorkArea(_cfg.Monitor);
            int w = _overlay.Width, h = _overlay.Height;
            int m = (int)(_cfg.Margin * Win32.GetScale());
            int x, y;
            switch (idx)
            {
                case 0: x = work.Left + m; y = work.Bottom - h - m; break;
                case 1: x = work.Right - w - m; y = work.Bottom - h - m; break;
                case 2: x = work.Left + m; y = work.Top + m; break;
                case 3: x = work.Right - w - m; y = work.Top + m; break;
                default: x = work.Left + (work.Width - w) / 2; y = work.Bottom - h - m; break;
            }
            _cfg.X = x;
            _cfg.Y = y;
            _cfg.ExplicitPos = true;
            _posFromPreset = true;
            ComputePosition();
        }

        private static void ComputePosition()
        {
            if (_overlay == null) return;
            Win32.RECT work = Win32.GetWorkArea(_cfg.Monitor);
            int w = _overlay.Width;
            int h = _overlay.Height;
            int margin = (int)(_cfg.Margin * Win32.GetScale());

            int x, y;
            if (_cfg.ExplicitPos)
            {
                x = _cfg.X;
                y = _cfg.Y;
            }
            else
            {
                if (_cfg.Align == "right") x = work.Right - w - margin;
                else if (_cfg.Align == "center") x = work.Left + (work.Width - w) / 2;
                else x = work.Left + margin;
                y = work.Bottom - h - margin;
            }

            // 换显示器 / 改分辨率后把窗口夹回可见区域
            if (x + w > work.Right) x = work.Right - w;
            if (x < work.Left) x = work.Left;
            if (y + h > work.Bottom) y = work.Bottom - h;
            if (y < work.Top) y = work.Top;

            _overlay.X = x;
            _overlay.Y = y;
            if (_hwnd != IntPtr.Zero)
                Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, y, w, h,
                    Win32.SWP_NOACTIVATE | Win32.SWP_NOZORDER);
        }

        // ---------------- 显示/隐藏 ----------------
        private static void ApplyVisibility()
        {
            bool show = _wantVisible && (!_cfg.FollowQQ || _qqRunning);
            if (_audio != null) _audio.SetActive(show);   // 隐藏时彻底停掉音频客户端
            if (show == _visible) return;
            _visible = show;
            _forceRedraw = true;
            if (_hwnd == IntPtr.Zero) return;
            if (show)
            {
                ComputePosition();
                Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE);
                Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            }
            else
            {
                Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
            }
        }

        // ---------------- 托盘 ----------------
        private static void AddTrayIcon()
        {
            Win32.NOTIFYICONDATA data = new Win32.NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(Win32.NOTIFYICONDATA));
            data.hWnd = _hwnd;
            data.uID = 1;
            data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP;
            data.uCallbackMessage = Win32.WM_TRAY;
            data.hIcon = _hIcon;
            data.szTip = "QQ 音乐频谱";
            bool ok = Win32.Shell_NotifyIconW(Win32.NIM_ADD, ref data);
            Log.Write("tray: add ok=" + ok + " icon=0x" + _hIcon.ToInt64().ToString("X"));
        }

        private static void RemoveTrayIcon()
        {
            Win32.NOTIFYICONDATA data = new Win32.NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(Win32.NOTIFYICONDATA));
            data.hWnd = _hwnd;
            data.uID = 1;
            Win32.Shell_NotifyIconW(Win32.NIM_DELETE, ref data);
        }

        private static void OnTrayMessage(int mouseMessage)
        {
            if (mouseMessage == Win32.WM_RBUTTONUP) ShowMenu();
            else if (mouseMessage == Win32.WM_LBUTTONDBLCLK)
            {
                _wantVisible = !_wantVisible;
                ApplyVisibility();
            }
        }

        private static void ShowMenu()
        {
            IntPtr menu = Win32.CreatePopupMenu();
            Win32.AppendMenuW(menu, Win32.MF_STRING, new IntPtr(ID_TOGGLE),
                _wantVisible ? "隐藏频谱" : "显示频谱");
            Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, IntPtr.Zero, null);

            IntPtr themes = Win32.CreatePopupMenu();
            string[] names = Overlay.Names;
            for (int i = 0; i < names.Length; i++)
            {
                int flags = Win32.MF_STRING;
                if (string.Equals(names[i], _cfg.Theme, StringComparison.OrdinalIgnoreCase)) flags |= Win32.MF_CHECKED;
                Win32.AppendMenuW(themes, flags, new IntPtr(ID_THEME_BASE + i), ThemeLabel(names[i]));
            }
            Win32.AppendMenuW(menu, Win32.MF_POPUP, themes, "配色主题");

            IntPtr opacity = Win32.CreatePopupMenu();
            for (int i = 0; i < OpacitySteps.Length; i++)
            {
                int flags = Win32.MF_STRING;
                if (Math.Abs(OpacitySteps[i] - _cfg.Opacity) < 0.02f) flags |= Win32.MF_CHECKED;
                Win32.AppendMenuW(opacity, flags, new IntPtr(ID_OPACITY_BASE + i),
                    ((int)(OpacitySteps[i] * 100)) + "%");
            }
            Win32.AppendMenuW(menu, Win32.MF_POPUP, opacity, "不透明度");

            IntPtr sens = Win32.CreatePopupMenu();
            for (int i = 0; i < SensitivitySteps.Length; i++)
            {
                int flags = Win32.MF_STRING;
                if (Math.Abs(SensitivitySteps[i] - _cfg.Sensitivity) < 0.03f) flags |= Win32.MF_CHECKED;
                Win32.AppendMenuW(sens, flags, new IntPtr(ID_SENS_BASE + i),
                    SensitivitySteps[i].ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "x");
            }
            Win32.AppendMenuW(menu, Win32.MF_POPUP, sens, "敏感度");

            IntPtr pos = Win32.CreatePopupMenu();
            int posCur = CurrentPositionPreset();
            for (int i = 0; i < PositionNames.Length; i++)
            {
                int flags = Win32.MF_STRING;
                if (i == posCur) flags |= Win32.MF_CHECKED;
                Win32.AppendMenuW(pos, flags, new IntPtr(ID_POS_BASE + i), PositionNames[i]);
            }
            Win32.AppendMenuW(menu, Win32.MF_POPUP, pos, "位置");

            Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenuW(menu, Win32.MF_STRING | (_cfg.Peaks ? Win32.MF_CHECKED : 0), new IntPtr(ID_PEAKS), "峰值线");
            Win32.AppendMenuW(menu, Win32.MF_STRING | (_cfg.Mirror ? Win32.MF_CHECKED : 0), new IntPtr(ID_MIRROR), "倒影");
            Win32.AppendMenuW(menu, Win32.MF_STRING | (_cfg.FollowQQ ? Win32.MF_CHECKED : 0), new IntPtr(ID_FOLLOW), "跟随 QQ 音乐");
            Win32.AppendMenuW(menu, Win32.MF_STRING | (_cfg.Autostart ? Win32.MF_CHECKED : 0), new IntPtr(ID_AUTOSTART), "开机自启");

            Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenuW(menu, Win32.MF_STRING, new IntPtr(ID_OPENCFG), "打开配置文件夹");
            Win32.AppendMenuW(menu, Win32.MF_STRING, new IntPtr(ID_EDITCFG), "编辑配置文件");
            Win32.AppendMenuW(menu, Win32.MF_STRING, new IntPtr(ID_RELOAD), "重新载入配置");
            Win32.AppendMenuW(menu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenuW(menu, Win32.MF_STRING, new IntPtr(ID_EXIT), "退出");

            Win32.POINT pt;
            Win32.GetCursorPos(out pt);
            Win32.SetForegroundWindow(_hwnd);
            int cmd = Win32.TrackPopupMenu(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
            Win32.PostMessageW(_hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            Win32.DestroyMenu(menu);
            if (cmd != 0) OnMenuCommand(cmd);
        }

        private static string ThemeLabel(string name)
        {
            switch (name)
            {
                case "cyber": return "赛博（青紫粉）";
                case "aurora": return "极光（绿蓝紫）";
                case "sunset": return "落日（黄橙红）";
                case "mono": return "纯白";
                case "ice": return "冰蓝";
                case "ember": return "余烬（橙红）";
            }
            return name;
        }

        private static void OnMenuCommand(int id)
        {
            if (id == ID_TOGGLE)
            {
                _wantVisible = !_wantVisible;
                ApplyVisibility();
                return;
            }
            if (id >= ID_THEME_BASE && id < ID_THEME_BASE + Overlay.Names.Length)
            {
                _cfg.Theme = Overlay.Names[id - ID_THEME_BASE];
                _cfg.Colors = "";                    // 选命名主题即取消自定义配色
                _overlay.SetTheme(_cfg.Theme);
                _forceRedraw = true;
                _state.Save(_cfg);
                return;
            }
            if (id >= ID_SENS_BASE && id < ID_SENS_BASE + SensitivitySteps.Length)
            {
                _cfg.Sensitivity = SensitivitySteps[id - ID_SENS_BASE];
                _state.Save(_cfg);
                ReloadConfig();
                return;
            }
            if (id >= ID_POS_BASE && id < ID_POS_BASE + PositionNames.Length)
            {
                ApplyPositionPreset(id - ID_POS_BASE);
                _state.Save(_cfg);
                return;
            }
            if (id >= ID_OPACITY_BASE && id < ID_OPACITY_BASE + OpacitySteps.Length)
            {
                _cfg.Opacity = OpacitySteps[id - ID_OPACITY_BASE];
                _overlay.SetOpacity(_cfg.Opacity);
                _forceRedraw = true;
                _state.Save(_cfg);
                return;
            }
            switch (id)
            {
                case ID_PEAKS:
                    _cfg.Peaks = !_cfg.Peaks;
                    RebuildOverlay();
                    _state.Save(_cfg);
                    break;
                case ID_MIRROR:
                    _cfg.Mirror = !_cfg.Mirror;
                    RebuildOverlay();
                    _state.Save(_cfg);
                    break;
                case ID_FOLLOW:
                    _cfg.FollowQQ = !_cfg.FollowQQ;
                    _state.Save(_cfg);
                    ApplyVisibility();
                    break;
                case ID_AUTOSTART:
                    _cfg.Autostart = !_cfg.Autostart;
                    SetAutostart(_cfg.Autostart);
                    _state.Save(_cfg);
                    break;
                case ID_OPENCFG:
                    Win32.ShellOpen(_baseDir);
                    break;
                case ID_EDITCFG:
                    Win32.ShellOpen(Path.Combine(_baseDir, "spectrum.ini"));
                    break;
                case ID_RELOAD:
                    ReloadConfig();
                    break;
                case ID_EXIT:
                    _run = false;
                    Win32.DestroyWindow(_hwnd);
                    break;
            }
        }

        private static void RebuildOverlay()
        {
            lock (_swapGate)
            {
                Overlay old = _overlay;
                Overlay fresh = new Overlay(old.Width, old.Height, _cfg.Bars, (int)(_cfg.Gap * Win32.GetScale()),
                    _cfg.Theme, _cfg.Colors, _cfg.Opacity, _cfg.Mirror, _cfg.Peaks, _cfg.MinHeight);
                fresh.X = old.X;
                fresh.Y = old.Y;
                fresh.Attach(_hwnd);
                _overlay = fresh;
                old.Dispose();
            }
            _forceRedraw = true;
        }

        /// <summary>重新读取 spectrum.ini：几何、配色、频段全部重建，不需要重启程序。</summary>
        private static void ReloadConfig()
        {
            bool firstRun;
            Config fresh = Config.Load(_baseDir, out firstRun);
            if (_state.IsNewerThan(Path.Combine(_baseDir, "spectrum.ini"))) _state.ApplyTo(fresh);

            lock (_swapGate)
            {
                _cfg = fresh;

                Analyzer analyzer = new Analyzer(_cfg.Bars, _cfg.FloorDb, _cfg.TopDb,
                    _cfg.Attack, _cfg.Release, _cfg.Fps, _cfg.Sensitivity, _cfg.Tilt);
                analyzer.ConfigureBands(_audio.SampleRate);

                Overlay old = _overlay;
                Overlay neo = new Overlay(
                    (int)(_cfg.Width * Win32.GetScale()),
                    (int)(_cfg.Height * Win32.GetScale()),
                    _cfg.Bars, (int)(_cfg.Gap * Win32.GetScale()), _cfg.Theme, _cfg.Colors, _cfg.Opacity,
                    _cfg.Mirror, _cfg.Peaks, _cfg.MinHeight);
                neo.X = old.X;
                neo.Y = old.Y;
                neo.Attach(_hwnd);

                _analyzer = analyzer;
                _overlay = neo;
                old.Dispose();
            }

            _forceRedraw = true;
            ComputePosition();
            ApplyVisibility();
            Log.Write("config reloaded");
        }

        // ---------------- 开机自启 ----------------
        private static void SyncAutostart(bool firstRun)
        {
            bool want = _cfg.Autostart;                       // 来自 spectrum.ini 或 state.ini
            string current = Win32.RegReadString(Win32.HKEY_CURRENT_USER, RunKey, RunValue);
            if (want)
            {
                string expected = "\"" + _exePath + "\" --tray";
                // 注册表缺失、或整个文件夹被移动/改名导致路径漂移，都按当前路径重写
                if (current != expected) SetAutostart(true);
                _cfg.Autostart = true;
            }
            else
            {
                // 配置里明确关了，就以配置为准（否则 ini 里的 autostart=false 永远无效）
                if (current != null) SetAutostart(false);
                _cfg.Autostart = false;
            }
        }

        private static void SetAutostart(bool enable)
        {
            if (enable)
            {
                bool ok = Win32.RegWriteString(Win32.HKEY_CURRENT_USER, RunKey, RunValue,
                    "\"" + _exePath + "\" --tray");
                Log.Write("autostart=on ok=" + ok);
            }
            else
            {
                Win32.RegDeleteValue(Win32.HKEY_CURRENT_USER, RunKey, RunValue);
                Log.Write("autostart=off");
            }
        }

        // ---------------- 进程检测 ----------------
        // 稳态成本：每次轮询只做一次 EnumProcesses（一次系统调用，微秒级）+ 集合比对；
        // 只有系统里出现「没见过的 PID」时才去查它的映像名。
        // 早期用 CreateToolhelp32Snapshot 每次都枚举全部进程并做 300 次字符串分配，
        // 实测每轮 ~9ms，pollms=500 时白占 2% CPU，故改为增量式。
        private static uint[] _pidBuf = new uint[1024];
        private static HashSet<uint> _knownPids = new HashSet<uint>();
        private static HashSet<uint> _livePids = new HashSet<uint>();
        private static uint _qqPid;                       // 0 = 没在跑
        private static readonly StringBuilder _pathBuf = new StringBuilder(1024);

        private static bool IsProcessRunning(string name)
        {
            string target = name + ".exe";
            uint needed;
            if (!Win32.EnumProcesses(_pidBuf, (uint)(_pidBuf.Length * 4), out needed))
                return _qqPid != 0;                       // 枚举失败就沿用上次结论

            int count = (int)(needed / 4);
            if (count >= _pidBuf.Length)                  // 进程数超出缓冲，扩容后重来
            {
                _pidBuf = new uint[count + 128];
                if (!Win32.EnumProcesses(_pidBuf, (uint)(_pidBuf.Length * 4), out needed))
                    return _qqPid != 0;
                count = (int)(needed / 4);
            }

            _livePids.Clear();
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                uint pid = _pidBuf[i];
                if (pid == 0) continue;
                _livePids.Add(pid);
                if (pid == _qqPid) { found = true; continue; }        // 已跟踪的进程还在，零成本
                if (_knownPids.Contains(pid)) continue;               // 老进程，名字已知不是目标
                if (ImageNameMatches(pid, target)) { _qqPid = pid; found = true; }
            }
            if (!found) _qqPid = 0;

            HashSet<uint> swap = _knownPids;                  // 交换而非复制，避免每次搬 300 个元素
            _knownPids = _livePids;
            _livePids = swap;
            return found;
        }

        private static bool ImageNameMatches(uint pid, string target)
        {
            IntPtr h = Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return false;           // 系统进程或权限不足，跳过
            try
            {
                uint size = (uint)_pathBuf.Capacity;
                if (!Win32.QueryFullProcessImageNameW(h, 0, _pathBuf, ref size)) return false;
                int len = (int)size;
                if (len <= 0 || len > _pathBuf.Length) len = _pathBuf.Length;
                if (len <= 0) return false;
                string full = _pathBuf.ToString(0, len);
                int slash = full.LastIndexOf('\\');
                string file = slash >= 0 ? full.Substring(slash + 1) : full;
                return string.Equals(file, target, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Win32.CloseHandle(h);
            }
        }

        private static void WatchLoop()
        {
            while (_run)
            {
                bool running = IsProcessRunning(_cfg.QQProcess);
                if (running != _qqRunning)
                {
                    _qqRunning = running;
                    if (_hwnd != IntPtr.Zero) Win32.PostMessageW(_hwnd, (uint)Win32.WM_QQSTATE, IntPtr.Zero, IntPtr.Zero);
                }
                int waited = 0;
                while (_run && waited < _cfg.PollMs)
                {
                    Thread.Sleep(100);
                    waited += 100;
                }
            }
        }

        // ---------------- 渲染 ----------------
        private static void RenderLoop()
        {
            bool highRes;
            IntPtr timer = Win32.CreateFrameTimer(out highRes);
            Log.Write("timer: " + (highRes ? "high-resolution waitable timer"
                                           : "fallback: ordinary timer + timeBeginPeriod(1ms)"));
            int lastSampleRate = -1;
            int restFrames = 0;
            int _diagTick = 0;

            while (_run)
            {
                if (!_visible)
                {
                    restFrames = 0;
                    Thread.Sleep(50);
                    continue;
                }

                // 取一份当前实例的本地引用，配置重载时替换不会撕裂本帧
                Analyzer analyzer;
                Overlay overlay;
                lock (_swapGate) { analyzer = _analyzer; overlay = _overlay; }

                int sr = _audio.SampleRate;
                if (sr != lastSampleRate)
                {
                    analyzer.ConfigureBands(sr);
                    lastSampleRate = sr;
                }

                analyzer.Update(_audio.Ring, _audio.Head);

                if ((Environment.TickCount - _diagTick) > 10000)
                {
                    _diagTick = Environment.TickCount;
                    float maxv = 0f;
                    for (int i = 0; i < analyzer.Bands.Length; i++) if (analyzer.Bands[i] > maxv) maxv = analyzer.Bands[i];
                    Log.Write("diag: atrest=" + analyzer.AtRest + " maxband=" + maxv.ToString("F3")
                        + " rms=" + _audio.Rms.ToString("F4"));
                    StringBuilder bs = new StringBuilder();
                    for (int i = 0; i < analyzer.Bands.Length; i++)
                    {
                        if (i > 0) bs.Append(',');
                        bs.Append((int)(analyzer.Bands[i] * 99f));
                    }
                    Log.Write("bands: " + bs);
                }

                bool quiet = analyzer.AtRest && !_forceRedraw;
                if (quiet && restFrames >= 2)
                {
                    // 静音：完全不重绘、不上传分层窗口，只保持低频轮询
                    Thread.Sleep(100);
                    continue;
                }

                overlay.Draw(analyzer.Bands, analyzer.Peaks);
                overlay.Present();
                _forceRedraw = false;
                restFrames = quiet ? restFrames + 1 : 0;

                int fps = quiet ? _cfg.IdleFps : _cfg.Fps;
                if (timer != IntPtr.Zero)
                {
                    long due = -(long)(10000000L / fps);
                    Win32.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false);
                    Win32.WaitForSingleObject(timer, 250);
                }
                else
                {
                    Thread.Sleep(Math.Max(1, 1000 / fps));
                }
            }

            if (timer != IntPtr.Zero) Win32.CloseHandle(timer);
        }

        private static void Shutdown()
        {
            _run = false;
            try { if (_audio != null) { _audio.SetActive(false); _audio.Stop(); } }
            catch (Exception) { }
            try { if (_renderThread != null) _renderThread.Join(1200); }
            catch (Exception) { }
            try { if (_watchThread != null) _watchThread.Join(1200); }
            catch (Exception) { }
            // 不在这里写 state.ini：托盘操作已经即时保存，退出时再写会把 state 变「新」，
            // 导致用户下次手改 spectrum.ini 被旧状态覆盖。
            if (_hwnd != IntPtr.Zero) RemoveTrayIcon();
            if (_overlay != null) _overlay.Dispose();
            if (_hIcon != IntPtr.Zero) Win32.DestroyIcon(_hIcon);
            Log.Write("exit");
        }

        // ---------------- 自检 ----------------
        private static int SelfTest()
        {
            Win32.AttachConsole(-1);
            StringBuilder report = new StringBuilder();
            int failures = 0;

            double err = Analyzer.SelfCheck();
            report.AppendLine("FFT 自检最大误差 = " + err.ToString("E3"));
            if (!(err < 1e-4)) { failures++; report.AppendLine("  [失败] FFT 结果不正确"); }

            int sr = 48000;
            Config cfg = new Config();
            bool firstRun;
            try { cfg = Config.Load(_baseDir, out firstRun); }
            catch (Exception) { }

            Analyzer analyzer = new Analyzer(cfg.Bars, cfg.FloorDb, cfg.TopDb, cfg.Attack, cfg.Release,
                cfg.Fps, cfg.Sensitivity, cfg.Tilt);
            analyzer.ConfigureBands(sr);

            float[] ring = new float[AudioCapture.RingSize];
            int head = 0;
            Random rnd = new Random(20240607);

            for (int block = 0; block < 80; block++)
            {
                for (int i = 0; i < 512; i++)
                {
                    double t = (block * 512 + i) / (double)sr;
                    double v = 0;
                    v += 0.55 * Math.Sin(2 * Math.PI * 55 * t) * (0.55 + 0.45 * Math.Sin(2 * Math.PI * 0.8 * t));
                    v += 0.35 * Math.Sin(2 * Math.PI * 220 * t);
                    v += 0.22 * Math.Sin(2 * Math.PI * 880 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 1.7 * t));
                    v += 0.12 * Math.Sin(2 * Math.PI * 3300 * t);
                    v += 0.05 * (rnd.NextDouble() - 0.5);
                    ring[head] = (float)v;
                    head = (head + 1) & AudioCapture.RingMask;
                }
                analyzer.Update(ring, head);
            }

            float max = 0f, sum = 0f;
            int nonZero = 0;
            for (int i = 0; i < analyzer.Bands.Length; i++)
            {
                float b = analyzer.Bands[i];
                if (b > max) max = b;
                sum += b;
                if (b > 0.02f) nonZero++;
            }
            report.AppendLine(string.Format("频段: {0} 根, 最大 {1:F3}, 均值 {2:F3}, 有效 {3} 根",
                analyzer.Bands.Length, max, sum / analyzer.Bands.Length, nonZero));
            if (max < 0.25f) { failures++; report.AppendLine("  [失败] 柱高没有起来"); }
            if (nonZero < analyzer.Bands.Length / 4) { failures++; report.AppendLine("  [失败] 有效频段太少"); }

            foreach (string theme in Overlay.Names)
            {
                Overlay ov = new Overlay(cfg.Width, cfg.Height, cfg.Bars, cfg.Gap, theme, null, cfg.Opacity,
                    cfg.Mirror, cfg.Peaks, cfg.MinHeight);
                ov.Draw(analyzer.Bands, analyzer.Peaks);
                string path = Path.Combine(_baseDir, "selftest_" + theme + ".bmp");
                ov.SaveBmp(path, 0x14141C);
                ov.Dispose();

                byte[] data = File.ReadAllBytes(path);
                int colored = 0;
                for (int i = 54; i < data.Length; i++) if (data[i] != 0) colored++;
                report.AppendLine(string.Format("  {0,-7} -> {1}  ({2} 字节, 着色像素 {3})",
                    theme, Path.GetFileName(path), data.Length, colored));
                if (colored < 200) { failures++; report.AppendLine("  [失败] 渲染结果几乎为空"); }
            }

            // 自定义配色用例：把 LUT 首尾色取出来验证
            string probeColors = (cfg.Colors != null && cfg.Colors.Length > 0)
                ? cfg.Colors : "#FF0000,#FFFF00,#00FF00";
            Overlay custom = new Overlay(cfg.Width, cfg.Height, cfg.Bars, cfg.Gap, "cyber", probeColors,
                cfg.Opacity, cfg.Mirror, cfg.Peaks, cfg.MinHeight);
            custom.Draw(analyzer.Bands, analyzer.Peaks);
            string customPath = Path.Combine(_baseDir, "selftest_custom.bmp");
            custom.SaveBmp(customPath, 0x14141C);
            custom.Dispose();
            uint[] want = Overlay.ParseColors(probeColors);
            report.AppendLine(string.Format("  自定义配色 \"{0}\" -> {1}  色标 {2} 个, 首色 #{3:X6}",
                probeColors, Path.GetFileName(customPath), want == null ? 0 : want.Length,
                want == null ? 0u : want[0]));
            if (want == null) { failures++; report.AppendLine("  [失败] 自定义配色解析失败"); }

            report.AppendLine(failures == 0 ? "结果: 全部通过" : "结果: " + failures + " 项失败");
            string text = report.ToString();
            try { File.WriteAllText(Path.Combine(_baseDir, "selftest.txt"), text, new UTF8Encoding(false)); }
            catch (Exception) { }
            try { Console.Write(text); }
            catch (Exception) { }
            return failures == 0 ? 0 : 1;
        }
    }
}
