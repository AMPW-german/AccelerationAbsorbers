"""Showcase lineup: 1m/2m/3m/4m inline absorbers in a row for presentation.

Run headless:
    blender --background --python build_lineup.py

Builds the same pressure-vessel design as build_inline_absorber.py at four
diameters (1-4 m), each scaled proportionally (length = 0.5 x diameter),
with the rating colors Green/Cyan/Amber/Violet, spaced along the X axis,
and renders aa_lineup.png. Saves AA_Lineup.blend next to this script.
"""

import bpy
import math
import os
from mathutils import Vector

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT_PNG = os.path.join(ROOT, 'aa_lineup.png')
BLEND_PATH = os.path.join(ROOT, 'AA_Lineup.blend')

# ------------------------------------------------------------------ cleanup
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for _ in range(3):
    bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)

# ------------------------------------------------------------------ materials
def make_mat(name, color, metallic, rough, emit=None, emit_strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    m.diffuse_color = color
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = color
    bsdf.inputs['Metallic'].default_value = metallic
    bsdf.inputs['Roughness'].default_value = rough
    if emit is not None:
        inp = bsdf.inputs.get('Emission Color') or bsdf.inputs.get('Emission')
        if inp is not None:
            inp.default_value = emit
        sinp = bsdf.inputs.get('Emission Strength')
        if sinp is not None:
            sinp.default_value = emit_strength
    return m

MAT_HULL = make_mat('AA_Hull',   (0.56, 0.59, 0.63, 1.0), 0.65, 0.48)
MAT_DARK = make_mat('AA_Dark',   (0.14, 0.15, 0.17, 1.0), 0.85, 0.42)

# variant -> (tag, diameter, accent rgb, glow rgb)
VARIANTS = [
    ('1m', 1.0, (0.15, 0.75, 0.25, 1.0), (0.35, 1.00, 0.45, 1.0)),   # green 4G
    ('2m', 2.0, (0.05, 0.70, 0.80, 1.0), (0.20, 0.95, 1.00, 1.0)),   # cyan  8G
    ('3m', 3.0, (0.95, 0.55, 0.08, 1.0), (1.00, 0.75, 0.20, 1.0)),   # amber 12G
    ('4m', 4.0, (0.55, 0.20, 0.85, 1.0), (0.80, 0.45, 1.00, 1.0)),   # violet 16G
]
MAT_ACCENT, MAT_GLOW = {}, {}
for tag, d, acc, glow in VARIANTS:
    MAT_ACCENT[tag] = make_mat(f'AA_Accent_{tag}', acc, 0.35, 0.32)
    MAT_GLOW[tag]   = make_mat(f'AA_Glow_{tag}',   glow, 0.30, 0.30,
                               emit=glow, emit_strength=2.5)

# ------------------------------------------------------------------ helpers
def dir_rot(direction):
    d = Vector(direction).normalized()
    return Vector((0.0, 0.0, 1.0)).rotation_difference(d).to_euler()


def finish(obj, mat):
    obj.data.materials.append(mat)
    return obj


def add_cylinder(name, loc, radius, depth, direction, mat, verts=48,
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
    if shade == 'cylinder':
        for p in o.data.polygons:
            p.use_smooth = len(p.vertices) <= 4
    elif shade == 'all':
        for p in o.data.polygons:
            p.use_smooth = True
    return finish(o, mat)


def add_box(name, loc, dims, mat):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = (dims[0], dims[1], dims[2])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, mat)


def add_pod(name, loc, radius, squash, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=48, ring_count=24, radius=radius, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = (1.0, squash, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(o, mat)


# ------------------------------------------------------------------ part
def build_part(tag, s, ox):
    """Build one absorber scaled by s, centred at X = ox."""
    X = (1.0, 0.0, 0.0)
    p = lambda v: (v[0] * s + ox, v[1] * s, v[2] * s)
    d = lambda v: tuple(x * s for x in v) if isinstance(v, (tuple, list)) else v * s
    H, D, A, G = MAT_HULL, MAT_DARK, MAT_ACCENT[tag], MAT_GLOW[tag]

    # hull: barrel, flanges, collar with centre section cut away
    add_cylinder(f'{tag}_barrel',  p((0, 0, 0)),        d(0.96), d(0.80), X, H, bevel=d(0.008))
    add_cylinder(f'{tag}_flange_f', p((0.45, 0, 0)),     d(1.00), d(0.10), X, H, bevel=d(0.008))
    add_cylinder(f'{tag}_flange_a', p((-0.45, 0, 0)),    d(1.00), d(0.10), X, H, bevel=d(0.008))
    collar = add_cylinder(f'{tag}_collar',  p((0, 0, 0)), d(1.00), d(0.14), X, H, bevel=d(0.008))

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(ox, 0, 0))
    cut = bpy.context.active_object
    cut.scale = (d(0.20), d(0.32), d(2.40))
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

    # dark: four continuous diagonal conduit runs, flange to flange
    pipe_r, pipe_len, pipe_rad = d(0.030), d(0.88), d(0.9446)
    for sy in (1.0, -1.0):
        for sz in (1.0, -1.0):
            y = sy * pipe_rad / math.sqrt(2.0)
            z = sz * pipe_rad / math.sqrt(2.0)
            add_cylinder(f'{tag}_pipe_{sy:+.0f}{sz:+.0f}', p((0, y / s, z / s)),
                         pipe_r, pipe_len, X, D)

    # dark: top hatch + label area, bottom vent
    add_box(f'{tag}_hatch_panel',  p((0.0, 0.0, 0.953)),  d((0.36, 0.30, 0.025)), D)
    add_box(f'{tag}_hatch_handle', p((-0.10, 0.0, 0.968)), d((0.10, 0.025, 0.018)), D)
    add_box(f'{tag}_vent_backing', p((0.0, 0.0, -0.952)), d((0.30, 0.24, 0.025)), D)
    for i in range(6):
        y = (-0.0925 + i * 0.037)
        add_box(f'{tag}_vent_bar_{i}', p((0.0, y, -0.966)), d((0.26, 0.022, 0.014)), D)

    # dark: ports (4x) and pods (4x)
    for xs, xn in ((0.28, 'f'), (-0.28, 'a')):
        add_cylinder(f'{tag}_port_{xn}top', p((xs, 0.0, 0.93)), d(0.045), d(0.10), (0, 0, 1),  D)
        add_cylinder(f'{tag}_port_{xn}bot', p((xs, 0.0, -0.93)), d(0.045), d(0.10), (0, 0, -1), D)
        add_pod(f'{tag}_pod_{xn}r', p((xs, 0.935, 0.0)), d(0.065), 0.55, D)
        add_pod(f'{tag}_pod_{xn}l', p((xs, -0.935, 0.0)), d(0.065), 0.55, D)

    # glow: violet-equivalent stripe rings in the variant colour
    add_cylinder(f'{tag}_stripe_f', p((0.28, 0, 0)), d(0.972), d(0.020), X, G)
    add_cylinder(f'{tag}_stripe_a', p((-0.28, 0, 0)), d(0.972), d(0.020), X, G)

    # accent: 8-fold indicator studs on both collar faces
    stud_r, stud_len, stud_circle = d(0.014), d(0.024), d(0.982)
    for side, xs in (('f', 0.076), ('a', -0.076)):
        for i in range(8):
            a = math.radians(22.5 + i * 45.0)
            y = stud_circle * math.cos(a)
            z = stud_circle * math.sin(a)
            add_cylinder(f'{tag}_stud_{side}{i}', p((xs, y / s, z / s)),
                         stud_r, stud_len, X, A)


# ------------------------------------------------------------------ layout
GAP = 1.2
lengths = [0.5 * d for _, d, _, _ in VARIANTS]
total = sum(lengths) + GAP * (len(VARIANTS) - 1)
x = -total / 2.0
for (tag, dia, _, _), ln in zip(VARIANTS, lengths):
    build_part(tag, dia / 2.0, x + ln / 2.0)
    x += ln + GAP
print('Lineup built:', [(t, d) for t, d, _, _ in VARIANTS])

# ------------------------------------------------------------------ stage
world = bpy.data.worlds.new('World') if not bpy.data.worlds else bpy.data.worlds[0]
bpy.context.scene.world = world
world.use_nodes = True
bg = world.node_tree.nodes.get('Background')
bg.inputs[0].default_value = (0.05, 0.05, 0.07, 1.0)
bg.inputs[1].default_value = 0.6

bpy.ops.object.camera_add(location=(0.4, -15.5, 4.2))
cam = bpy.context.active_object
cam.data.lens = 40
cam.rotation_euler = (Vector((0, 0, 0.4)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
bpy.context.scene.camera = cam

bpy.ops.object.light_add(type='SUN', location=(6, -8, 12))
sun = bpy.context.active_object
sun.data.energy = 3.5
sun.rotation_euler = (math.radians(50), math.radians(15), math.radians(30))
sun.data.angle = math.radians(10)

bpy.ops.object.light_add(type='AREA', location=(-8, -6, 6))
fill = bpy.context.active_object
fill.data.energy = 800
fill.data.shape = 'DISK'
fill.data.size = 5.0
fill.rotation_euler = (Vector((0, 0, 0.5)) - fill.location).to_track_quat('-Z', 'Y').to_euler()

sc = bpy.context.scene
try:
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
except Exception:
    sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 1920
sc.render.resolution_y = 800
sc.render.resolution_percentage = 100
sc.render.image_settings.file_format = 'PNG'
sc.render.filepath = OUT_PNG
try:
    sc.view_settings.look = 'AgX - Medium High Contrast'
except Exception:
    pass

bpy.ops.render.render(write_still=True)
print('wrote', OUT_PNG)

bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
print('saved', BLEND_PATH)
print('Done.')
