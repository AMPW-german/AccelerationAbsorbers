"""Generate AccelerationAbsorbersPartsAssets.xml and
AccelerationAbsorbersPartsGameData.xml.

Rating configurations are standalone parts because KSA <Variants> inherit
everything except <SubParts>. Heavy Radial's cage/no-cage options are cosmetic
variants under each rating head, so they inherit the same gameplay data.
Inline sizes share scaled meshes; radial designs use dedicated meshes.
All rating heads carry the custom "Absorbers" editor tag.

Balance values come from PARTS_AND_BALANCE.md.
Run:  python gen_parts_xml.py
"""

import os
import math

ROOT = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(ROOT, 'AccelerationAbsorbers', 'Content')
ASSETS_PATH = os.path.join(CONTENT, 'AccelerationAbsorbersPartsAssets.xml')
GAMEDATA_PATH = os.path.join(CONTENT, 'AccelerationAbsorbersPartsGameData.xml')

# tag -> (diameter m, length m = D/2, subpart scale vs 2 m base mesh,
#          connector offset m, connector scale)
SIZES = [
    ('1M', 1.0, 0.5, 0.5, 0.25, 1.0),
    ('2M', 2.0, 1.0, 1.0, 0.50, 2.0),
    ('3M', 3.0, 1.5, 1.5, 0.75, 3.0),
    ('4M', 4.0, 2.0, 2.0, 1.00, 4.0),
]
DENSITY_KG_M3 = 5000.0

# (tag, envelope diameter m, depth m, connector scale,
#  collider X, collider Y, collider Z, collider center Z)
# Radial mass uses a solid-cylinder approximation of each design envelope.
RADIALS = [
    ('Compact', 0.5, 0.40, 0.5, 0.56, 0.60, 0.44, -0.22),
    ('Heavy', 0.9, 0.55, 1.0, 1.14, 0.74, 0.60, -0.30),
]

# color -> (display, MaxGLoad)
RATINGS = [
    ('Green',  4),
    ('Cyan',   8),
    ('Amber',  12),
    ('Violet', 16),
]
COLOR_KEY = {'Green': 'green', 'Cyan': 'cyan',
             'Amber': 'amber', 'Violet': 'violet'}

# (size tag, color) -> (BasePower W, PowerPerGLoad k, MaxPower W)
STATS = {
    ('1M', 'Green'):  (125.0, 1.30, 5000.0),
    ('1M', 'Cyan'):   (187.5, 1.70, 6650.0),
    ('1M', 'Amber'):  (250.0, 2.00, 8350.0),
    ('1M', 'Violet'): (312.5, 2.30, 10000.0),
    ('2M', 'Green'):  (375.0, 1.235, 25000.0),
    ('2M', 'Cyan'):   (562.5, 1.615, 33250.0),
    ('2M', 'Amber'):  (750.0, 1.90, 41750.0),
    ('2M', 'Violet'): (937.5, 2.185, 50000.0),
    ('3M', 'Green'):  (1000.0, 1.17, 80000.0),
    ('3M', 'Cyan'):   (1500.0, 1.53, 106400.0),
    ('3M', 'Amber'):  (2000.0, 1.80, 133600.0),
    ('3M', 'Violet'): (2500.0, 2.07, 160000.0),
    ('4M', 'Green'):  (2500.0, 1.105, 250000.0),
    ('4M', 'Cyan'):   (3750.0, 1.445, 332500.0),
    ('4M', 'Amber'):  (5000.0, 1.70, 417500.0),
    ('4M', 'Violet'): (6250.0, 1.955, 500000.0),
}

