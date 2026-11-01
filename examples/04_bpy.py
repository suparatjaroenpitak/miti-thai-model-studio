# ใช้ Blender Python API (bpy) โดยตรงได้ทั้งหมด
# ตัวอย่าง mesh กำหนดจุดและหน้าด้วยโค้ด
import bpy

จุด = [(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0), (0, 0, 2)]
หน้า = [(0, 3, 2, 1), (0, 1, 4), (1, 2, 4), (2, 3, 4), (3, 0, 4)]
mesh = bpy.data.meshes.new("พีระมิด_mesh")
mesh.from_pydata(จุด, [], หน้า)
mesh.update()
obj = bpy.data.objects.new("พีระมิด", mesh)
bpy.context.collection.objects.link(obj)
obj.data.materials.append(วัสดุ("สีทอง", color=(0.95, 0.61, 0.16, 1), metallic=0.55))
ขอบมน(obj, width=0.05, segments=3)
