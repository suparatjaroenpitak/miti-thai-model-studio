"""Apply bilingual sculpt palette strokes to a Blender mesh without UI context."""

import math

import bmesh
import bpy
from mathutils import Vector


BRUSHES = {'DRAW', 'STANDARD', 'CLAY', 'INFLATE', 'SMOOTH', 'FLATTEN', 'PINCH', 'MOVE', 'CREASE'}


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
        if path and not any((path[0][0] - existing[0][0]).length_squared < 1e-10 for existing in paths):
            paths.append(path)
    return paths


def sculpt_stroke(object_name, samples, radius=0.8, strength=0.5, mode='DRAW', detail=3,
                  subtract=False, symmetry='', focal_shift=0):
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
    if mode == 'SMOOTH':
        adjacency = [[] for _ in mesh.vertices]
        for edge in mesh.edges:
            a, b = edge.vertices
            adjacency[a].append(b)
            adjacency[b].append(a)

    sign = -1 if subtract else 1
    exponent = 2 * (2 ** (focal_shift / 50))
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
                falloff = (1 - distance / radius) ** (exponent * (0.6 if mode == 'CLAY' else 1))
                influence = strength * falloff
                if mode == 'SMOOTH':
                    neighbors = adjacency[vertex.index]
                    if neighbors:
                        average = sum((positions[index] for index in neighbors), Vector()) / len(neighbors)
                        vertex.co = positions[vertex.index].lerp(average, influence)
                elif mode == 'MOVE':
                    vertex.co += (inverse_direction @ drag) * (influence * sign)
                elif mode == 'FLATTEN':
                    height = offset.dot(normal)
                    vertex.co -= (inverse_direction @ normal) * (height * influence * sign)
                elif mode == 'PINCH':
                    tangent = -offset + normal * offset.dot(normal)
                    vertex.co += (inverse_direction @ tangent) * (influence * 0.55 * sign)
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