# (radial tag, color) -> (MaxGLoad, BasePower W, PowerPerGLoad, MaxPower W)
RADIAL_STATS = {
    ('Compact', 'Green'):  (4, 50.0, 1.30, 2500.0),
    ('Compact', 'Cyan'):   (8, 75.0, 1.70, 3325.0),
    ('Compact', 'Amber'):  (12, 100.0, 2.00, 4175.0),
    ('Compact', 'Violet'): (16, 125.0, 2.30, 5000.0),
    ('Heavy', 'Green'):    (4, 200.0, 1.235, 15000.0),
    ('Heavy', 'Cyan'):     (8, 300.0, 1.615, 19950.0),
    ('Heavy', 'Amber'):    (12, 400.0, 1.90, 25050.0),
    ('Heavy', 'Violet'):   (16, 500.0, 2.185, 30000.0),
}


def part_id(size_tag, color):
    return f'AA_Prefab_InlineAbsorber{size_tag}_{color}'


def radial_part_id(radial_tag, color):
    return f'AA_Prefab_RadialAbsorber{radial_tag}_{color}'


def display_name(size_tag, color, gload):
    return f'{size_tag} Inline Absorber - {color} {gload}G'


def radial_display_name(radial_tag, color, gload):
    return f'{radial_tag} Radial Absorber - {color} {gload}G'


def subparts_block(scale, color, indent='\t\t\t'):
    """Four SubPart instances; colored refs depend on rating color."""
    ck = COLOR_KEY[color]
    lines = [
        f'{indent}<SubPart Key="AA_Hull_1" Ref="AA_Subpart_Hull">',
        f'{indent}\t<Transform><Scale X="{scale}" Y="{scale}" Z="{scale}" /></Transform>',
        f'{indent}</SubPart>',
        f'{indent}<SubPart Key="AA_Dark_1" Ref="AA_Subpart_Dark">',
        f'{indent}\t<Transform><Scale X="{scale}" Y="{scale}" Z="{scale}" /></Transform>',
        f'{indent}</SubPart>',
        f'{indent}<SubPart Key="AA_Accent_1" Ref="AA_Subpart_Accent_{ck.capitalize()}">',
        f'{indent}\t<Transform><Scale X="{scale}" Y="{scale}" Z="{scale}" /></Transform>',
        f'{indent}</SubPart>',
        f'{indent}<SubPart Key="AA_Glow_1" Ref="AA_Subpart_Glow_{ck.capitalize()}">',
        f'{indent}\t<Transform><Scale X="{scale}" Y="{scale}" Z="{scale}" /></Transform>',
        f'{indent}</SubPart>',
    ]
    return '\n'.join(lines)


def connectors_block(conn_x, conn_scale, indent='\t\t'):
    return f'''{indent}<Connectors>
{indent}\t<Connector Key="_connectorFore">
{indent}\t\t<Transform>
{indent}\t\t\t<Position X="{conn_x}" />
{indent}\t\t\t<Scale X="{conn_scale}" Y="{conn_scale}" Z="{conn_scale}" />
{indent}\t\t</Transform>
{indent}\t</Connector>
{indent}\t<Connector Key="_connectorAft">
{indent}\t\t<Transform>
{indent}\t\t\t<Position X="-{conn_x}" />
{indent}\t\t\t<Rotation Z="3.1416" />
{indent}\t\t\t<Scale X="{conn_scale}" Y="{conn_scale}" Z="{conn_scale}" />
{indent}\t\t</Transform>
{indent}\t</Connector>
{indent}</Connectors>'''


def collider_block(length, radius, indent='\t\t\t'):
    return f'''{indent}<Collider Key="Collider1">
{indent}\t<Colliders>
{indent}\t\t<Cylinder Key="CylinderCollider1">
{indent}\t\t\t<LocationAsmb X="0" Y="0" Z="0" />
{indent}\t\t\t<Collider2Asmb X="0" Y="0" Z="-1.5708" />
{indent}\t\t\t<LengthY M="{length}" />
{indent}\t\t\t<Radius M="{radius}" />
{indent}\t\t</Cylinder>
{indent}\t</Colliders>
{indent}</Collider>'''


