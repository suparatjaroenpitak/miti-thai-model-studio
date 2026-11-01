"""Small, friendly modeling helpers. Every returned object is a real bpy object."""
import math
import bpy


def clear():
    """Remove objects from the isolated build scene."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def material(name="วัสดุ", color=(0.35, 0.65, 0.95, 1.0), roughness=0.45, metallic=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    if isinstance(color, str):
        code = color.lstrip('#')
        if len(code) == 3:
            code = ''.join(c * 2 for c in code)
        if len(code) not in (6, 8):
            raise ValueError('สีต้องเป็น #RGB, #RRGGBB หรือ #RRGGBBAA')
        channels = [int(code[i:i + 2], 16) / 255.0 for i in range(0, len(code), 2)]
        # Blender shader inputs use linear RGB; hex colors use sRGB.
        rgb = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in channels[:3]]
        rgba = tuple(rgb) + (channels[3] if len(channels) == 4 else 1.0,)
    else:
        rgba = tuple(color) + ((1.0,) if len(color) == 3 else ())
    mat.diffuse_color = rgba
    node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = rgba
    node.inputs['Roughness'].default_value = roughness
    node.inputs['Metallic'].default_value = metallic
    return mat


def _finish(obj, name, material):
    obj.name = name
    if material is not None:
        obj.data.materials.append(material)
    return obj


def cube(name="กล่อง", size=(1, 1, 1), location=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.scale = (size, size, size) if isinstance(size, (int, float)) else size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _finish(obj, name, material)


def sphere(name="ทรงกลม", radius=1, location=(0, 0, 0), material=None, segments=32, rings=16):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius, location=location)
    return smooth(_finish(bpy.context.object, name, material))


def cylinder(name="ทรงกระบอก", radius=1, depth=2, location=(0, 0, 0), material=None, vertices=48):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location)
    return _finish(bpy.context.object, name, material)


def cone(name="กรวย", radius=1, depth=2, location=(0, 0, 0), material=None, vertices=48, radius_top=0):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius, radius2=radius_top, depth=depth, location=location)
    return _finish(bpy.context.object, name, material)


def torus(name="วงแหวน", radius=1, thickness=0.25, location=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_torus_add(major_segments=48, minor_segments=16, major_radius=radius, minor_radius=thickness, location=location)
    return smooth(_finish(bpy.context.object, name, material))


def plane(name="พื้น", size=10, location=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_plane_add(size=size, location=location)
    return _finish(bpy.context.object, name, material)


def move(obj, location):
    obj.location = location
    return obj


def rotate(obj, degrees):
    """Set XYZ Euler rotation in degrees."""
    obj.rotation_euler = [math.radians(v) for v in degrees]
    return obj


def scale(obj, size):
    obj.scale = (size, size, size) if isinstance(size, (int, float)) else size
    return obj


def duplicate(obj, name=None, location=None):
    copy = obj.copy()
    if obj.data is not None:
        copy.data = obj.data.copy()
    bpy.context.collection.objects.link(copy)
    if name is not None:
        copy.name = name
    if location is not None:
        copy.location = location
    return copy


def bevel(obj, width=0.1, segments=3):
    mod = obj.modifiers.new("ขอบมน", 'BEVEL')
    mod.width = width
    mod.segments = segments
    return obj


def smooth(obj):
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def boolean(obj, cutter, operation='DIFFERENCE', hide_cutter=True):
    mod = obj.modifiers.new("ตัดรวมรูปทรง", 'BOOLEAN')
    mod.operation = operation
    mod.object = cutter
    if hide_cutter:
        cutter.hide_render = True
        cutter.hide_viewport = True
    return obj


กล่อง = cube
ทรงกลม = sphere
ทรงกระบอก = cylinder
กรวย = cone
วงแหวน = torus
พื้น = plane
วัสดุ = material
ย้าย = move
หมุน = rotate
ปรับขนาด = scale
ทำซ้ำ = duplicate
ขอบมน = bevel
ผิวเรียบ = smooth
ล้างฉาก = clear
ตัดรวม = boolean

__all__ = [name for name in list(globals()) if not name.startswith('_') and name not in ('math', 'bpy')]
