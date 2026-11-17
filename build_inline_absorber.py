"""Build the AccelerationAbsorbers 2 m inline Violet (16 G) absorber part.

Run headless:
    blender --background --python build_inline_absorber.py

Produces, under AccelerationAbsorbers/Content/:
    Meshes/AA_MeshAtlas.glb      - mesh atlas (meshes: AA_Hull, AA_Dark, AA_Accent)
    Textures/aa_*.png            - diffuse / AoRoughMetal / normal maps per material
and saves AA_Inline2M_Violet.blend next to this script for further editing.

KSA part space: +X forward/aft axis, +Y right, +Z up (1 BU = 1 m).
The part is a 2 m diameter inline module: radius 1.0 m, length 1.0 m,
axial connectors at X = +/-0.5 m (connector scale 2, matching stock 2W parts).

Look: futuristic but believable pressure-vessel machine - barrel, end
flanges, center collar, conduit runs, hatch, vent, sensor ports. Violet is
confined to stripe rings, indicator studs and a small rating label.
"""

import bpy
import math
import os
import zlib
import struct
import random
import colorsys
from mathutils import Vector

# ------------------------------------------------------------------ paths
ROOT = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(ROOT, 'AccelerationAbsorbers', 'Content')
MESH_DIR = os.path.join(CONTENT, 'Meshes')
TEX_DIR = os.path.join(CONTENT, 'Textures')
GLB_PATH = os.path.join(MESH_DIR, 'AA_MeshAtlas.glb')
BLEND_PATH = os.path.join(ROOT, 'AA_Inline2M_Violet.blend')
for d in (MESH_DIR, TEX_DIR):
    os.makedirs(d, exist_ok=True)

# ------------------------------------------------------------------ cleanup
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for _ in range(3):
    bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)

# ------------------------------------------------------------------ materials
MAT_DEFS = {
    # name        -> (viewport colour, )
    'Hull':   ((0.56, 0.59, 0.63, 1.0), 0.65, 0.48),
    'Dark':   ((0.14, 0.15, 0.17, 1.0), 0.85, 0.42),
    'Accent': ((0.46, 0.20, 0.74, 1.0), 0.35, 0.32),
    'Glow':   ((0.62, 0.30, 0.95, 1.0), 0.30, 0.30),
}
MAT = {}
for name, (col, metallic, rough) in MAT_DEFS.items():
    m = bpy.data.materials.new('AA_' + name)
    m.use_nodes = True
    m.diffuse_color = col
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = col
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = rough
    MAT[name] = m

GROUPS = {k: [] for k in MAT}


# ------------------------------------------------------------------ helpers
def dir_rot(direction):
    """Euler rotating the primitive's local +Z axis onto direction."""
    d = Vector(direction).normalized()
    return Vector((0.0, 0.0, 1.0)).rotation_difference(d).to_euler()


def _finish(obj, group, shade):
    obj.data.materials.append(MAT[group])
    if shade == 'cylinder':
        # smooth side walls, flat caps: cylinder sides are quads, caps are ngons
        for p in obj.data.polygons:
            p.use_smooth = len(p.vertices) <= 4
    elif shade == 'all':
        for p in obj.data.polygons:
            p.use_smooth = True
    GROUPS[group].append(obj)
    return obj


def add_cylinder(name, loc, radius, depth, direction, group, verts=24,
                 bevel=0.0, shade='cylinder'):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=verts, radius=radius, depth=depth,
        location=loc, rotation=dir_rot(direction))
    o = bpy.context.active_object
    o.name = name
    if bevel > 0.0:
        mod = o.modifiers.new('EdgeBreak', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier='EdgeBreak')
    return _finish(o, group, shade)


