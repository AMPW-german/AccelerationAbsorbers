"""Build the Compact and Heavy radial absorber meshes.

Run from Blender (or the Blender MCP background runner). The inline source
blend is left intact; this creates a separate radial scene, GLB atlas, blend,
and preview image.
"""

import math
import os
from mathutils import Vector
import bpy

ROOT = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(ROOT, 'AccelerationAbsorbers', 'Content')
MESH_DIR = os.path.join(CONTENT, 'Meshes')
GLB_PATH = os.path.join(MESH_DIR, 'AA_RadialMeshAtlas.glb')
BLEND_PATH = os.path.join(ROOT, 'AA_RadialParts.blend')
PREVIEW_PATH = os.path.join(ROOT, 'aa_radial_preview.png')
os.makedirs(MESH_DIR, exist_ok=True)

MATERIALS = {}
for name, color, metallic, roughness in (
    ('Hull', (0.56, 0.59, 0.63, 1), 0.65, 0.48),
    ('Dark', (0.14, 0.15, 0.17, 1), 0.85, 0.42),
    ('Accent', (0.62, 0.12, 0.95, 1), 0.35, 0.32),
    ('Glow', (0.62, 0.12, 0.95, 1), 0.2, 0.25),
):
    material = bpy.data.materials.get('AA_Radial_' + name)
    if material is None:
        material = bpy.data.materials.new('AA_Radial_' + name)
    material.diffuse_color = color
    material.use_nodes = True
    shader = material.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = color
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    if name == 'Glow':
        shader.inputs['Emission Color'].default_value = color
        shader.inputs['Emission Strength'].default_value = 0.8
    MATERIALS[name] = material

SOURCE_SCENE = next(
    (scene for scene in bpy.data.scenes
     if scene.name not in ('AA_RadialExport', 'AA_RadialPreview')),
    bpy.context.scene,
)
if bpy.context.window:
    bpy.context.window.scene = SOURCE_SCENE
for stale_name in ('AA_RadialExport', 'AA_RadialPreview'):
    stale_scene = bpy.data.scenes.get(stale_name)
    if stale_scene is not None:
        for stale_obj in list(stale_scene.objects):
            bpy.data.objects.remove(stale_obj, do_unlink=True)
        bpy.data.scenes.remove(stale_scene)
for stale_collection in list(bpy.data.collections):
    if stale_collection.name.startswith('RadialLineup') and not stale_collection.users:
        bpy.data.collections.remove(stale_collection)
for stale_mesh in list(bpy.data.meshes):
    if stale_mesh.name.startswith('AA_Radial') and not stale_mesh.users:
        bpy.data.meshes.remove(stale_mesh)
SCENE = bpy.data.scenes.new('AA_RadialExport')
if bpy.context.window:
    bpy.context.window.scene = SCENE

GROUPS = {
    'Compact': {material: [] for material in MATERIALS},
    'Heavy': {
        **{material: [] for material in MATERIALS},
        'CageHull': [],
        'CageDark': [],
    },
}


