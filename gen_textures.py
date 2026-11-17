"""Generate the AccelerationAbsorbers texture PNGs without Blender.

The PNG writer / noise functions are pure Python (zlib + struct), so this
runs with a stock interpreter:
    python gen_textures.py

Writes into AccelerationAbsorbers/Content/Textures/.
Keep in sync with build_inline_absorber.py.
"""

import os
import zlib
import struct
import random
import math
import colorsys

ROOT = os.path.dirname(os.path.abspath(__file__))
TEX_DIR = os.path.join(ROOT, 'AccelerationAbsorbers', 'Content', 'Textures')
os.makedirs(TEX_DIR, exist_ok=True)


def write_png(path, w, h, px):
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        for x in range(w):
            r, g, b = px(x, y)
            raw += bytes((r, g, b))

    def chunk(tag, data):
        return (struct.pack('>I', len(data)) + tag + data +
                struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF))

    png = (b'\x89PNG\r\n\x1a\n' +
           chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) +
           chunk(b'IDAT', zlib.compress(bytes(raw), 6)) +
           chunk(b'IEND', b''))
    with open(path, 'wb') as f:
        f.write(png)
    print('wrote', os.path.basename(path), f'{w}x{h}')


def clamp(v):
    return 0 if v < 0 else 255 if v > 255 else int(v)


class Noise:
    def __init__(self, seed, cells=8):
        self.cells = cells
        self.rng = random.Random(seed)
        self.grid = [[self.rng.random() for _ in range(cells + 1)]
                     for _ in range(cells + 1)]

    def __call__(self, x, y):
        fx = x * self.cells
        fy = y * self.cells
        ix = int(fx) % self.cells
        iy = int(fy) % self.cells
        tx = fx - int(fx)
        ty = fy - int(fy)
        tx = tx * tx * (3 - 2 * tx)
        ty = ty * ty * (3 - 2 * ty)
        g = self.grid
        a = g[iy][ix] * (1 - tx) + g[iy][ix + 1] * tx
        b = g[iy + 1][ix] * (1 - tx) + g[iy + 1][ix + 1] * tx
        return a * (1 - ty) + b * ty


def grain(x, y, seed=0):
    n = __import__('math').sin(x * 127.1 + y * 311.7 + seed * 74.7) * 43758.5453
    return (n - __import__('math').floor(n)) * 2 - 1


# name -> (diffuse rgb, roughness, metallic, noise amount, seed)
TEX_MATS = {
    'hull':   ((0.56, 0.59, 0.63), 0.48, 0.65, 0.05, 11),
    'dark':   ((0.14, 0.15, 0.17), 0.42, 0.85, 0.05, 23),
    'accent': ((0.46, 0.20, 0.74), 0.32, 0.35, 0.04, 37),
}
DIFFUSE_SIZE = 256
PBR_SIZE = 128

for name, (base, rough, metal, n_amt, seed) in TEX_MATS.items():
    noise = Noise(seed, cells=8)
    br, bg, bb = base
    rv = clamp(rough * 255)
    bv = clamp(metal * 255)

    def diff_px(x, y, _n=noise, _b=(br, bg, bb), _a=n_amt, _s=seed):
        u = x / DIFFUSE_SIZE
        v = y / DIFFUSE_SIZE
        m = _n(u, v) - 0.5
        g = grain(x, y, _s) * 0.5
        f = 1.0 + m * 2 * _a + g * _a * 0.6
        return (clamp(_b[0] * 255 * f), clamp(_b[1] * 255 * f),
                clamp(_b[2] * 255 * f))

    write_png(os.path.join(TEX_DIR, f'aa_diff_{name}.png'),
              DIFFUSE_SIZE, DIFFUSE_SIZE, diff_px)
    write_png(os.path.join(TEX_DIR, f'aa_pbr_{name}.png'), PBR_SIZE, PBR_SIZE,
              lambda x, y: (255, rv, bv))

write_png(os.path.join(TEX_DIR, 'aa_normal.png'), PBR_SIZE, PBR_SIZE,
          lambda x, y: (128, 128, 255))

# HSV hue degrees, with full saturation and a slightly restrained value.
RATING_HUES = {
    'green': 120,
    'cyan': 180,
    'amber': 38,
    'violet': 275,
}
for ci, (cname, hue) in enumerate(RATING_HUES.items()):
    cnoise = Noise(101 + ci * 17, cells=8)

    def c_diff_px(x, y, _n=cnoise, _h=hue):
        u = x / DIFFUSE_SIZE
        v = y / DIFFUSE_SIZE
        value = 0.80 + (_n(u, v) - 0.5) * 0.025
        rgb = colorsys.hsv_to_rgb(_h / 360.0, 1.0, value)
        return tuple(clamp(channel * 255) for channel in rgb)

    write_png(os.path.join(TEX_DIR, f'aa_diff_accent_{cname}.png'),
              DIFFUSE_SIZE, DIFFUSE_SIZE, c_diff_px)

# KSA uses the emissive map's red channel as a white-light intensity mask.
# Keep it grayscale and low so the rating hue remains saturated.
write_png(os.path.join(TEX_DIR, 'aa_emissive_glow.png'), PBR_SIZE, PBR_SIZE,
          lambda x, y: (46, 46, 46))
