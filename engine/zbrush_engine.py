"""ZBrush-style geometry operations built on Blender's own engine.

These are emulations written for Miti Studio. They do not call ZBrush's kernel;
the optional native handoff is implemented separately in zbrush_open.py.
Every operation below produces ordinary Blender mesh data, which means results
are saved in the .blend, exported to GLB/OBJ/STL and visible in the viewport.
"""

import math
import random

import bmesh
import bpy
from mathutils import Euler, Matrix, Quaternion, Vector

try:
    from mesh_mask import read_weights
except ImportError:  # The runner always adds engine/ to sys.path first.
    read_weights = None

MASK_GROUP = 'MitiMask'
PAINT_ATTRIBUTE = 'MitiPaint'
PRIMITIVES = {
    'CUBE': 'primitive_cube_add',
    'SPHERE': 'primitive_uv_sphere_add',
    'ICOSPHERE': 'primitive_ico_sphere_add',
    'CYLINDER': 'primitive_cylinder_add',
    'CONE': 'primitive_cone_add',
    'TORUS': 'primitive_torus_add',
    'MONKEY': 'primitive_monkey_add',
}
AXES = {'X': Vector((1, 0, 0)), 'Y': Vector((0, 1, 0)), 'Z': Vector((0, 0, 1))}


def _mesh(name):
    obj = bpy.data.objects.get(name) if isinstance(name, str) else name
    if obj is None or obj.type != 'MESH':
        raise ValueError('ไม่พบเมช · Mesh not found: ' + str(name))
    if obj.data.users > 1:
        obj.data = obj.data.copy()
    return obj