def finish(obj, design, material, bevel=0.0, mesh_group=None):
    obj.data.materials.append(MATERIALS[material])
    if bevel:
        modifier = obj.modifiers.new('Machined edge radii', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = 'ANGLE'
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    GROUPS[design][mesh_group or material].append(obj)
    return obj


def box(design, material, name, location, dimensions, bevel=0.0,
        mesh_group=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, design, material, bevel, mesh_group)


def cylinder(design, material, name, location, radius, depth, bevel=0.0,
             direction=(0, 0, 1), mesh_group=None):
    vector = Vector(direction).normalized()
    rotation = Vector((0, 0, 1)).rotation_difference(vector).to_euler()
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=48, radius=radius, depth=depth, location=location,
        rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj = finish(obj, design, material, bevel, mesh_group)
    for polygon in obj.data.polygons:
        polygon.use_smooth = len(polygon.vertices) <= 4
    return obj


def torus(design, material, name, location, radius, thickness):
    bpy.ops.mesh.primitive_torus_add(
        major_segments=48, minor_segments=12,
        major_radius=radius, minor_radius=thickness,
        location=location)
    obj = bpy.context.object
    obj.name = name
    obj = finish(obj, design, material)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def bolt(design, name, x, y, z, radius=0.018):
    cylinder(design, 'Dark', name, (x, y, z), radius, 0.018, 0.003)
    cylinder(design, 'Accent', name + '_dot', (x, y, z + 0.010),
             radius * 0.30, 0.004)


def build_compact():
    design = 'Compact'

    # A single sealed radial drum sits on a thick, four-point mounting shoe.
    box(design, 'Hull', 'Compact_MountShoe', (0, 0, 0.035),
        (0.52, 0.56, 0.07), 0.025)
    box(design, 'Dark', 'Compact_SeatGasket', (0, 0, 0.078),
        (0.42, 0.42, 0.025), 0.012)
    for x in (-0.215, 0.215):
        for y in (-0.175, 0.175):
            box(design, 'Hull', 'Compact_BoltBoss', (x, y, 0.083),
                (0.075, 0.075, 0.035), 0.01)
            bolt(design, 'Compact_MountBolt', x, y, 0.108)

    cylinder(design, 'Hull', 'Compact_PressureDrum', (0, 0, 0.235),
             0.185, 0.29, 0.008)
    cylinder(design, 'Dark', 'Compact_LowerRim', (0, 0, 0.105),
             0.204, 0.045, 0.004)
    cylinder(design, 'Hull', 'Compact_UpperFlange', (0, 0, 0.36),
             0.205, 0.055, 0.006)
    torus(design, 'Glow', 'Compact_GlowBandLower', (0, 0, 0.145),
          0.194, 0.012)
    torus(design, 'Glow', 'Compact_GlowBandUpper', (0, 0, 0.335),
          0.194, 0.012)
    cylinder(design, 'Dark', 'Compact_ServiceCap', (0, 0, 0.397),
             0.15, 0.025, 0.005)
    cylinder(design, 'Hull', 'Compact_CapBoss', (0, 0, 0.419),
             0.075, 0.025, 0.004)

    # Short external hard lines tie the lid and lower flange into the shoe.
    for x in (-0.165, 0.165):
        cylinder(design, 'Dark', 'Compact_HardLine', (x, 0, 0.235),
                 0.022, 0.235, 0.004)
        cylinder(design, 'Accent', 'Compact_LineFerrule', (x, 0, 0.34),
                 0.028, 0.022, 0.003)
    for side in (-1, 1):
        box(design, 'Dark', 'Compact_RatingPanel', (0, side * 0.179, 0.245),
            (0.12, 0.035, 0.085), 0.012)
        for x in (-0.033, 0, 0.033):
            cylinder(design, 'Accent', 'Compact_StatusLamp',
                     (x, side * 0.201, 0.254), 0.009, 0.008, 0.002,
                     direction=(0, side, 0))


def build_heavy():
    design = 'Heavy'

    # Two pressure cells in a shared structural cradle make this distinct
    # from the compact's single-canister body while retaining the same style.
    box(design, 'Hull', 'Heavy_MountPlate', (0, 0, 0.045),
        (1.12, 0.70, 0.09), 0.03)
    box(design, 'Dark', 'Heavy_SeatGasket', (0, 0, 0.097),
        (1.02, 0.58, 0.035), 0.012)
    for x in (-0.42, 0.42):
        for y in (-0.25, 0.25):
            box(design, 'Hull', 'Heavy_BoltBoss', (x, y, 0.108),
                (0.10, 0.09, 0.04), 0.012)
            bolt(design, 'Heavy_MountBolt', x, y, 0.128, 0.022)

    for x in (-0.25, 0.25):
        cylinder(design, 'Hull', 'Heavy_PressureCell', (x, 0, 0.315),
                 0.205, 0.405, 0.009)
        cylinder(design, 'Dark', 'Heavy_LowerCellRim', (x, 0, 0.135),
                 0.222, 0.045, 0.005)
        cylinder(design, 'Hull', 'Heavy_UpperCellFlange', (x, 0, 0.495),
                 0.225, 0.055, 0.006)
        torus(design, 'Glow', 'Heavy_GlowBandLower', (x, 0, 0.18),
              0.214, 0.012)
        torus(design, 'Glow', 'Heavy_GlowBandUpper', (x, 0, 0.465),
              0.214, 0.012)
        cylinder(design, 'Dark', 'Heavy_CellCap', (x, 0, 0.531),
                 0.17, 0.025, 0.005)
        cylinder(design, 'Hull', 'Heavy_CapBoss', (x, 0, 0.555),
                 0.07, 0.025, 0.004)

    # Thick cage rails protect both cells and merge into the base plate.
    for x in (-0.515, 0.515):
        for y in (-0.29, 0.29):
            box(design, 'Dark', 'Heavy_CageUpright', (x, y, 0.315),
                (0.055, 0.055, 0.39), 0.012, mesh_group='CageDark')
    for y in (-0.29, 0.29):
        for z in (0.16, 0.47):
            box(design, 'Hull', 'Heavy_CageCrossbar', (0, y, z),
                (1.03, 0.055, 0.06), 0.012, mesh_group='CageHull')

    # Paired hard lines run through the manifold blocks and disappear into
    # both pressure cells, so no exposed flat pipe ends remain.
    for side in (-1, 1):
        cylinder(design, 'Hull', 'Heavy_CellConnectorRod',
                 (0, side * 0.18, 0.32), 0.022, 0.50, 0.004,
                 direction=(1, 0, 0))
        box(design, 'Dark', 'Heavy_CentralManifold',
            (0, side * 0.22, 0.32), (0.13, 0.13, 0.18), 0.018)
        box(design, 'Accent', 'Heavy_RatingBadge',
            (0, side * 0.292, 0.325), (0.10, 0.018, 0.09), 0.008)
        for x in (-0.027, 0, 0.027):
            cylinder(design, 'Glow', 'Heavy_StatusLamp',
                     (x, side * 0.304, 0.33), 0.008, 0.004, 0.001,
                     direction=(0, side, 0))


def join_group(design, material):
    objects = GROUPS[design][material]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    active = objects[0]
    bpy.context.view_layer.objects.active = active
    bpy.ops.object.join()
    active.name = f'AA_Radial{design}_{material}'
    active.data.name = active.name
    active.select_set(False)
    return active


build_compact()
build_heavy()
atlas_objects = [
    *(join_group('Compact', material)
      for material in ('Hull', 'Dark', 'Accent', 'Glow')),
    *(join_group('Heavy', material)
      for material in ('Hull', 'Dark', 'Accent', 'Glow')),
    join_group('Heavy', 'CageHull'),
    join_group('Heavy', 'CageDark'),
]
# Radial parts extend outward from the mounting plane along local -Z.
for obj in atlas_objects:
    for vertex in obj.data.vertices:
        vertex.co.y = -vertex.co.y
        vertex.co.z = -vertex.co.z
    obj.data.update()

# Export only this new scene; the source inline scene is preserved.
bpy.ops.export_scene.gltf(
    filepath=GLB_PATH,
    export_format='GLB',
    export_apply=True,
    export_animations=False,
    export_cameras=False,
    export_lights=False,
    export_yup=False,
    use_active_scene=True,
)

# A separate render scene shows the two silhouettes side-by-side without
# translating the source meshes used by the KSA atlas.
preview = bpy.data.scenes.new('AA_RadialPreview')
if bpy.context.window:
    bpy.context.window.scene = preview
preview_collection = bpy.data.collections.new('RadialLineup')
preview.collection.children.link(preview_collection)

for obj in atlas_objects:
    if 'Compact' in obj.name:
        offsets = (-1.47,)
    elif 'Cage' in obj.name:
        offsets = (-0.30,)
    else:
        offsets = (-0.30, 1.17)
    for offset in offsets:
        display = obj.copy()
        display.data = obj.data
        display.location.x = offset
        display.rotation_euler.x = math.pi
        preview_collection.objects.link(display)

inline_blend = os.path.join(ROOT, 'AA_Inline2M_Violet.blend')
inline_names = ('AA_Hull', 'AA_Dark', 'AA_Accent', 'AA_Glow')
inline_objects = {
    name: bpy.data.objects.get(name)
    for name in inline_names
}
inline_objects = {name: obj for name, obj in inline_objects.items()
                  if obj is not None}
if len(inline_objects) != len(inline_names):
    with bpy.data.libraries.load(inline_blend, link=False) as (source, target_data):
        target_data.objects = [
            name for name in inline_names if name in source.objects
        ]
    inline_objects.update({
        obj.name: obj for obj in target_data.objects if obj is not None
    })
if len(inline_objects) != len(inline_names):
    raise RuntimeError('Could not load all meshes for the 1 m Violet reference')
for name in inline_names:
    source_obj = inline_objects[name]
    display = source_obj.copy()
    display.data = source_obj.data
    display.location = (0, 1.8, 0.5)
    display.scale = (0.5, 0.5, 0.5)
    display.rotation_euler = (0, 0, -math.pi / 2)
    preview_collection.objects.link(display)

camera_data = bpy.data.cameras.new('AA_RadialPreviewCamera')
camera = bpy.data.objects.new('AA_RadialPreviewCamera', camera_data)
preview_collection.objects.link(camera)
camera.location = (0, -7.0, 4.3)
target = Vector((0, 0.25, 0.42))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 5.8
preview.camera = camera

for name, location, power, size in (
    ('Key', (1, -3, 4), 650, 3),
    ('Fill', (-3, -1, 2.5), 350, 3),
    ('Rim', (1.5, 3, 3.4), 800, 2),
):
    light_data = bpy.data.lights.new('AA_Radial_' + name, 'AREA')
    light_data.energy = power
    light_data.shape = 'DISK'
    light_data.size = size
    light = bpy.data.objects.new('AA_Radial_' + name, light_data)
    light.location = location
    preview_collection.objects.link(light)
    light.rotation_euler = (target - light.location).to_track_quat('-Z', 'Y').to_euler()

world = bpy.data.worlds.new('AA_RadialPreviewWorld')
world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = (0.018, 0.025, 0.04, 1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.35
preview.world = world
preview.render.engine = 'BLENDER_EEVEE'
preview.render.resolution_x = 1440
preview.render.resolution_y = 900
preview.render.resolution_percentage = 100
preview.render.image_settings.file_format = 'PNG'
preview.render.filepath = PREVIEW_PATH
preview.render.film_transparent = False
preview.view_settings.view_transform = 'AgX'

bpy.context.window.scene = preview
bpy.ops.render.render(write_still=True)
if globals().get('SAVE_RADIAL_BLEND', True):
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
elif bpy.context.window:
    bpy.context.window.scene = SOURCE_SCENE

result = {
    'glb': GLB_PATH,
    'blend': BLEND_PATH,
    'preview': PREVIEW_PATH,
    'meshes': [obj.name for obj in atlas_objects],
}
