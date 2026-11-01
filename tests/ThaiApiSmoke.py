from pathlib import Path
import json,sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'engine'))
from thai_api import *

for obj in list(bpy.data.objects): bpy.data.objects.remove(obj,do_unlink=True)
result = คำสั่ง('ตาข่าย.เพิ่มลูกบาศก์', ขนาด=2, ตำแหน่ง=(1,2,3))
assert result == {'FINISHED'}
obj=bpy.context.object
assert tuple(obj.location)==(1,2,3)
assert ของจริง(เบลนเดอร์.บริบท.วัตถุ) == obj
ตั้งค่า(obj, ชื่อ='ทดสอบภาษาไทย', ตำแหน่ง=(2,3,4))
assert อ่านค่า(obj,'ชื่อ') == 'ทดสอบภาษาไทย'
ไทย(obj).ตำแหน่ง=(3,4,5)
assert tuple(obj.location)==(3,4,5)
mat=เบลนเดอร์.ข้อมูล.วัสดุ.สร้าง(ชื่อ='วัสดุไทย')
assert isinstance(mat,bpy.types.Material)
ตั้งค่า(mat, ใช้โหนด=True)
assert mat.use_nodes
copy=เรียกฟังก์ชัน(bpy.data.meshes,'สร้าง',ชื่อ='ตาข่ายไทย')
assert isinstance(copy,bpy.types.Mesh)
other=เรียกAPI('bpy.data.objects.new', ชื่อ='วัตถุสร้าง', ข้อมูลวัตถุ=copy)
assert isinstance(other,bpy.types.Object)
เรียกฟังก์ชัน(bpy.context.scene.collection.objects,'เชื่อม',other)
assert other.name in bpy.context.scene.objects
bm=เรียกAPI('bmesh.new')
เรียกAPI('bmesh.ops.create_cube',bm,ขนาด=1)
assert len(bm.verts)==8
bm.free()
vec=เรียกAPI('mathutils.Vector',(1,2,3))
assert tuple(vec)==(1,2,3)
# Catalog-provided identifiers and enum labels are tested when available.
matches=ค้นหาคำสั่ง('bpy.ops.object.modifier_add',limit=1)
if matches:
 entry=matches[0]
 parameter=next(p for p in entry['parameters'] if p['id']=='type')
 item=next(v for v in parameter['enumItems'] if v['id']=='BEVEL')
 result=คำสั่ง(entry.get('thaiPath') or entry['id'], **{parameter.get('thaiIdentifier') or 'type':item.get('thaiIdentifier') or item.get('thaiName') or 'BEVEL'})
 assert result=={'FINISHED'} and obj.modifiers[-1].type=='BEVEL'
 print('CATALOG_ALIAS_ENUM_PASS', len(ค้นหาคำสั่ง('',limit=None)))
try:
 คำสั่ง('ไม่มีคำสั่งนี้')
 raise AssertionError('unknown operator accepted')
except LookupError: pass
try:
 คำสั่ง('bpy.ops.object.modifier_add',ชนิดข้อมูล='INVALID')
 raise AssertionError('invalid enum accepted')
except TypeError as e:
 assert any('เรียก Blender API' in note for note in e.__notes__)
print('THAI_API_SMOKE_PASS')
