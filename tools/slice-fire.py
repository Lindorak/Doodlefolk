# Cut two CC0 fire recordings into (1) individual crackle/pop grains and (2) a soft roar bed with the crackles
# filtered out, for Doodlefolk's fire voice. Sources (both CC0, OpenGameArt):
#   fire-1.wav  "Fire Crackling" by AntumDeluge   https://opengameart.org/content/fire-crackling
#   fire.wav    "Fireplace Sound loop" by PagDev  https://opengameart.org/content/fireplace-sound-loop
import json, wave, numpy as np
from scipy import signal
from scipy.io import wavfile

OUT = r'C:\src\stickfight\Sounds'
RATE = 44100

def load(path):
    sr, d = wavfile.read(path)
    d = d.astype(np.float64)
    if d.ndim > 1: d = d.mean(axis=1)
    if np.issubdtype(wavfile.read(path)[1].dtype, np.integer):
        d /= float(np.iinfo(wavfile.read(path)[1].dtype).max)
    assert sr == RATE, sr
    return d - d.mean()

def grains(d, src, max_n):
    hp = signal.sosfilt(signal.butter(4, 1500, 'hp', fs=RATE, output='sos'), d)
    env = signal.sosfilt(signal.butter(2, 400, 'lp', fs=RATE, output='sos'), np.abs(hp))
    thr = np.median(env) * 4.0
    peaks, props = signal.find_peaks(env, height=thr, distance=int(0.045 * RATE))
    out = []
    for p in peaks:
        pk = env[p]
        start = max(0, p - int(0.004 * RATE))
        end = p
        while end < len(env) - 1 and env[end] > pk * 0.12 and end - p < int(0.18 * RATE): end += 1
        n = end - start
        if n < int(0.025 * RATE): n = int(0.025 * RATE)
        g = hp[start:start + n].copy() * 0.65 + d[start:start + n] * 0.35   # mostly the snap, a little body
        if len(g) < 64: continue
        fi, fo = int(0.002 * RATE), min(int(0.02 * RATE), len(g) // 2)
        g[:fi] *= np.linspace(0, 1, fi); g[-fo:] *= np.linspace(1, 0, fo)
        level = float(np.abs(g).max())
        if level <= 1e-5: continue
        out.append((g / level, level, src))
    # Keep a spread of loudness: sort by level and take evenly.
    out.sort(key=lambda x: x[1])
    if len(out) > max_n:
        idx = np.linspace(0, len(out) - 1, max_n).round().astype(int)
        out = [out[i] for i in idx]
    return out

a = load(r'C:\tmp\df\fire\fire-1.wav')
b = load(r'C:\tmp\df\fire\fire.wav')
gs = grains(a, 'fire-1', 40) + grains(b, 'fire', 70)
top = max(g[1] for g in gs)
index, chunks, o = [], [], 0
for g, lvl, src in gs:
    chunks.append(g)
    index.append({'o': o, 'n': len(g), 'lvl': round(lvl / top, 4)})
    o += len(g)
allg = np.concatenate(chunks)
wavfile.write(OUT + r'\fire-grains.wav', RATE, (np.clip(allg, -1, 1) * 32000).astype(np.int16))
json.dump(index, open(OUT + r'\fire-grains.json', 'w'))

# The bed: the long recording, low-passed (the roar and hiss of the flames, not the pops), soft-limited, 14 s,
# made seamless, at 22.05 kHz (nothing up there anyway).
bed = signal.sosfiltfilt(signal.butter(4, 1100, 'lp', fs=RATE, output='sos'), b)
bed = np.tanh(bed / (np.percentile(np.abs(bed), 99.5) + 1e-9)) * 0.9
seg = bed[int(3 * RATE): int(17.5 * RATE)]
fade = int(0.5 * RATE)
body = seg[:-fade].copy()
body[:fade] = seg[:fade] * np.linspace(0, 1, fade) + seg[-fade:] * np.linspace(1, 0, fade)
body = signal.resample_poly(body, 1, 2)
body /= np.sqrt(np.mean(body ** 2)) * 6   # RMS ~ 0.17
wavfile.write(OUT + r'\fire-bed.wav', RATE // 2, (np.clip(body, -1, 1) * 32000).astype(np.int16))
print(len(gs), 'grains,', round(len(allg) / RATE, 2), 's; bed', round(len(body) / (RATE / 2), 1), 's')