def radial_subparts_block(radial_tag, color, include_cage=True,
                          indent='\t\t\t'):
    suffix = COLOR_KEY[color].capitalize()
    subparts = [
        ('Hull', ''),
        ('Dark', ''),
        ('Accent', '_' + suffix),
        ('Glow', '_' + suffix),
    ]
    if radial_tag == 'Heavy' and include_cage:
        subparts += [('CageHull', ''), ('CageDark', '')]
    return '\n'.join(
        f'{indent}<SubPart Key="AA_Radial{radial_tag}_{material}_1" '
        f'Ref="AA_Subpart_Radial{radial_tag}_{material}{color_suffix}" />'
        for material, color_suffix in subparts
    )


def radial_nocage_part_id(color):
    return radial_part_id('Heavy', color) + '_NoCage'


def radial_collider_block(dim_x, dim_y, dim_z, center_z,
                          indent='\t\t\t'):
    return f'''{indent}<Collider Key="RadialCollider">
{indent}\t<Colliders>
{indent}\t\t<Box Key="RadialBoxCollider">
{indent}\t\t\t<LocationAsmb X="0" Y="0" Z="{fmt(center_z)}" />
{indent}\t\t\t<Collider2Asmb X="0" Y="0" Z="0" />
{indent}\t\t\t<LengthX M="{fmt(dim_x)}" />
{indent}\t\t\t<LengthY M="{fmt(dim_y)}" />
{indent}\t\t\t<LengthZ M="{fmt(dim_z)}" />
{indent}\t\t</Box>
{indent}\t</Colliders>
{indent}</Collider>'''


def fmt(v):
    """Trim trailing zeros for readable XML numbers."""
    s = f'{v:g}'
    return s