def add_box(name, loc, dims, group='Dark'):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = (dims[0], dims[1], dims[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, group, 'flat')


def add_pod(name, loc, radius, squash, group='Dark'):
    """Squashed UV sphere, long axis along Y (radial sensor pod)."""
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=48, ring_count=24, radius=radius, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = (1.0, squash, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(o, group, 'all')


# ------------------------------------------------------------------ build
X = (1.0, 0.0, 0.0)
print('Building AA 2m inline violet absorber ...')

# ---- Hull: pressure-vessel shell -------------------------------------
add_cylinder('shell_barrel',  (0, 0, 0),        0.96, 0.80, X, 'Hull', verts=48, bevel=0.008)
add_cylinder('shell_flange_f', (0.45, 0, 0),     1.00, 0.10, X, 'Hull', verts=48, bevel=0.008)
add_cylinder('shell_flange_a', (-0.45, 0, 0),    1.00, 0.10, X, 'Hull', verts=48, bevel=0.008)
add_cylinder('shell_collar',  (0, 0, 0),         1.00, 0.14, X, 'Hull', verts=48, bevel=0.008)

# split the collar into two side arcs: remove the centre section through the
# ring's full height so the top hatch and bottom vent are fully exposed
collar = bpy.data.objects['shell_collar']
bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
cut = bpy.context.active_object
cut.scale = (0.20, 0.32, 2.40)
bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
bpy.ops.object.select_all(action='DESELECT')
collar.select_set(True)
bpy.context.view_layer.objects.active = collar
mod = collar.modifiers.new('section', 'BOOLEAN')
mod.operation = 'DIFFERENCE'
mod.solver = 'EXACT'
mod.object = cut
bpy.ops.object.modifier_apply(modifier='section')
bpy.data.objects.remove(cut, do_unlink=True)
collar.select_set(False)

# ---- Dark: surface conduit runs ---------------------------------------
# four continuous diagonal runs, flange to flange through the collar
pipe_r, pipe_len, pipe_rad = 0.030, 0.88, 0.9446
for sy in (1.0, -1.0):
    for sz in (1.0, -1.0):
        y = sy * pipe_rad / math.sqrt(2.0)
        z = sz * pipe_rad / math.sqrt(2.0)
        add_cylinder(f'pipe_{sy:+.0f}{sz:+.0f}', (0, y, z),
                     pipe_r, pipe_len, X, 'Dark', verts=48)

# ---- Dark: top access hatch -------------------------------------------
add_box('hatch_panel',  (0.0, 0.0, 0.953),  (0.36, 0.30, 0.025), 'Dark')
add_box('hatch_handle', (-0.10, 0.0, 0.968), (0.10, 0.025, 0.018), 'Dark')

# ---- Accent: rating label on the hatch ---------------------------------
add_box('hatch_label', (-0.09, 0.0, 0.968), (0.13, 0.05, 0.008), 'Accent')

# ---- Dark: belly vent grille -------------------------------------------
add_box('vent_backing', (0.0, 0.0, -0.952), (0.30, 0.24, 0.025), 'Dark')
for i in range(6):
    y = -0.0925 + i * 0.037
    add_box(f'vent_bar_{i}', (0.0, y, -0.966), (0.26, 0.022, 0.014), 'Dark')

# ---- Dark: sensor ports and pods ----------------------------------------
for xs, xn in ((0.28, 'f'), (-0.28, 'a')):
    add_cylinder(f'port_{xn}top', (xs, 0.0,  0.93), 0.045, 0.10, (0, 0, 1),  'Dark', verts=48)
    add_cylinder(f'port_{xn}bot', (xs, 0.0, -0.93), 0.045, 0.10, (0, 0, -1), 'Dark', verts=48)
    add_pod(f'pod_{xn}r', (xs,  0.935, 0.0), 0.065, 0.55, 'Dark')
    add_pod(f'pod_{xn}l', (xs, -0.935, 0.0), 0.065, 0.55, 'Dark')

# ---- Glow: violet stripe rings (emissive in-game) -----------------------
add_cylinder('stripe_f', ( 0.28, 0, 0), 0.972, 0.020, X, 'Glow', verts=48)
add_cylinder('stripe_a', (-0.28, 0, 0), 0.972, 0.020, X, 'Glow', verts=48)

# ---- Accent: indicator studs on the collar faces -------------------------
# 8-fold, sitting between the top/bottom terminals and the diagonal pipes
stud_r, stud_len, stud_circle = 0.014, 0.024, 0.982
for side, xs in (('f', 0.076), ('a', -0.076)):
    for i in range(8):
        a = math.radians(22.5 + i * 45.0)
        y = stud_circle * math.cos(a)
        z = stud_circle * math.sin(a)
        add_cylinder(f'stud_{side}{i}', (xs, y, z), stud_r, stud_len,
                     X, 'Accent', verts=48)

# ------------------------------------------------------------------ join
print('Joining per material group ...')
for group, objs in GROUPS.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    joined = bpy.context.view_layer.objects.active
    joined.name = f'AA_{group}'
    joined.data.name = f'AA_{group}'
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    print(f'  {group}: {len(objs)} objects -> {len(joined.data.vertices)} verts, '
          f'{len(joined.data.polygons)} polys')

# ------------------------------------------------------------------ textures
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
    n = math.sin(x * 127.1 + y * 311.7 + seed * 74.7) * 43758.5453
    return (n - math.floor(n)) * 2 - 1


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

# ------------------------------------------------------------------ export
print('Exporting GLB ...')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(
    filepath=GLB_PATH,
    export_format='GLB',
    export_apply=True,
    export_animations=False,
    export_cameras=False,
    export_lights=False,
)
print('wrote', GLB_PATH)

bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
print('saved', BLEND_PATH)
print('Done. Objects:', [o.name for o in bpy.data.objects])
