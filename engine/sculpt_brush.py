"""Apply bilingual sculpt palette strokes to a Blender mesh without UI context.

The brush set covers the ZBrush-style names offered by the app; each one is a
deterministic displacement of the mesh around the recorded stroke, evaluated
with Blender's mesh data. A vertex-group mask, when present, weakens the brush.
"""

import math

import bmesh
import bpy
from mathutils import Vector

try:
    from mesh_mask import MASK_GROUP, read_weights
except ImportError:  # The runner always adds engine/ to sys.path first.
    MASK_GROUP = 'MitiMask'
    read_weights = None


BRUSHES = {
    'DRAW', 'STANDARD', 'CLAY', 'CLAY_STRIPS', 'CLAY_TUBES', 'INFLATE', 'SMOOTH',
    'FLATTEN', 'PINCH', 'MOVE', 'CREASE', 'DAM_STANDARD', 'TRIM_DYNAMIC',
    'SNAKE_HOOK', 'POLISH', 'LAYER', 'FILL',
}


def _mirrored_samples(obj, inverse, samples, symmetry):
    axes = [axis for axis in 'XYZ' if axis in symmetry]
    normal_to_local = obj.matrix_world.to_3x3().transposed()
    normal_to_world = inverse.to_3x3().transposed()
    paths = []
    for mask in range(1 << len(axes)):
        path = []
        for center_values, normal_values in samples:
            local_center = inverse @ Vector(center_values)
            local_normal = normal_to_local @ Vector(normal_values)
            for bit, axis in enumerate(axes):
                if mask & (1 << bit):
                    local_center['XYZ'.index(axis)] *= -1
                    local_normal['XYZ'.index(axis)] *= -1
            world_center = obj.matrix_world @ local_center
            world_normal = normal_to_world @ local_normal
            if world_normal.length_squared < 1e-12:
                continue
            world_normal.normalize()
            path.append((world_center, world_normal))
        if path and not any(len(path) == len(existing) and all(
                (point[0] - other[0]).length_squared < 1e-10 for point, other in zip(path, existing))
                for existing in paths):
            paths.append(path)
    return paths


