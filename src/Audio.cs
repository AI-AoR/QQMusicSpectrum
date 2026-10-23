// WASAPI 环回采集（loopback）：直接抓取系统默认播放设备的混音输出，
// 不需要虚拟声卡、不需要管理员权限，QQ 音乐播放什么就分析什么。
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace QQMSpectrum
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, [MarshalAs(UnmanagedType.Interface)] out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Interface)] out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
            [MarshalAs(UnmanagedType.Interface)] out IAudioClient client);
        [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    internal interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity,
            IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out int frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out int padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAudioCaptureClient service);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    internal interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out int flags,
            out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(int frames);
        [PreserveSig] int GetNextPacketSize(out int frames);
    }

    internal sealed class AudioCapture
    {
        // 环形缓冲（2 的幂，掩码取模），写入为单线程，读取由分析线程完成
        public const int RingSize = 16384;
        public const int RingMask = RingSize - 1;
        public readonly float[] Ring = new float[RingSize];
        public volatile int Head;

        public volatile float Rms;           // 平滑后的电平，用于自适应帧率
        public volatile int SampleRate = 48000;
        public volatile int Channels = 2;
        public volatile bool FormatOk;
        public volatile string LastError = "";

        private Thread _thread;
        private volatile bool _run;
        private volatile bool _active;
        private IntPtr _wake;
        private IntPtr _audioEvent = IntPtr.Zero;
        private IAudioClient _client;
        private IAudioCaptureClient _capture;
        private bool _formatFloat;
        private int _bitsPerSample;

        public void Start()
        {
            if (_thread != null) return;
            _run = true;
            _wake = Win32.CreateEventW(IntPtr.Zero, false, false, null);
            _thread = new Thread(ThreadProc);
            _thread.IsBackground = true;
            _thread.Priority = ThreadPriority.BelowNormal;
            _thread.Name = "audio-capture";
            _thread.Start();
        }

        public void Stop()
        {
            _run = false;
            if (_wake != IntPtr.Zero) SetEvent(_wake);
            if (_thread != null) { _thread.Join(1000); _thread = null; }
        }

        /// <summary>窗口可见时才开始采集，隐藏时彻底停掉音频客户端，把占用压到 0。</summary>
        public void SetActive(bool active)
        {
            if (_active == active) return;
            _active = active;
            if (_wake != IntPtr.Zero) SetEvent(_wake);
        }

        public void Wake()
        {
            if (_wake != IntPtr.Zero) SetEvent(_wake);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetEvent(IntPtr handle);

        private void ThreadProc()
        {
            int stall = 0;   // 连续超时计数：设备被拔出/切换后事件不再触发，靠它触发重连
            while (_run)
            {
                if (!_active)
                {
                    StopClient();
                    stall = 0;
                    WaitForSingleObjectSafe(_wake, 500);
                    continue;
                }

                if (_client == null && !StartClient())
                {
                    WaitForSingleObjectSafe(_wake, 1500);
                    continue;
                }

                uint r = WaitForSingleObjectSafe(_audioEvent, 100);
                if (r == 0)
                {
                    stall = 0;
                    Drain();
                }
                else if (r == 0xFFFFFFFF)
                {
                    StopClient();   // 事件句柄失效，重建
                }
                else if (++stall >= 30)
                {
                    // 3 秒没有任何数据包：默认设备可能已经变化，丢弃客户端下轮重连
                    Log.Write("audio: stream stalled, reconnecting");
                    StopClient();
                    stall = 0;
                }
            }
            StopClient();
            if (_audioEvent != IntPtr.Zero) { Win32.CloseHandle(_audioEvent); _audioEvent = IntPtr.Zero; }
        }

        private static uint WaitForSingleObjectSafe(IntPtr h, uint ms)
        {
            if (h == IntPtr.Zero) { Win32.Sleep(ms); return 0x102; }
            return Win32.WaitForSingleObject(h, ms);
        }

        private bool StartClient()
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice device;
                int hr = enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out device);
                if (hr != 0 || device == null) { Fail("GetDefaultAudioEndpoint", hr); return false; }

                Guid iid = typeof(IAudioClient).GUID;
                IAudioClient client;
                hr = device.Activate(ref iid, 0x17 /*CLSCTX_ALL*/, IntPtr.Zero, out client);
                if (hr != 0 || client == null) { Fail("Activate", hr); return false; }

                IntPtr fmtPtr;
                hr = client.GetMixFormat(out fmtPtr);
                if (hr != 0 || fmtPtr == IntPtr.Zero) { Fail("GetMixFormat", hr); return false; }

                var fmt = (Win32.WAVEFORMATEX)Marshal.PtrToStructure(fmtPtr, typeof(Win32.WAVEFORMATEX));
                int tag = fmt.wFormatTag;
                int bits = fmt.wBitsPerSample;
                if (tag == 0xFFFE) // WAVE_FORMAT_EXTENSIBLE：从 SubFormat 判断真正的采样类型
                {
                    int sub = Marshal.ReadInt32(fmtPtr, 24);
                    tag = sub == 3 ? 3 : 1;
                }
                _formatFloat = tag == 3;
                _bitsPerSample = bits;
                SampleRate = (int)fmt.nSamplesPerSec;
                Channels = fmt.nChannels;

                _audioEvent = Win32.CreateEventW(IntPtr.Zero, false, false, null);

                // 20ms 缓冲 + 事件驱动，兼顾低延迟与低唤醒频率
                hr = client.Initialize(0 /*AUDCLNT_SHAREMODE_SHARED*/,
                    0x00020000 | 0x00040000 /*LOOPBACK | EVENTCALLBACK*/,
                    200000, 0, fmtPtr, IntPtr.Zero);
                Marshal.FreeCoTaskMem(fmtPtr);
                if (hr != 0) { Fail("Initialize", hr); return false; }

                hr = client.SetEventHandle(_audioEvent);
                if (hr != 0) { Fail("SetEventHandle", hr); return false; }

                Guid capIid = typeof(IAudioCaptureClient).GUID;
                IAudioCaptureClient capture;
                hr = client.GetService(ref capIid, out capture);
                if (hr != 0 || capture == null) { Fail("GetService", hr); return false; }

                hr = client.Start();
                if (hr != 0) { Fail("Start", hr); return false; }

                _client = client;
                _capture = capture;
                FormatOk = true;
                LastError = "";
                Log.Write("audio: started " + SampleRate + "Hz ch=" + Channels +
                          " bits=" + bits + ( _formatFloat ? " float" : " pcm"));
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Write("audio: exception " + ex.Message);
                StopClient();
                return false;
            }
        }

        private void Fail(string where, int hr)
        {
            LastError = where + " hr=0x" + hr.ToString("X8");
            Log.Write("audio: " + LastError);
            StopClient();
        }

        private void StopClient()
        {
            try { if (_client != null) _client.Stop(); }
            catch (Exception) { }
            _client = null;
            _capture = null;
            FormatOk = false;
            Rms = 0f;
        }

        private void Drain()
        {
            try
            {
                while (true)
                {
                    int packet;
                    int hrNext = _capture.GetNextPacketSize(out packet);
                    if (hrNext != 0) { Fail("GetNextPacketSize", hrNext); return; }
                    if (packet <= 0) break;

                    IntPtr data; int frames; int flags; ulong devPos; ulong qpcPos;
                    int hr = _capture.GetBuffer(out data, out frames, out flags, out devPos, out qpcPos);
                    if (hr != 0) break;
                    if (frames > 0)
                    {
                        if ((flags & 2) != 0 || data == IntPtr.Zero) WriteSilence(frames);
                        else Convert(data, frames);
                    }
                    _capture.ReleaseBuffer(frames);
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Write("audio: drain " + ex.Message);
                StopClient();
            }
        }

        private void WriteSilence(int frames)
        {
            int head = Head;
            for (int i = 0; i < frames; i++)
            {
                Ring[head] = 0f;
                head = (head + 1) & RingMask;
            }
            Head = head;
            Rms = Rms * 0.7f;
        }

        private unsafe void Convert(IntPtr data, int frames)
        {
            int ch = Channels;
            if (ch < 1) ch = 1;
            float[] ring = Ring;
            int head = Head;
            double sum = 0;
            byte* p = (byte*)data;

            if (_formatFloat)
            {
                float* f = (float*)data;
                for (int i = 0; i < frames; i++)
                {
                    float acc = 0f;
                    int baseIdx = i * ch;
                    for (int c = 0; c < ch; c++) acc += f[baseIdx + c];
                    float s = acc / ch;
                    ring[head] = s;
                    head = (head + 1) & RingMask;
                    sum += (double)s * s;
                }
            }
            else if (_bitsPerSample == 16)
            {
                short* s16 = (short*)data;
                for (int i = 0; i < frames; i++)
                {
                    float acc = 0f;
                    int baseIdx = i * ch;
                    for (int c = 0; c < ch; c++) acc += s16[baseIdx + c];
                    float s = acc / (ch * 32768f);
                    ring[head] = s;
                    head = (head + 1) & RingMask;
                    sum += (double)s * s;
                }
            }
            else if (_bitsPerSample == 24)
            {
                int stride = ch * 3;
                for (int i = 0; i < frames; i++)
                {
                    float acc = 0f;
                    for (int c = 0; c < ch; c++)
                    {
                        byte* q = p + i * stride + c * 3;
                        int v = q[0] | (q[1] << 8) | (q[2] << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                        acc += v / 8388608f;
                    }
                    float s = acc / ch;
                    ring[head] = s;
                    head = (head + 1) & RingMask;
                    sum += (double)s * s;
                }
            }
            else // 32 位整型
            {
                int* s32 = (int*)data;
                for (int i = 0; i < frames; i++)
                {
                    float acc = 0f;
                    int baseIdx = i * ch;
                    for (int c = 0; c < ch; c++) acc += s32[baseIdx + c];
                    float s = acc / (ch * 2147483648f);
                    ring[head] = s;
                    head = (head + 1) & RingMask;
                    sum += (double)s * s;
                }
            }

            Head = head;
            if (frames > 0)
            {
                float blockRms = (float)Math.Sqrt(sum / frames);
                Rms = Rms * 0.75f + blockRms * 0.25f;
            }
        }
    }
}
