"""Generate a test WAV: many tones + a beat envelope so the spectrum shows varied bars."""
import math
import struct
import wave

RATE = 44100
SECONDS = 6.0
N = int(RATE * SECONDS)

# Spread tones across the audible range so most bands light up.
freqs = [55, 82, 110, 165, 220, 330, 440, 660, 880, 1320, 1760, 2640,
         3520, 5280, 7040, 10560]
amps = [1.0, 0.9, 0.85, 0.8, 0.75, 0.7, 0.65, 0.6, 0.55, 0.5, 0.45, 0.4,
        0.35, 0.3, 0.25, 0.2]

samples = []
for i in range(N):
    t = i / RATE
    # 120 BPM beat envelope, sharp attack so the bars visibly punch
    beat = (t * 2.0) % 1.0
    env = math.exp(-4.0 * beat)
    s = 0.0
    for f, a in zip(freqs, amps):
        s += a * math.sin(2.0 * math.pi * f * t)
    s = s / len(freqs) * env * 1.6
    # slow wobble on the high end so the shape keeps moving
    s *= 0.75 + 0.25 * math.sin(2.0 * math.pi * 0.7 * t)
    # keep the test signal quiet: it goes out of the speakers during the run
    s *= 0.06
    samples.append(max(-1.0, min(1.0, s)))

with wave.open("test_tone.wav", "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(RATE)
    w.writeframes(b"".join(struct.pack("<h", int(x * 32000)) for x in samples))
print("wrote test_tone.wav", N, "frames")
