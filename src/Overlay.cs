// 分层窗口渲染：32bpp 预乘 alpha DIB，逐像素直接写入（无 GDI+ / 无 WPF），
// 每帧仅一次 UpdateLayeredWindow。
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace QQMSpectrum
{
    internal sealed class Overlay
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int Bars;
        public int X;
        public int Y;

        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _memDc = IntPtr.Zero;
        private IntPtr _dib = IntPtr.Zero;
        private IntPtr _bits = IntPtr.Zero;
        private IntPtr _oldBmp = IntPtr.Zero;

        private readonly uint[] _lut = new uint[256];
        private readonly float _gap;
        private readonly float _slot;
        private readonly float _barW;
        private readonly float _baseY;
        private readonly float _maxH;
        private readonly float _minH;
        private readonly float _reflH;
        private readonly bool _mirror;
        private readonly bool _showPeaks;
        private float _opacity;
        private string _theme;
        private string _colors;

        public Overlay(int width, int height, int bars, int gap, string theme, string colors, float opacity,
            bool mirror, bool peaks, int minHeight)
        {
            Width = width;
            Height = height;
            Bars = bars;
            _gap = gap;
            _slot = (float)width / bars;
            _barW = Math.Max(1.5f, _slot - gap);
            _reflH = Math.Max(6f, height * 0.09f);
            _baseY = height - _reflH;
            _maxH = _baseY - Math.Max(3f, height * 0.05f);
            _minH = Math.Max(1, minHeight);
            _mirror = mirror;
            _showPeaks = peaks;
            _theme = theme;
            _colors = colors;
            _opacity = opacity;
            BuildLut();
            CreateSurface();
        }

        public void Attach(IntPtr hwnd) { _hwnd = hwnd; }

        public void SetTheme(string theme) { _theme = theme; _colors = null; BuildLut(); }
        public void SetColors(string colors) { _colors = colors; BuildLut(); }
        public void SetOpacity(float opacity) { _opacity = opacity; BuildLut(); }
        public string Theme { get { return _theme; } }

        // ---------------- 表面 ----------------
        private void CreateSurface()
        {
            Win32.BITMAPINFO bmi = new Win32.BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(Win32.BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = Width;
            bmi.bmiHeader.biHeight = -Height; // 自上而下
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            _memDc = Win32.CreateCompatibleDC(IntPtr.Zero);
            _dib = Win32.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib != IntPtr.Zero) _oldBmp = Win32.SelectObject(_memDc, _dib);
        }

        public void Dispose()
        {
            if (_memDc != IntPtr.Zero && _oldBmp != IntPtr.Zero) Win32.SelectObject(_memDc, _oldBmp);
            if (_dib != IntPtr.Zero) Win32.DeleteObject(_dib);
            if (_memDc != IntPtr.Zero) Win32.DeleteDC(_memDc);
            _dib = IntPtr.Zero; _memDc = IntPtr.Zero; _bits = IntPtr.Zero;
        }

        // ---------------- 配色 ----------------
        private static readonly string[] ThemeNames = { "cyber", "aurora", "sunset", "mono", "ice", "ember" };
        private static readonly uint[][] ThemeColors =
        {
            new uint[] { 0x00E5FF, 0x7C4DFF, 0xFF3D9A }, // cyber
            new uint[] { 0x00FFA3, 0x00D2FF, 0x6A5CFF }, // aurora
            new uint[] { 0xFFC400, 0xFF6B35, 0xFF2D95 }, // sunset
            new uint[] { 0xFFFFFF, 0xE0E0E0, 0xFFFFFF }, // mono
            new uint[] { 0x7FE7FF, 0x3D8BFF, 0x2E5BFF }, // ice
            new uint[] { 0xFFD166, 0xFF6B35, 0xD7263D }, // ember
        };

        public static string[] Names { get { return ThemeNames; } }

        private uint[] PickTheme(string name)
        {
            for (int i = 0; i < ThemeNames.Length; i++)
                if (string.Equals(ThemeNames[i], name, StringComparison.OrdinalIgnoreCase))
                    return ThemeColors[i];
            return ThemeColors[0];
        }

        /// <summary>解析 "#RRGGBB,#RRGGBB,..." 形式的自定义配色；无效返回 null。</summary>
        public static uint[] ParseColors(string spec)
        {
            if (spec == null) return null;
            spec = spec.Trim();
            if (spec.Length == 0) return null;
            string[] parts = spec.Split(',');
            if (parts.Length < 2 || parts.Length > 8) return null;

            uint[] stops = new uint[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
                if (s.Length != 6) return null;
                uint v = 0;
                for (int k = 0; k < 6; k++)
                {
                    int d = HexDigit(s[k]);
                    if (d < 0) return null;
                    v = (v << 4) | (uint)d;
                }
                stops[i] = v;
            }
            return stops;
        }

        private static int HexDigit(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

        private void BuildLut()
        {
            uint[] stops = ParseColors(_colors);
            if (stops == null) stops = PickTheme(_theme);
            float alpha = _opacity * 255f;
            if (alpha < 0f) alpha = 0f;
            if (alpha > 255f) alpha = 255f;

            int segs = stops.Length - 1;
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                float pos = t * segs;
                int seg = (int)pos;
                if (seg > segs - 1) seg = segs - 1;
                float f = pos - seg;
                uint c0 = stops[seg], c1 = stops[seg + 1];
                float r0 = (c0 >> 16) & 0xFF, g0 = (c0 >> 8) & 0xFF, b0 = c0 & 0xFF;
                float r1 = (c1 >> 16) & 0xFF, g1 = (c1 >> 8) & 0xFF, b1 = c1 & 0xFF;
                float r = r0 + (r1 - r0) * f;
                float g = g0 + (g1 - g0) * f;
                float b = b0 + (b1 - b0) * f;

                uint a = (uint)alpha;
                uint pr = (uint)(r * alpha / 255f);
                uint pg = (uint)(g * alpha / 255f);
                uint pb = (uint)(b * alpha / 255f);
                _lut[i] = (a << 24) | (pr << 16) | (pg << 8) | pb;
            }
        }

        // ---------------- 绘制 ----------------
        public unsafe void Draw(float[] v, float[] peaks)
        {
            if (_bits == IntPtr.Zero) return;
            Win32.MemSet(_bits, 0, (IntPtr)(Width * Height * 4));
            uint* px = (uint*)_bits;
            int w = Width;
            int baseRow = (int)_baseY;

            for (int b = 0; b < Bars; b++)
            {
                float hv = v[b];
                if (hv < 0f) hv = 0f; else if (hv > 1f) hv = 1f;
                float h = hv * _maxH;
                if (h < _minH) h = _minH;

                float top = _baseY - h;
                float left = b * _slot + _gap * 0.5f;
                float right = left + _barW;
                float r = _barW * 0.5f;
                if (r > h * 0.5f) r = h * 0.5f;

                int y0 = (int)Math.Floor(top);
                if (y0 < 0) y0 = 0;
                int yEnd = baseRow;
                if (yEnd > Height) yEnd = Height;

                for (int y = y0; y < yEnd; y++)
                {
                    float t = (_baseY - y) / _maxH;
                    if (t > 1f) t = 1f; else if (t < 0f) t = 0f;
                    uint c = _lut[(int)(t * 255f)];

                    float inset = 0f;
                    float dy = (top + r) - (y + 0.5f);
                    if (dy > 0f)
                        inset = dy >= r ? r : r - (float)Math.Sqrt(r * r - dy * dy);

                    int xa = (int)Math.Ceiling(left + inset);
                    int xb = (int)Math.Floor(right - inset);
                    if (xa < 0) xa = 0;
                    if (xb >= w) xb = w - 1;
                    if (xb < xa) continue;

                    if (y == y0)
                    {
                        float cov = 1f - (top - y0);
                        if (cov < 0f) cov = 0f; else if (cov > 1f) cov = 1f;
                        c = Scale(c, cov);
                    }

                    uint* row = px + y * w;
                    for (int x = xa; x <= xb; x++) row[x] = c;
                }

                if (_showPeaks)
                {
                    float ph = peaks[b];
                    if (ph > 0.03f)
                    {
                        float ptop = _baseY - ph * _maxH;
                        int py = (int)Math.Round(ptop);
                        if (py < 1) py = 1;
                        float left2 = left + 0.5f;
                        float right2 = right - 0.5f;
                        int xa2 = (int)Math.Ceiling(left2);
                        int xb2 = (int)Math.Floor(right2);
                        for (int dy2 = 0; dy2 < 2; dy2++)
                        {
                            int y = py - dy2;
                            if (y < 0 || y >= baseRow) continue;
                            uint* row = px + y * w;
                            for (int x = xa2; x <= xb2; x++)
                                if (x >= 0 && x < w) row[x] = 0xE6FFFFFF;
                        }
                    }
                }
            }

            // 倒影：把基线以上的像素镜像到下方并快速衰减（预乘 alpha 直接右移缩放）
            if (_mirror)
            {
                int reflH = (int)_reflH;
                for (int y = 0; y < reflH; y++)
                {
                    int srcY = baseRow - 1 - y;
                    int dstY = baseRow + y;
                    if (srcY < 0 || dstY >= Height) break;
                    uint* src = px + srcY * w;
                    uint* dst = px + dstY * w;
                    float fade = 1f - (y + 1) / (float)reflH;
                    int f8 = (int)(0.30f * fade * 256f);
                    if (f8 <= 0) continue;
                    for (int x = 0; x < w; x++)
                    {
                        uint c = src[x];
                        if (c == 0) continue;
                        uint a = (((c >> 24) & 0xFF) * (uint)f8) >> 8;
                        uint r = (((c >> 16) & 0xFF) * (uint)f8) >> 8;
                        uint g = (((c >> 8) & 0xFF) * (uint)f8) >> 8;
                        uint b2 = ((c & 0xFF) * (uint)f8) >> 8;
                        dst[x] = (a << 24) | (r << 16) | (g << 8) | b2;
                    }
                }
            }
        }

        private static uint Scale(uint c, float k)
        {
            uint a = (uint)(((c >> 24) & 0xFF) * k);
            uint r = (uint)(((c >> 16) & 0xFF) * k);
            uint g = (uint)(((c >> 8) & 0xFF) * k);
            uint b = (uint)((c & 0xFF) * k);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        public void Present()
        {
            if (_hwnd == IntPtr.Zero || _memDc == IntPtr.Zero) return;
            IntPtr screenDc = Win32.GetDC(IntPtr.Zero);
            Win32.POINT dst = new Win32.POINT(X, Y);
            Win32.SIZE size = new Win32.SIZE(Width, Height);
            Win32.POINT src = new Win32.POINT(0, 0);
            Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION();
            blend.BlendOp = Win32.AC_SRC_OVER;
            blend.BlendFlags = 0;
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = Win32.AC_SRC_ALPHA;
            Win32.UpdateLayeredWindow(_hwnd, screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
            Win32.ReleaseDC(IntPtr.Zero, screenDc);
        }

        // ---------------- 自检输出 ----------------
        public unsafe void SaveBmp(string path, uint background)
        {
            int w = Width, h = Height;
            int stride = (w * 3 + 3) & ~3;
            int size = 54 + stride * h;
            byte[] buf = new byte[size];
            buf[0] = (byte)'B'; buf[1] = (byte)'M';
            PutInt(buf, 2, size);
            PutInt(buf, 10, 54);
            PutInt(buf, 14, 40);
            PutInt(buf, 18, w);
            PutInt(buf, 22, h);
            PutShort(buf, 26, 1);
            PutShort(buf, 28, 24);
            PutInt(buf, 34, stride * h);

            uint bgR = (background >> 16) & 0xFF, bgG = (background >> 8) & 0xFF, bgB = background & 0xFF;
            uint* px = (uint*)_bits;
            for (int y = 0; y < h; y++)
            {
                int rowOff = 54 + (h - 1 - y) * stride;
                for (int x = 0; x < w; x++)
                {
                    uint c = px[y * w + x];
                    uint a = (c >> 24) & 0xFF;
                    uint inv = 255 - a;
                    uint r = ((c >> 16) & 0xFF) + bgR * inv / 255;
                    uint g = ((c >> 8) & 0xFF) + bgG * inv / 255;
                    uint b = (c & 0xFF) + bgB * inv / 255;
                    int o = rowOff + x * 3;
                    buf[o] = (byte)b; buf[o + 1] = (byte)g; buf[o + 2] = (byte)r;
                }
            }
            File.WriteAllBytes(path, buf);
        }

        private static void PutInt(byte[] b, int off, int v)
        {
            b[off] = (byte)v; b[off + 1] = (byte)(v >> 8); b[off + 2] = (byte)(v >> 16); b[off + 3] = (byte)(v >> 24);
        }

        private static void PutShort(byte[] b, int off, int v)
        {
            b[off] = (byte)v; b[off + 1] = (byte)(v >> 8);
        }
    }
}