def build_assets():
    out = ['<?xml version="1.0" encoding="utf-8"?>', '<Assets>',
           '\t<!-- Acceleration absorber parts - visual assets.',
           '\t     Four inline sizes and two radial designs, each with four',
           '\t     standalone rating/color parts (KSA variants cannot carry',
           '\t     different gameplay stats). Heavy Radial ratings offer a',
           '\t     cage-free cosmetic variant; inline parts scale shared',
           '\t     meshes, radial parts use dedicated meshes. -->',
           '',
           '\t<MeshAtlas Id="AA_MeshAtlas" Path="Content/Meshes/AA_MeshAtlas.glb" />',
           '\t<MeshAtlas Id="AA_RadialMeshAtlas" Path="Content/Meshes/AA_RadialMeshAtlas.glb" />',
           '']

    # textures
    tex = [('AA_Tex_DiffHull', 'aa_diff_hull.png'),
           ('AA_Tex_DiffDark', 'aa_diff_dark.png')]
    tex += [(f'AA_Tex_DiffAccent_{c}', f'aa_diff_accent_{COLOR_KEY[c]}.png')
            for c in ('Green', 'Cyan', 'Amber', 'Violet')]
    tex += [('AA_Tex_PbrHull', 'aa_pbr_hull.png'),
            ('AA_Tex_PbrDark', 'aa_pbr_dark.png'),
            ('AA_Tex_PbrAccent', 'aa_pbr_accent.png'),
            ('AA_Tex_Normal', 'aa_normal.png')]
    tex += [('AA_Tex_EmissiveGlow', 'aa_emissive_glow.png')]
    for tid, path in tex:
        out.append(f'\t<Texture Id="{tid}" Path="Content/Textures/{path}" Category="Vessel" />')
    out.append('')

    # shared materials
    out += [
        '\t<PbrMaterial Id="AA_Mat_Hull">',
        '\t\t<Diffuse Ref="AA_Tex_DiffHull" />',
        '\t\t<Normal Ref="AA_Tex_Normal" />',
        '\t\t<AoRoughMetal Ref="AA_Tex_PbrHull" />',
        '\t</PbrMaterial>',
        '\t<PbrMaterial Id="AA_Mat_Dark">',
        '\t\t<Diffuse Ref="AA_Tex_DiffDark" />',
        '\t\t<Normal Ref="AA_Tex_Normal" />',
        '\t\t<AoRoughMetal Ref="AA_Tex_PbrDark" />',
        '\t</PbrMaterial>',
    ]
    # per-rating accent + glow materials
    for color in ('Green', 'Cyan', 'Amber', 'Violet'):
        ck = COLOR_KEY[color]
        out += [
            f'\t<PbrMaterial Id="AA_Mat_Accent_{color}">',
            f'\t\t<Diffuse Ref="AA_Tex_DiffAccent_{color}" />',
            f'\t\t<Normal Ref="AA_Tex_Normal" />',
            f'\t\t<AoRoughMetal Ref="AA_Tex_PbrAccent" />',
            f'\t</PbrMaterial>',
            f'\t<PbrMaterial Id="AA_Mat_Glow_{color}">',
            f'\t\t<Diffuse Ref="AA_Tex_DiffAccent_{color}" />',
            f'\t\t<Normal Ref="AA_Tex_Normal" />',
            f'\t\t<AoRoughMetal Ref="AA_Tex_PbrAccent" />',
            f'\t\t<Emissive Ref="AA_Tex_EmissiveGlow" />',
            f'\t</PbrMaterial>',
        ]
    out.append('')

    # subparts: hull/dark shared; accent/glow per rating color
    def subpart(tid, mesh, mat):
        return [
            f'\t<SubPart Id="{tid}">',
            f'\t\t<Components>',
            f'\t\t\t<PartModel Key="{tid}_Model">',
            f'\t\t\t\t<Mesh Ref="{mesh}" />',
            f'\t\t\t\t<Material Ref="{mat}" />',
            f'\t\t\t</PartModel>',
            f'\t\t\t<MeshView>',
            f'\t\t\t\t<Mesh Ref="{mesh}" />',
            f'\t\t\t</MeshView>',
            f'\t\t</Components>',
            f'\t</SubPart>',
        ]

    out += subpart('AA_Subpart_Hull', 'AA_Hull', 'AA_Mat_Hull')
    out += subpart('AA_Subpart_Dark', 'AA_Dark', 'AA_Mat_Dark')
    for color in ('Green', 'Cyan', 'Amber', 'Violet'):
        out += subpart(f'AA_Subpart_Accent_{color}', 'AA_Accent',
                       f'AA_Mat_Accent_{color}')
        out += subpart(f'AA_Subpart_Glow_{color}', 'AA_Glow',
                       f'AA_Mat_Glow_{color}')
    for radial_tag in ('Compact', 'Heavy'):
        for material in ('Hull', 'Dark'):
            mesh = f'AA_Radial{radial_tag}_{material}'
            out += subpart(
                f'AA_Subpart_Radial{radial_tag}_{material}',
                mesh,
                f'AA_Mat_{material}',
            )
        for color in ('Green', 'Cyan', 'Amber', 'Violet'):
            accent_mesh = f'AA_Radial{radial_tag}_Accent'
            glow_mesh = f'AA_Radial{radial_tag}_Glow'
            out += subpart(
                f'AA_Subpart_Radial{radial_tag}_Accent_{color}',
                accent_mesh,
                f'AA_Mat_Accent_{color}',
            )
            out += subpart(
                f'AA_Subpart_Radial{radial_tag}_Glow_{color}',
                glow_mesh,
                f'AA_Mat_Glow_{color}',
            )
    out += subpart('AA_Subpart_RadialHeavy_CageHull',
                   'AA_RadialHeavy_CageHull', 'AA_Mat_Hull')
    out += subpart('AA_Subpart_RadialHeavy_CageDark',
                   'AA_RadialHeavy_CageDark', 'AA_Mat_Dark')
    out.append('')

    # parts: 16 standalone (4 sizes x 4 ratings), each self-contained
    for size_tag, dia, length, scale, conn_x, conn_scale in SIZES:
        for color, _g in RATINGS:
            out.append(f'\t<Part Id="{part_id(size_tag, color)}">')
            out.append('\t\t<SubParts>')
            out.append(subparts_block(fmt(scale), color))
            out.append('\t\t</SubParts>')
            out.append(connectors_block(fmt(conn_x), fmt(conn_scale)))
            out.append('\t\t<Components>')
            out.append(collider_block(fmt(length), fmt(dia / 2)))
            out.append('\t\t</Components>')
            out.append('\t</Part>')
            out.append('')

    # Radial parts attach through one surface-facing connector at the back.
    for radial_tag, dia, depth, connector_scale, col_x, col_y, col_z, center_z in RADIALS:
        for color, _g in RATINGS:
            pid = radial_part_id(radial_tag, color)
            out.append(f'\t<Part Id="{pid}">')
            out.append('\t\t<SubParts>')
            out.append(radial_subparts_block(
                radial_tag, color, include_cage=True))
            out.append('\t\t</SubParts>')
            if radial_tag == 'Heavy':
                out.append('\t\t<Variants>')
                out.append(f'\t\t\t<Part Ref="{pid}" />')
                out.append(
                    f'\t\t\t<Part Ref="{radial_nocage_part_id(color)}" />'
                )
                out.append('\t\t</Variants>')
            out.append('\t\t<Connectors>')
            out.append('\t\t\t<Connector Key="_connectorRadial">')
            out.append('\t\t\t\t<Transform>')
            out.append('\t\t\t\t\t<Rotation Y="-1.5708" />')
            out.append(
                f'\t\t\t\t\t<Scale X="{fmt(connector_scale)}" '
                f'Y="{fmt(connector_scale)}" Z="{fmt(connector_scale)}" />'
            )
            out.append('\t\t\t\t</Transform>')
            out.append('\t\t\t</Connector>')
            out.append('\t\t</Connectors>')
            out.append('\t\t<Components>')
            out.append(radial_collider_block(col_x, col_y, col_z, center_z))
            out.append('\t\t</Components>')
            out.append('\t</Part>')
            out.append('')
            if radial_tag == 'Heavy':
                out.append(
                    f'\t<Part Id="{radial_nocage_part_id(color)}" Parent="{pid}">'
                )
                out.append('\t\t<SubParts>')
                out.append(radial_subparts_block(
                    radial_tag, color, include_cage=False))
                out.append('\t\t</SubParts>')
                out.append('\t</Part>')
                out.append('')

    out.append('</Assets>')
    out.append('')
    return '\n'.join(out)