def _select(*objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[-1]
    bpy.context.view_layer.update()


def _apply_modifier(obj, modifier):
    _select(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def _flatten_layers(obj):
    """Bake active shape keys into the mesh: Blender cannot apply modifiers on top of them.

    This mirrors freezing a ZBrush layer before a destructive operation: the
    result is kept, the layer stack is not.
    """
    if obj.data.shape_keys is None:
        return obj
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    baked = bpy.data.meshes.new_from_object(evaluated)
    baked.name = obj.data.name + '_baked'
    obj.data = baked
    bpy.context.view_layer.update()
    return obj


def _join(target, others):
    objects = [obj for obj in others if obj is not None and obj.name != target.name]
    if not objects:
        return target
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    target.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.context.view_layer.update()
    bpy.ops.object.join()
    return target


def _hex_color(value):
    text = str(value).strip().lstrip('#')
    if len(text) == 3:
        text = ''.join(channel * 2 for channel in text)
    if len(text) not in (6, 8):
        raise ValueError('สีต้องเป็น #RGB หรือ #RRGGBB · Colour must be #RGB or #RRGGBB')
    channels = [int(text[index:index + 2], 16) / 255.0 for index in range(0, len(text), 2)]
    linear = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in channels[:3]]
    return (linear[0], linear[1], linear[2], channels[3] if len(channels) == 4 else 1.0)


def _primitive(kind, location, size, rotation):
    kind = (kind or 'SPHERE').upper()
    if kind not in PRIMITIVES:
        raise ValueError('รูปทรงไม่รองรับ · Unsupported primitive: ' + kind)
    if kind == 'CUBE':
        bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    elif kind == 'SPHERE':
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=0.5,
                                             location=location, rotation=rotation)
    elif kind == 'ICOSPHERE':
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=0.5,
                                              location=location, rotation=rotation)
    elif kind == 'TORUS':
        bpy.ops.mesh.primitive_torus_add(major_segments=32, minor_segments=12, major_radius=0.5,
                                         minor_radius=0.2, location=location, rotation=rotation)
    else:
        getattr(bpy.ops.mesh, PRIMITIVES[kind])(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.scale = (size, size, size) if isinstance(size, (int, float)) else tuple(size)
    _select(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def insert_mesh(target_name, primitive='SPHERE', location=(0, 0, 0), size=1.0,
                rotation=(0, 0, 0), join=True, name=None):
    """Insert Mesh: stamp a primitive at a point and optionally weld it into the SubTool."""
    rotation = [math.radians(value) for value in rotation]
    fresh = _primitive(primitive, location, size, rotation)
    if name:
        fresh.name = name
    if not join or target_name is None:
        return fresh
    target = _mesh(target_name)
    if target.data.shape_keys is not None:
        raise ValueError('รวมเมชไม่ได้เมื่อวัตถุมี shape keys · Cannot join into an object with shape keys')
    fresh.name = (target.name + '_insert')
    return _join(target, [fresh])


def clip_plane(target_name, axis='X', position=0.0, keep='ABOVE', fill=True):
    """Clip Curve: bisect the mesh with a world-space plane and cap the opening."""
    obj = _mesh(target_name)
    axis = (axis or 'X').upper()[:1]
    if axis not in AXES:
        raise ValueError('แกนต้องเป็น X, Y หรือ Z · Axis must be X, Y or Z')
    keep = (keep or 'ABOVE').upper()
    if keep not in ('ABOVE', 'BELOW'):
        raise ValueError('ต้องเลือกด้าน ABOVE หรือ BELOW · Keep ABOVE or BELOW')
    inverse = obj.matrix_world.inverted_safe()
    plane_co = inverse @ (AXES[axis] * float(position))
    plane_no = (inverse.to_3x3() @ AXES[axis]).normalized()
    working = bmesh.new()
    try:
        working.from_mesh(obj.data)
        geometry = list(working.verts) + list(working.edges) + list(working.faces)
        bmesh.ops.bisect_plane(working, geom=geometry, dist=1e-5, plane_co=plane_co, plane_no=plane_no,
                               clear_outer=(keep == 'BELOW'), clear_inner=(keep == 'ABOVE'))
        if not working.verts:
            raise ValueError('การตัดทำให้ไม่มีเมชเหลือ · The clip removed the whole mesh')
        if fill:
            boundary = [edge for edge in working.edges if edge.is_boundary]
            if boundary:
                bmesh.ops.holes_fill(working, edges=boundary)
        working.to_mesh(obj.data)
    finally:
        working.free()
    obj.data.update()
    bpy.context.view_layer.update()
    return obj


def transpose(target_name, pivot=(0, 0, 0), move=(0, 0, 0), rotate=(0, 0, 0), scale=(1, 1, 1),
              radius=1.0, falloff='SMOOTH'):
    """Transpose: proportional move/rotate/scale around a world-space pivot."""
    obj = _mesh(target_name)
    pivot = Vector(pivot)
    move = Vector(move)
    radius = float(radius)
    if radius < 0:
        raise ValueError('รัศมีต้องไม่ติดลบ · Radius cannot be negative')
    rotation = Quaternion(Euler([math.radians(value) for value in rotate], 'XYZ'))
    scale = Vector(scale)
    inverse = obj.matrix_world.inverted_safe()
    for vertex in obj.data.vertices:
        world = obj.matrix_world @ vertex.co
        distance = (world - pivot).length
        if radius > 0 and distance >= radius:
            continue
        weight = 1.0 if radius <= 0 else 1.0 - distance / radius
        weight = max(0.0, min(1.0, weight))
        weight = weight * weight * (3 - 2 * weight) if (falloff or '').upper() == 'SMOOTH' else weight
        if weight <= 0.0:
            continue
        local_scale = Vector((1 + (scale.x - 1) * weight, 1 + (scale.y - 1) * weight, 1 + (scale.z - 1) * weight))
        offset = world - pivot
        for index in range(3):
            offset[index] *= local_scale[index]
        if rotation.angle:
            offset = Quaternion().slerp(rotation, weight) @ offset
        world = pivot + offset + move * weight
        vertex.co = inverse @ world
    obj.data.update()
    bpy.context.view_layer.update()
    return obj


def project_all(target_name, source_name, offset=0.0, mode='NEAREST_SURFACEPOINT'):
    """Project All: snap the target onto another mesh with Shrinkwrap."""
    target = _mesh(target_name)
    source = _mesh(source_name)
    _flatten_layers(target)
    mode = (mode or 'NEAREST_SURFACEPOINT').upper()
    if mode not in ('NEAREST_SURFACEPOINT', 'PROJECT', 'NEAREST_VERTEX'):
        raise ValueError('วิธีการฉายไม่รองรับ · Unsupported projection mode')
    modifier = target.modifiers.new('MitiProjectAll', 'SHRINKWRAP')
    modifier.target = source
    modifier.wrap_method = mode
    modifier.offset = float(offset)
    return _apply_modifier(target, modifier)


def surface_noise(target_name, strength=0.05, size=0.5, detail=2.0, seed=0):
    """Surface Noise: displace the surface with a procedural Clouds texture."""
    obj = _mesh(target_name)
    _flatten_layers(obj)
    texture = bpy.data.textures.new('MitiNoise', 'CLOUDS')
    texture.noise_scale = max(0.0001, float(size))
    texture.noise_depth = max(0, min(8, int(detail)))
    texture.noise_basis = 'BLENDER_ORIGINAL'
    modifier = obj.modifiers.new('MitiSurfaceNoise', 'DISPLACE')
    modifier.texture = texture
    modifier.strength = float(strength)
    modifier.mid_level = 0.5
    return _apply_modifier(obj, modifier)


def sculpt_layer(target_name, name='Sculpt Layer', axis='Z', amount=0.1, intensity=1.0, use_mask=True):
    """Sculpt Layer: store a normal-direction push as an editable shape key."""
    obj = _mesh(target_name)
    axis = (axis or 'Z').upper()
    if axis != 'NORMAL' and axis not in AXES:
        raise ValueError('แกนต้องเป็น X, Y, Z หรือ NORMAL · Axis must be X, Y, Z or NORMAL')
    key = obj.shape_key_add(name=str(name or 'Sculpt Layer'), from_mix=False)
    key.slider_min = -10.0
    key.slider_max = 10.0
    key.value = max(-10.0, min(10.0, float(intensity)))
    weights = read_weights(obj) if (use_mask and read_weights is not None
                                    and obj.vertex_groups.get(MASK_GROUP) is not None) else [0.0] * len(obj.data.vertices)
    direction = AXES.get(axis)
    for index, vertex in enumerate(obj.data.vertices):
        influence = 1.0 - weights[index]
        if influence <= 0.0:
            continue
        if axis == 'NORMAL':
            normal = vertex.normal.copy()
            if normal.length_squared < 1e-12:
                normal = Vector((0, 0, 1))
            normal.normalize()
        else:
            normal = direction
        key.data[index].co = vertex.co + normal * (float(amount) * influence)
    obj.data.update()
    bpy.context.view_layer.update()
    return key


def zsphere(name='ZSphere', points=((0, 0, 0), (0, 0, 1)), radii=(0.4, 0.3), resolution=0.06):
    """ZSpheres: build an organic base mesh from a chain of blended spheres."""
    if len(points) < 2:
        raise ValueError('ต้องมีอย่างน้อยสองจุด · At least two points are required')
    if len(points) != len(radii):
        raise ValueError('จำนวนจุดกับรัศมีต้องเท่ากัน · Points and radii must match')
    metaball = bpy.data.metaballs.new('MitiZSphereData')
    metaball.resolution = max(0.01, min(1.0, float(resolution)))
    metaball.render_resolution = metaball.resolution
    metaball.threshold = 0.6
    obj = bpy.data.objects.new(str(name or 'ZSphere'), metaball)
    bpy.context.scene.collection.objects.link(obj)
    for point, radius in zip(points, radii):
        element = metaball.elements.new()
        element.co = Vector(point)
        element.radius = max(0.01, float(radius))
    _select(obj)
    bpy.ops.object.convert(target='MESH')
    result = bpy.context.object
    result.name = str(name or 'ZSphere')
    for poly in result.data.polygons:
        poly.use_smooth = True
    bpy.context.view_layer.update()
    return result


def fibermesh(target_name, count=80, length=0.25, radius=0.008, seed=1):
    """FiberMesh: grow tapered strands along surface normals as real curve geometry."""
    obj = _mesh(target_name)
    mesh = obj.data
    if not mesh.polygons:
        raise ValueError('เมชยังไม่มีหน้าให้สร้างเส้นใย · The mesh has no faces')
    count = max(1, min(2000, int(count)))
    randomizer = random.Random(int(seed))
    curves = bpy.data.curves.new('MitiFiberMesh', 'CURVE')
    curves.dimensions = '3D'
    curves.bevel_depth = max(0.0001, float(radius))
    curves.bevel_resolution = 1
    curves.resolution_u = 2
    matrix = obj.matrix_world
    for _ in range(count):
        polygon = mesh.polygons[randomizer.randrange(len(mesh.polygons))]
        local = polygon.center + Vector((randomizer.uniform(-0.6, 0.6), randomizer.uniform(-0.6, 0.6),
                                         randomizer.uniform(-0.6, 0.6))) * polygon.area ** 0.5
        normal = polygon.normal.copy()
        if normal.length_squared < 1e-12:
            continue
        normal.normalize()
        tangent = normal.cross(Vector((0, 0, 1)))
        if tangent.length_squared < 1e-12:
            tangent = normal.cross(Vector((1, 0, 0)))
        tangent.normalize()
        strand = curves.splines.new('POLY')
        strand.points.add(2)
        base = matrix @ local
        world_normal = (matrix.to_3x3() @ normal).normalized()
        bend = (matrix.to_3x3() @ tangent).normalized()
        strand.points[0].co = (base.x, base.y, base.z, 1.0)
        middle = base + world_normal * (float(length) * 0.55) + bend * (float(length) * 0.18)
        strand.points[1].co = (middle.x, middle.y, middle.z, 1.0)
        tip = base + world_normal * float(length) + bend * (float(length) * 0.45)
        strand.points[2].co = (tip.x, tip.y, tip.z, 1.0)
    result = bpy.data.objects.new('MitiFiberMesh', curves)
    bpy.context.scene.collection.objects.link(result)
    bpy.context.view_layer.update()
    return result


def nanomesh(target_name, instance_name=None, count=40, scale=0.12, seed=2):
    """NanoMesh: scatter scaled copies of an instance mesh over the surface."""
    target = _mesh(target_name)
    if not target.data.polygons:
        raise ValueError('เมชยังไม่มีหน้าให้กระจายชิ้นงาน · The mesh has no faces')
    temporary = not instance_name
    instance = _primitive('ICOSPHERE', (0, 0, 0), 1.0, (0, 0, 0)) if temporary else _mesh(instance_name)
    instance_matrix = instance.matrix_world.copy()
    copies = []
    randomizer = random.Random(int(seed))
    count = max(1, min(500, int(count)))
    matrix = target.matrix_world
    for _ in range(count):
        polygon = target.data.polygons[randomizer.randrange(len(target.data.polygons))]
        center = matrix @ (polygon.center + Vector((randomizer.uniform(-0.5, 0.5), randomizer.uniform(-0.5, 0.5),
                                                    randomizer.uniform(-0.5, 0.5))) * polygon.area ** 0.5)
        normal = (matrix.to_3x3() @ polygon.normal).normalized()
        rotation = normal.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        copy = instance.copy()
        copy.data = instance.data.copy()
        copy.matrix_world = (Matrix.Translation(center) @ rotation
                             @ instance_matrix @ Matrix.Scale(float(scale), 4))
        bpy.context.scene.collection.objects.link(copy)
        copies.append(copy)
    if temporary:
        bpy.data.objects.remove(instance, do_unlink=True)
    return _join(target, copies)


def array_mesh(target_name, count=5, offset=(0.6, 0, 0), rotate=(0, 0, 0), scale=1.0, radial=False):
    """ArrayMesh: repeat the SubTool in a line or around an axis."""
    obj = _mesh(target_name)
    count = max(1, min(200, int(count)))
    offset = Vector(offset)
    step_rotation = Euler([math.radians(value) for value in rotate], 'XYZ').to_quaternion()
    copies = []
    matrix = obj.matrix_world.copy()
    for index in range(1, count):
        copy = obj.copy()
        copy.data = obj.data.copy()
        turn = Matrix.Rotation(step_rotation.angle * index, 4, step_rotation.axis) if step_rotation.angle else Matrix.Identity(4)
        placement = Matrix.Translation(offset * index) @ turn @ matrix
        if radial:
            placement = Matrix.Rotation(step_rotation.angle * index if step_rotation.angle else 0.0,
                                        4, 'Z') @ Matrix.Translation(offset * index) @ matrix
        copy.matrix_world = placement @ Matrix.Scale(float(scale) ** index, 4)
        bpy.context.scene.collection.objects.link(copy)
        copies.append(copy)
    return _join(obj, copies)


def polypaint(target_name, color='#FF7A29', axis='', minimum=None, maximum=None, use_mask=True, clear=False):
    """PolyPaint: paint vertex colours, optionally limited by axis range or mask."""
    obj = _mesh(target_name)
    mesh = obj.data
    attribute = mesh.color_attributes.get(PAINT_ATTRIBUTE)
    if attribute is None:
        attribute = mesh.color_attributes.new(name=PAINT_ATTRIBUTE, type='FLOAT_COLOR', domain='POINT')
    if not clear:
        rgba = _hex_color(color)
        weights = read_weights(obj) if (use_mask and read_weights is not None
                                        and obj.vertex_groups.get(MASK_GROUP) is not None) else [0.0] * len(mesh.vertices)
        axis = (axis or '').upper()[:1]
        if axis and axis not in AXES:
            raise ValueError('แกนต้องเป็น X, Y หรือ Z · Axis must be X, Y or Z')
        for vertex in mesh.vertices:
            coordinate = (obj.matrix_world @ vertex.co)['XYZ'.index(axis)] if axis else 0.0
            if axis and minimum is not None and coordinate < float(minimum):
                continue
            if axis and maximum is not None and coordinate > float(maximum):
                continue
            if 1.0 - weights[vertex.index] <= 0.0:
                continue
            attribute.data[vertex.index].color = rgba
        mesh.color_attributes.active_color = attribute
    mesh.update()
    bpy.context.view_layer.update()
    return attribute


def subtool_boolean(target_name, tool_name, operation='UNION'):
    """SubTool Boolean: combine two meshes and remove the tool."""
    target = _mesh(target_name)
    tool = _mesh(tool_name)
    _flatten_layers(target)
    if target.name == tool.name:
        raise ValueError('ต้องเลือกวัตถุคนละชิ้น · Choose two different objects')
    operation = (operation or 'UNION').upper()
    if operation not in ('UNION', 'DIFFERENCE', 'INTERSECT'):
        raise ValueError('การรวมไม่รองรับ · Unsupported boolean operation')
    modifier = target.modifiers.new('MitiSubTool', 'BOOLEAN')
    modifier.operation = operation
    modifier.solver = 'EXACT'
    modifier.object = tool
    _apply_modifier(target, modifier)
    bpy.data.objects.remove(tool, do_unlink=True)
    return target


def import_mesh(path, name=None):
    """Import an OBJ/GLB/STL/FBX mesh file produced elsewhere."""
    from pathlib import Path
    source = Path(str(path))
    if not source.is_file():
        raise ValueError('ไม่พบไฟล์ · File not found: ' + str(source))
    suffix = source.suffix.lower()
    if suffix == '.obj':
        bpy.ops.wm.obj_import(filepath=str(source))
    elif suffix in ('.glb', '.gltf'):
        bpy.ops.import_scene.gltf(filepath=str(source))
    elif suffix == '.stl':
        bpy.ops.wm.stl_import(filepath=str(source))
    elif suffix == '.fbx':
        bpy.ops.import_scene.fbx(filepath=str(source))
    else:
        raise ValueError('ชนิดไฟล์ไม่รองรับ · Unsupported file type: ' + suffix)
    imported = [obj for obj in bpy.context.selected_objects if obj.type == 'MESH']
    if not imported:
        raise ValueError('ไฟล์ไม่มีเมช · The file contained no mesh')
    if name:
        imported[0].name = str(name)
    bpy.context.view_layer.update()
    return imported[0]