def sculpt_stroke(object_name, samples, radius=0.8, strength=0.5, mode='DRAW', detail=3,
                  subtract=False, symmetry='', focal_shift=0, mask=MASK_GROUP):
    obj = bpy.data.objects.get(object_name)
    if obj is None or obj.type != 'MESH':
        raise ValueError('ไม่พบเมชสำหรับปั้น: ' + object_name)
    mode = mode.upper()
    if mode not in BRUSHES:
        raise ValueError('หัวแปรงไม่รองรับ: ' + mode)
    radius, strength, detail, focal_shift = float(radius), float(strength), int(detail), int(focal_shift)
    symmetry = ''.join(axis for axis in 'XYZ' if axis in symmetry.upper())
    if not math.isfinite(radius) or not math.isfinite(strength) or not 0.01 <= radius <= 100 \
            or not 0 <= strength <= 1 or not 0 <= detail <= 8 or not -100 <= focal_shift <= 100:
        raise ValueError('ค่าหัวแปรงอยู่นอกช่วงที่กำหนด')
    if not samples or len(samples) > 128:
        raise ValueError('จำนวนจุดรอยแปรงไม่ถูกต้อง')
    if obj.data.users > 1:
        obj.data = obj.data.copy()
    mesh = obj.data
    if detail and len(mesh.vertices) < 64 and len(mesh.polygons) <= 64:
        working = bmesh.new()
        try:
            working.from_mesh(mesh)
            bmesh.ops.subdivide_edges(working, edges=list(working.edges), cuts=detail, use_grid_fill=True)
            working.to_mesh(mesh)
        finally:
            working.free()
        mesh.update()

    inverse = obj.matrix_world.inverted_safe()
    inverse_direction = inverse.to_3x3()
    normal_matrix = inverse_direction.transposed()
    paths = _mirrored_samples(obj, inverse, samples, symmetry)
    adjacency = None
    if mode in {'SMOOTH', 'POLISH'}:
        adjacency = [[] for _ in mesh.vertices]
        for edge in mesh.edges:
            a, b = edge.vertices
            adjacency[a].append(b)
            adjacency[b].append(a)

    mask_weights = None
    if mask and read_weights is not None and obj.vertex_groups.get(mask) is not None:
        mask_weights = read_weights(obj)

    sign = -1 if subtract else 1
    exponent = 2 * (2 ** (focal_shift / 50))
    if mode == 'DAM_STANDARD':
        exponent *= 1.7
    elif mode in {'LAYER', 'FILL'}:
        exponent *= 0.5
    for path in paths:
        previous_center = None
        for center, normal in path:
            positions = [vertex.co.copy() for vertex in mesh.vertices] if adjacency is not None else None
            drag = center - previous_center if previous_center is not None else Vector()
            previous_center = center
            for vertex in mesh.vertices:
                world_position = obj.matrix_world @ vertex.co
                offset = world_position - center
                distance = offset.length
                if distance >= radius:
                    continue
                falloff = (1 - distance / radius) ** (exponent * (0.6 if mode in {'CLAY', 'CLAY_STRIPS', 'CLAY_TUBES'} else 1))
                influence = strength * falloff
                if mask_weights is not None:
                    influence *= 1.0 - mask_weights[vertex.index]
                if influence <= 0.0:
                    continue
                if mode == 'SMOOTH':
                    neighbors = adjacency[vertex.index]
                    if neighbors:
                        average = sum((positions[index] for index in neighbors), Vector()) / len(neighbors)
                        vertex.co = positions[vertex.index].lerp(average, influence)
                elif mode == 'MOVE' or mode == 'SNAKE_HOOK':
                    vertex.co += (inverse_direction @ drag) * (influence * sign)
                    if mode == 'SNAKE_HOOK':
                        vertex.co += (inverse_direction @ normal) * (radius * 0.12 * influence * sign)
                elif mode == 'FLATTEN' or mode == 'TRIM_DYNAMIC':
                    height = offset.dot(normal)
                    strength_scale = 1.5 if mode == 'TRIM_DYNAMIC' else 1.0
                    vertex.co -= (inverse_direction @ normal) * (height * min(1.0, influence * strength_scale) * sign)
                elif mode == 'PINCH':
                    tangent = -offset + normal * offset.dot(normal)
                    vertex.co += (inverse_direction @ tangent) * (influence * 0.55 * sign)
                elif mode == 'POLISH':
                    neighbors = adjacency[vertex.index]
                    if neighbors:
                        average = sum((positions[index] for index in neighbors), Vector()) / len(neighbors)
                        vertex.co = positions[vertex.index].lerp(average, influence * 0.6)
                    height = offset.dot(normal)
                    vertex.co -= (inverse_direction @ normal) * (height * influence * 0.4 * sign)
                elif mode == 'DAM_STANDARD':
                    amount = radius * 0.35 * influence
                    tangent = -offset + normal * offset.dot(normal)
                    vertex.co += (inverse_direction @ tangent) * (influence * 0.3 * sign)
                    vertex.co += (inverse_direction @ normal) * (amount * sign)
                elif mode == 'CLAY_STRIPS' or mode == 'CLAY_TUBES':
                    amount = radius * (0.55 if mode == 'CLAY_TUBES' else 0.38) * influence
                    vertex.co += (inverse_direction @ normal) * (amount * sign)
                elif mode == 'LAYER':
                    vertex.co += (inverse_direction @ normal) * (radius * 0.1 * sign)
                elif mode == 'FILL':
                    vertex.co += (inverse_direction @ normal) * (radius * 0.18 * influence * sign)
                else:
                    world_normal = normal
                    if mode == 'INFLATE':
                        world_normal = normal_matrix @ vertex.normal
                        if world_normal.length_squared < 1e-12:
                            continue
                        world_normal.normalize()
                    amount = radius * (0.5 if mode == 'CLAY' else 0.3 if mode == 'CREASE' else 0.35) * influence
                    if mode == 'CREASE':
                        amount *= -1
                        tangent = -offset + normal * offset.dot(normal)
                        vertex.co += (inverse_direction @ tangent) * (influence * 0.25 * sign)
                    vertex.co += (inverse_direction @ world_normal) * (amount * sign)
            mesh.update()
    bpy.context.view_layer.update()
