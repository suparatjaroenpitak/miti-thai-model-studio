"""บริบทพื้นที่ทำงานและโหมด Blender ภาษาไทย"""
import bpy
import os, subprocess, json, shlex
from contextlib import contextmanager
_พื้นที่={"เค้าโครง":"Layout","สร้างโมเดล":"Modeling","ยูวี":"UV Editing","พื้นผิว":"Texture Paint","วัสดุ":"Shading","แอนิเมชัน":"Animation","เรนเดอร์":"Rendering","โหนดเรขาคณิต":"Geometry Nodes"}
_โหมด={"วัตถุ":"OBJECT","แก้ไข":"EDIT","ปั้น":"SCULPT","ระบายพื้นผิว":"TEXTURE_PAINT","ระบายจุดยอด":"VERTEX_PAINT","โพส":"POSE"}
def ตั้งพื้นที่ทำงาน(name):
 ws=bpy.data.workspaces.get(_พื้นที่.get(name,name))
 if ws and not bpy.app.background: bpy.context.window.workspace=ws
 return ws
def ตั้งโหมด(name,obj=None):
 if obj: bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 target=_โหมด.get(name,name);o=bpy.context.view_layer.objects.active
 if o and o.mode!=target:bpy.ops.object.mode_set(mode=target)
 return target
def โหมดเลือก(name):
 v={"จุดยอด":(True,False,False),"ขอบ":(False,True,False),"หน้า":(False,False,True)}.get(name)
 if v and hasattr(bpy.context,'tool_settings'):bpy.context.tool_settings.mesh_select_mode=v
 return v
def สร้างยูวี(method='สมาร์ต',angle=1.15192,margin=0.001):
    """สร้าง UV จริงใน Edit Mode ด้วยตัวดำเนินการของ Blender"""
    obj=bpy.context.object
    if not obj or obj.type!='MESH': return None
    was=obj.mode
    if was!='EDIT': bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    if str(method).lower() in ('สมาร์ต','smart','smart_project'):
        bpy.ops.uv.smart_project(angle_limit=float(angle),island_margin=float(margin))
    else:
        bpy.ops.uv.unwrap(method='ANGLE_BASED',margin=float(margin))
    if was!='EDIT': bpy.ops.object.mode_set(mode=was)
    return obj.data.uv_layers.active.name if obj.data.uv_layers.active else None
def ตั้งค่าปั้น(name,value):
    """ตั้งค่าปั้นแบบไม่พึ่งเมาส์ โดยแมปค่ากับ brush ของ Blender"""
    try: value=float(value)
    except (TypeError,ValueError): return value
    tool=bpy.context.tool_settings
    brush=getattr(tool,'sculpt',None)
    if name in ('ขนาดแปรง','รัศมี') and brush and hasattr(brush,'size'): brush.size=max(1,int(value*100))
    if name in ('ความแรงแปรง','ความแรง') and brush and hasattr(brush,'strength'): brush.strength=max(0.0,min(1.0,value))
    return value
def เรียก_blender_mcp_uvx(arguments=()):
    """เรียกเซิร์ฟเวอร์ Blender MCP ผ่าน uvx เมื่อผู้ใช้ตั้งค่า MITI_BLENDER_MCP_UVX"""
    command=os.environ.get('MITI_BLENDER_MCP_UVX','uvx blender-mcp')
    return subprocess.Popen(shlex.split(command)+list(arguments), stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
@contextmanager
def บริบท(editor='มุมมอง3มิติ',mode=None,selection=None,workspace=None,obj=None):
 if workspace:ตั้งพื้นที่ทำงาน(workspace)
 if mode:ตั้งโหมด(mode,obj)
 if selection:โหมดเลือก(selection)
 yield bpy.context
def เลือกองค์ประกอบ(obj,kind='จุดยอด',indices=()):
 if obj and obj.type=='MESH':
  bpy.context.view_layer.objects.active=obj;obj.select_set(True);ตั้งโหมด('แก้ไข',obj);โหมดเลือก(kind);bpy.ops.mesh.select_all(action='DESELECT');bpy.ops.object.mode_set(mode='OBJECT');seq={'จุดยอด':obj.data.vertices,'ขอบ':obj.data.edges,'หน้า':obj.data.polygons}.get(kind,())
  for i in indices:
   if 0<=i<len(seq):seq[i].select=True
  bpy.ops.object.mode_set(mode='EDIT')
 return obj
