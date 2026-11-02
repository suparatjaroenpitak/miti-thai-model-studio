import bpy

bpy.ops.mesh.primitive_cube_add()
_miti_obj = bpy.context.object
_miti_obj.name = 'CubeActionSmoke'

# This is the same sequence emitted by the right-side parameter panel.
_miti_obj = bpy.data.objects.get('CubeActionSmoke')
if _miti_obj is None: raise ValueError('ไม่พบวัตถุที่เลือก')
if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='DESELECT')
_miti_obj.select_set(True)
bpy.context.view_layer.objects.active = _miti_obj
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
คำสั่ง('mesh.subdivide', number_cuts=2)
bpy.ops.object.mode_set(mode='OBJECT')
assert len(_miti_obj.data.vertices) > 8, 'Subdivision did not change the mesh'
print('OBJECT_ACTION_SUCCESS', len(_miti_obj.data.vertices))
