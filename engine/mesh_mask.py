"""Vertex-group masks shared by the sculpt brush and the GUI mask panel.

The mask is ordinary mesh data (a vertex group named ``MitiMask``), so it is
saved in the .blend and exported like any other model data.
"""

import bpy

MASK_GROUP = 'MitiMask'
AXES = ('X', 'Y', 'Z')


def _object(object_name):
    obj = bpy.data.objects.get(object_name)
    if obj is None or obj.type != 'MESH':
        raise ValueError('ไม่พบเมชสำหรับมาสก์: ' + str(object_name))
    if obj.data.users > 1:
        obj.data = obj.data.copy()
    return obj


def _group(obj, create=True):
    group = obj.vertex_groups.get(MASK_GROUP)
    if group is None and create:
        group = obj.vertex_groups.new(name=MASK_GROUP)
    return group


def read_weights(obj):
    """Return one weight per vertex (0 when the group or vertex is unmasked)."""
    group = _group(obj, create=False)
    weights = [0.0] * len(obj.data.vertices)
    if group is None:
        return weights
    index = group.index
    for vertex in obj.data.vertices:
        for element in vertex.groups:
            if element.group == index:
                weights[vertex.index] = float(element.weight)
                break
    return weights


def _write_weights(obj, weights):
    group = _group(obj)
    for vertex, weight in zip(obj.data.vertices, weights):
        weight = max(0.0, min(1.0, float(weight)))
        if weight > 0.0005:
            group.add([vertex.index], weight, 'REPLACE')
        else:
            try:
                group.remove([vertex.index])
            except RuntimeError:
                pass
    bpy.context.view_layer.update()


def mask_set(object_name, value=1.0, axis='', minimum=None, maximum=None, invert=False):
    """Mask the whole mesh or the part of it between two world-space limits."""
    obj = _object(object_name)
    value = max(0.0, min(1.0, float(value)))
    axis = (axis or '').upper()[:1]
    if axis and axis not in AXES:
        raise ValueError('แกนมาสก์ต้องเป็น X, Y หรือ Z')
    weights = []
    for vertex in obj.data.vertices:
        inside = True
        if axis:
            coordinate = (obj.matrix_world @ vertex.co)[AXES.index(axis)]
            if minimum is not None and coordinate < minimum:
                inside = False
            if maximum is not None and coordinate > maximum:
                inside = False
        weight = value if inside else 0.0
        weights.append(value - weight if invert else weight)
    _write_weights(obj, weights)
    return obj


def mask_clear(object_name):
    obj = _object(object_name)
    group = _group(obj, create=False)
    if group is not None:
        obj.vertex_groups.remove(group)
    bpy.context.view_layer.update()
    return obj


def mask_invert(object_name):
    obj = _object(object_name)
    _write_weights(obj, [1.0 - weight for weight in read_weights(obj)])
    return obj


def mask_feather(object_name, iterations=3):
    """Soften mask edges by averaging weights across connected vertices."""
    obj = _object(object_name)
    iterations = int(iterations)
    if not 1 <= iterations <= 40:
        raise ValueError('จำนวนครั้งการเบลอมาสก์ต้องอยู่ระหว่าง 1 ถึง 40')
    mesh = obj.data
    neighbours = [[] for _ in mesh.vertices]
    for edge in mesh.edges:
        a, b = edge.vertices
        neighbours[a].append(b)
        neighbours[b].append(a)
    weights = read_weights(obj)
    for _ in range(iterations):
        previous = weights
        weights = [
            0.5 * previous[index] + 0.5 * (sum(previous[n] for n in linked) / len(linked))
            if linked else previous[index]
            for index, linked in enumerate(neighbours)
        ]
    _write_weights(obj, weights)
    return obj