def build_gamedata():
    out = ['<?xml version="1.0" encoding="utf-8"?>', '<Assets>',
           '\t<!-- Acceleration absorber parts - gameplay data.',
           '\t     Stats from PARTS_AND_BALANCE.md variant table;',
           '\t     PowerPerTonne=1, delay 10-30 ms on all variants. -->',
           '\t<EditorTagDef Id="Absorbers" />']
    for size_tag, dia, length, _scale, _cx, _cs in SIZES:
        mass = DENSITY_KG_M3 * math.pi * (dia / 2) ** 2 * length
        for color, gload in RATINGS:
            base_w, k, max_w = STATS[(size_tag, color)]
            pid = part_id(size_tag, color)
            out += [
                f'\t<Part Id="{pid}" Modify="Patch" DisplayName="{display_name(size_tag, color, gload)}">',
                '\t\t<EditorTags Modify="Extend">',
                '\t\t\t<EditorTag Ref="Absorbers" />',
                '\t\t\t<EditorTag Ref="Structural" />',
                '\t\t\t<EditorTag Ref="NoFaceSnapping" />',
                '\t\t</EditorTags>',
                '\t\t<Diameters Modify="Extend">',
                f'\t\t\t<Diameter M="{fmt(dia)}" />',
                '\t\t</Diameters>',
                '\t\t<InertMasses Modify="Extend">',
                '\t\t\t<SolidCylinderMass>',
                f'\t\t\t\t<Mass Kg="{mass:.3f}" />',
                f'\t\t\t\t<Length M="{fmt(length)}" />',
                f'\t\t\t\t<Radius M="{fmt(dia / 2)}" />',
                '\t\t\t</SolidCylinderMass>',
                '\t\t</InertMasses>',
                '\t\t<Components Modify="Extend">',
                f'\t\t\t<AccelerationAbsorber MaxGLoad="{gload}" MinDelayMs="10" MaxDelayMs="30">',
                f'\t\t\t\t<BasePower W="{fmt(base_w)}" />',
                f'\t\t\t\t<MaxPower W="{fmt(max_w)}" />',
                f'\t\t\t\t<PowerPerGLoad W="{fmt(k)}" />',
                f'\t\t\t\t<PowerPerTonne W="1" />',
                '\t\t\t</AccelerationAbsorber>',
                '\t\t</Components>',
                '\t</Part>',
            ]
    for radial_tag, dia, depth, _connector_scale, _x, _y, _z, _center_z in RADIALS:
        mass = DENSITY_KG_M3 * math.pi * (dia / 2) ** 2 * depth
        for color, _gload in RATINGS:
            max_g, base_w, k, max_w = RADIAL_STATS[(radial_tag, color)]
            pid = radial_part_id(radial_tag, color)
            out += [
                f'\t<Part Id="{pid}" Modify="Patch" DisplayName="{radial_display_name(radial_tag, color, max_g)}">',
                '\t\t<EditorTags Modify="Extend">',
                '\t\t\t<EditorTag Ref="Absorbers" />',
                '\t\t\t<EditorTag Ref="Radial" />',
                '\t\t</EditorTags>',
                '\t\t<Connectors Modify="Extend">',
                '\t\t\t<Connector Key="_connectorRadial" Modify="Patch">',
                '\t\t\t\t<Flags>ToSurface</Flags>',
                '\t\t\t</Connector>',
                '\t\t</Connectors>',
                '\t\t<InertMasses Modify="Extend">',
                '\t\t\t<SolidCylinderMass>',
                f'\t\t\t\t<Mass Kg="{mass:.3f}" />',
                f'\t\t\t\t<LocationAsmb Z="{fmt(-depth / 2)}" />',
                '\t\t\t\t<Paf2Asmb Y="1.5708" />',
                f'\t\t\t\t<Length M="{fmt(depth)}" />',
                f'\t\t\t\t<Radius M="{fmt(dia / 2)}" />',
                '\t\t\t</SolidCylinderMass>',
                '\t\t</InertMasses>',
                '\t\t<Components Modify="Extend">',
                f'\t\t\t<AccelerationAbsorber MaxGLoad="{max_g}" MinDelayMs="10" MaxDelayMs="30">',
                f'\t\t\t\t<BasePower W="{fmt(base_w)}" />',
                f'\t\t\t\t<MaxPower W="{fmt(max_w)}" />',
                f'\t\t\t\t<PowerPerGLoad W="{fmt(k)}" />',
                '\t\t\t\t<PowerPerTonne W="1" />',
                '\t\t\t</AccelerationAbsorber>',
                '\t\t</Components>',
                '\t</Part>',
            ]
    out.append('</Assets>')
    out.append('')
    return '\n'.join(out)


with open(ASSETS_PATH, 'w', encoding='utf-8', newline='\n') as f:
    f.write(build_assets())
with open(GAMEDATA_PATH, 'w', encoding='utf-8', newline='\n') as f:
    f.write(build_gamedata())
print('wrote', ASSETS_PATH)
print('wrote', GAMEDATA_PATH)
