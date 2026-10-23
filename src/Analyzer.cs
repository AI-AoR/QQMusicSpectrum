// 频谱分析：2048 点实数 FFT（复数实现）+ 对数频段映射 + 攻击/释放平滑 + 峰值保持。
// 全部使用预分配数组，稳态下零 GC 分配。
using System;

namespace QQMSpectrum
{
    internal sealed class Analyzer
    {
        public const int FftSize = 2048;
        private const int Bins = FftSize / 2;

        public readonly float[] Bands;   // 0..1 平滑后的柱高
        public readonly float[] Peaks;   // 0..1 峰值保持
        public bool AtRest { get; private set; }

        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private readonly float[] _hann = new float[FftSize];
        private readonly float[] _mag = new float[Bins];
        private readonly int[] _binLo;
        private readonly int[] _binHi;
        private readonly float[] _tiltDb;   // 每个频段的频率倾斜补偿（dB）
        private readonly float _tilt;
        private readonly float _floorDb;
        private readonly float _topDb;
        private readonly float _attack;
        private readonly float _release;
        private readonly float _peakFall;
        private readonly float _gain;
        private int _sampleRate = -1;

        public Analyzer(int bars, float floorDb, float topDb, float attack, float release, int fps,
            float sensitivity, float tilt)
        {
            Bands = new float[bars];
            Peaks = new float[bars];
            _binLo = new int[bars];
            _binHi = new int[bars];
            _tiltDb = new float[bars];
            _floorDb = floorDb;
            _topDb = topDb;
            _attack = attack;
            _release = release;
            _peakFall = Math.Max(0.0015f, 1.1f / Math.Max(1, fps));
            _gain = sensitivity;
            _tilt = tilt;

            for (int i = 0; i < FftSize; i++)
                _hann[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (FftSize - 1)));
        }

        /// <summary>采样率变化时重算每个频段对应的 FFT bin 区间。</summary>
        public void ConfigureBands(int sampleRate)
        {
            if (sampleRate == _sampleRate || sampleRate <= 0) return;
            _sampleRate = sampleRate;
            double binHz = (double)sampleRate / FftSize;
            double fLow = 35.0;
            double fHigh = Math.Min(16000.0, sampleRate * 0.47);
            if (fHigh <= fLow * 2) fHigh = fLow * 4;

            int bars = _binLo.Length;
            double ratio = Math.Log(fHigh / fLow);
            for (int i = 0; i < bars; i++)
            {
                double f0 = fLow * Math.Exp(ratio * i / bars);
                double f1 = fLow * Math.Exp(ratio * (i + 1) / bars);
                int b0 = (int)Math.Round(f0 / binHz);
                int b1 = (int)Math.Round(f1 / binHz);
                if (b0 < 1) b0 = 1;
                if (b1 <= b0) b1 = b0 + 1;
                if (b1 > Bins - 1) b1 = Bins - 1;
                if (b0 > Bins - 2) b0 = Bins - 2;
                _binLo[i] = b0;
                _binHi[i] = b1;

                // 频率倾斜补偿：以 1kHz 为基准，每个八度加减 _tilt dB。
                // 音乐（尤其流行/摇滚）的能量天然左高右低，粉红噪声约 -3dB/八度，
                // 不做补偿的话低频段会长期顶格、高频段几乎不动。
                double fc = Math.Sqrt(f0 * f1);
                _tiltDb[i] = _tilt * (float)(Math.Log(fc / 1000.0) / Math.Log(2.0));
            }
        }

        public void Update(float[] ring, int head)
        {
            // 取最近 FftSize 个采样并加窗
            int start = (head - FftSize) & AudioCapture.RingMask;
            for (int i = 0; i < FftSize; i++)
            {
                _re[i] = ring[(start + i) & AudioCapture.RingMask] * _hann[i];
                _im[i] = 0f;
            }

            Fft(_re, _im, FftSize);

            // 归一化幅度：满幅正弦 -> 1.0
            float scale = 4.0f / FftSize;
            for (int k = 1; k < Bins; k++)
            {
                float r = _re[k], im = _im[k];
                _mag[k] = (float)Math.Sqrt(r * r + im * im) * scale;
            }

            float span = _topDb - _floorDb;
            if (span < 1f) span = 1f;
            bool rest = true;

            for (int b = 0; b < Bands.Length; b++)
            {
                float peak = 0f;
                int lo = _binLo[b], hi = _binHi[b];
                for (int k = lo; k <= hi; k++)
                {
                    float m = _mag[k];
                    if (m > peak) peak = m;
                }

                float db = 20f * (float)Math.Log10(peak + 1e-9f) + _tiltDb[b];
                float target = (db - _floorDb) / span * _gain;
                if (target < 0f) target = 0f;
                if (target > 1f) target = 1f;

                float v = Bands[b];
                v += (target - v) * (target > v ? _attack : _release);
                if (v < 0.0015f) v = 0f;
                Bands[b] = v;

                if (v > Peaks[b]) Peaks[b] = v;
                else
                {
                    Peaks[b] -= _peakFall;
                    if (Peaks[b] < v) Peaks[b] = v;
                }

                if (v > 0.02f) rest = false;
            }

            AtRest = rest;
        }

        private static void Fft(float[] re, float[] im, int n)
        {
            // 位反转置换
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }

            // 迭代蝶形
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                float wlR = (float)Math.Cos(ang);
                float wlI = (float)Math.Sin(ang);
                int half = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    float wr = 1f, wi = 0f;
                    for (int j = 0; j < half; j++)
                    {
                        int a = i + j;
                        int b = a + half;
                        float vr = re[b] * wr - im[b] * wi;
                        float vi = re[b] * wi + im[b] * wr;
                        float ur = re[a], ui = im[a];
                        re[a] = ur + vr; im[a] = ui + vi;
                        re[b] = ur - vr; im[b] = ui - vi;
                        float nwr = wr * wlR - wi * wlI;
                        wi = wr * wlI + wi * wlR;
                        wr = nwr;
                    }
                }
            }
        }

        /// <summary>自检：用直接 DFT 校验 FFT 正确性。</summary>
        public static double SelfCheck()
        {
            int n = 64;
            float[] re = new float[n];
            float[] im = new float[n];
            float[] r2 = new float[n];
            float[] i2 = new float[n];
            Random rnd = new Random(12345);
            for (int i = 0; i < n; i++)
            {
                float v = (float)(rnd.NextDouble() * 2 - 1);
                re[i] = v; r2[i] = v;
            }

            Fft(re, im, n);

            double worst = 0;
            for (int k = 0; k < n; k++)
            {
                double sr = 0, si = 0;
                for (int t = 0; t < n; t++)
                {
                    double a = -2.0 * Math.PI * k * t / n;
                    sr += r2[t] * Math.Cos(a);
                    si += r2[t] * Math.Sin(a);
                }
                double dr = sr - re[k];
                double di = si - im[k];
                double err = Math.Sqrt(dr * dr + di * di);
                if (err > worst) worst = err;
            }
            return worst;
        }
    }
}
