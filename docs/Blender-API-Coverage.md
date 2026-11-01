# ขอบเขตการดึง API ของ Blender ทั้งชุดที่เปิดเผยให้ Python

ตรวจจาก **Blender 5.2.2 LTS** ที่ติดตั้งในเครื่อง เมื่อ 1 พฤศจิกายน 2026 โดยใช้ process ใหม่แบบ `--background --factory-startup` ไม่แตะฉากของหน้าต่าง Blender ที่อาจเปิดอยู่

การตรวจรอบนี้ต่อจากหลักฐาน REA ของไฟล์ติดตั้งใน [Blender-REA.md](Blender-REA.md) หลักฐาน REA เดิมยืนยันตัว executable และ PE resources ส่วนข้อมูล API ในเอกสารนี้ได้จาก **runtime introspection ผ่าน bpy/RNA และโมดูลที่จัดส่งมาด้วย** เป็นคนละชนิดหลักฐานกับ REA และไม่มีการ decompile native modeling kernel

## สิ่งที่ดึงได้

ไฟล์ข้อมูลต้นฉบับ: [rea/blender-api-catalog.json](rea/blender-api-catalog.json) มี schema version 1 และ SHA-256 ของ executable ผูกอยู่ใน metadata

| ชนิด | จำนวน |
| --- | ---: |
| Operators ทุกตัวที่ลงทะเบียนใน `bpy.ops` ของ factory startup | 2,499 |
| หมวด operators | 77 |
| RNA types ทุกตัวที่เปิดเผย `bl_rna` ใน `bpy.types` | 4,012 |
| Properties ที่ประกาศใน type เจ้าของ | 10,159 |
| RNA functions ที่ประกาศใน type เจ้าของ | 2,223 |
| Public routine methods ของ `bpy.types` ที่ไม่ได้ประกาศใน RNA รวม helper Python | 3,011 |
| Public callable bindings ในโมดูล/วัตถุที่สำรวจ | 785 |
| โมดูล/วัตถุที่สำรวจ | 36 |
| รวมรายการ API ที่มี key ไม่ซ้ำ | 22,689 |
| ตัวเลือก enum ในรายการและพารามิเตอร์ | 38,949 |
| ข้อผิดพลาดการดึงข้อมูลที่ยังไม่แก้ | 0 |

ข้อมูลแต่ละคำสั่งมีชื่อเดิม คำอธิบาย ชื่อแปลไทยจาก Blender (ถ้ามี) ชนิดพารามิเตอร์ ค่าเริ่มต้น required/read-only/array/subtype ตัวเลือก enum และชนิดข้อมูลอ้างอิงที่ RNA เปิดเผย สำหรับ Python/native bindings ที่ `inspect.signature()` อ่านไม่ได้ เก็บ docstring เดิมไว้แทน

Inherited properties/functions อยู่ที่ type ฐานโดยไม่ทำรายการซ้ำ และมี `parentType` เชื่อม type ลูกกลับไปยังฐาน การทดสอบเปรียบเทียบกับ runtime ใช้ขอบเขตเดียวกันทุกตัว

พบชื่อ RNA ซ้ำจริงหนึ่งตำแหน่งคือ `bpy.types.RenderEngine.render` ซึ่งเป็นได้ทั้ง property และ function เก็บ function เป็น ID ปกติ และ property มี key ลงท้าย `#property` พร้อม `apiPath` เดิม เพื่อไม่ทำข้อมูลชนิดใดชนิดหนึ่งหาย

## โมดูลที่สำรวจ

`bpy`, `bpy.context`, `bpy.data`, `bpy.app`, `bpy.app.handlers`, `bpy.app.timers`, `bpy.app.translations`, `bpy.utils`, `bpy.utils.previews`, `bpy.utils.units`, `bpy.path`, `bpy.props`, `bpy.msgbus`, `bmesh`, `bmesh.ops`, `bmesh.utils`, `bmesh.geometry`, `bmesh.types`, `mathutils`, `mathutils.geometry`, `mathutils.interpolate`, `mathutils.bvhtree`, `mathutils.kdtree`, `mathutils.noise`, `gpu`, `gpu.state`, `gpu.matrix`, `gpu.shader`, `gpu.texture`, `gpu.types`, `gpu.capabilities`, `gpu.platform`, `gpu.select`, `gpu.compute`, `blf`, `aud`

รวม public callable methods ของ native classes ใน `mathutils`, `bmesh.types`, `gpu.types` และโมดูลชนิดที่เกี่ยวข้อง โดยไม่เรียกใช้งานฟังก์ชันเหล่านั้นระหว่างสำรวจ

สำรวจ public routine members ที่ประกาศโดยตรงบนแต่ละ class ใน `bpy.types` เพิ่มจาก RNA functions ด้วย จึงรวม `Mesh.from_pydata` และ helper จาก `bpy_types.py` โดยไม่จัด property descriptors หรือ class ของ property เป็นฟังก์ชัน

## บริบทการรันคำสั่ง

ตรวจ `operator.poll()` แบบอ่านสถานะใน factory background โดยไม่ execute operators:

- 672 ตัวพร้อมใช้งานในบริบทตั้งต้นนี้
- 1,827 ตัวต้องเตรียมบริบทก่อน เช่น วัตถุที่เลือก โหมด Edit หรือพื้นที่หน้าต่างของ Blender
- ไม่มี poll ที่เกิด exception

ผล false ไม่ได้แปลว่า Blender ไม่มีฟังก์ชันนั้น และไม่ได้พิสูจน์ว่าคำสั่งนั้นต้องใช้ GUI เสมอไป การทำงานจริงขึ้นกับบริบทขณะเรียก คำสั่งที่ต้องใช้ window/editor/event ของ Blender ไม่สามารถถือว่าทำงานได้ใน WinForms หรือ background เพียงเพราะมีชื่ออยู่ใน catalog

## ภาษาไทยและที่มาคำแปล

อ่าน GNU gettext catalog ภาษาไทยที่ Blender จัดส่งไว้ที่ `5.2/datafiles/locale/th/LC_MESSAGES/blender.mo` โดยตรง ไม่แก้ไขไฟล์ติดตั้ง คำแปลที่ตรงกับ translation context เดิมมีที่มาจากไฟล์นี้และมี hash กำกับใน metadata

- ฟิลด์ชื่อ/คำอธิบาย/enum ที่พบคำแปลไทยทางการ: 13,900 ฟิลด์
- ฟิลด์ที่ยังไม่มีคำแปลไทยทางการ: 90,079 ฟิลด์
- ชื่อรายการ API หลักที่มีคำแปลทางการ: 3,489 จาก 22,689 รายการ

ข้อมูลดิบเก็บ `thaiName` และ `thaiDescription` เป็น `null` เมื่อไม่พบคำแปล เพื่อไม่อ้างว่ามีคำแปลจาก Blender ทั้งที่ไม่มี ไฟล์ [rea/untranslated-strings.json](rea/untranslated-strings.json) แสดงข้อความที่เหลือสำหรับแปลเพิ่มเติมด้วย glossary ของแอป การแปลที่แอปสร้างเองควรแยก provenance จากคำแปลทางการ

## ตรวจซ้ำ

สร้าง catalog:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python 'D:\MSC AI MODEL\engine\extract_api.py' -- --output 'D:\MSC AI MODEL\docs\rea\blender-api-catalog.json'
```

สร้างข้อมูลที่ผ่าน localizer ของแอปด้วย option `--localized-output <path>` เมื่อมี `engine/thai_localization.py` อยู่ด้วย

ตรวจว่า inventory ตรงกับ runtime ทุก operator/type/defining property/function และตัวอย่าง bindings ที่สำคัญ:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python-exit-code 1 --python 'D:\MSC AI MODEL\tests\api_inventory_smoke.py' -- --catalog 'D:\MSC AI MODEL\docs\rea\blender-api-catalog.json'
```

ผลการดึงข้อมูล: [rea/api-inventory.log](rea/api-inventory.log) ผลทดสอบ: [rea/api-inventory-test.log](rea/api-inventory-test.log)

## ขอบเขตที่ยังไม่ใช่คำว่า Blender ครบทั้งโปรแกรม

- ครบ **operators ที่ลงทะเบียนและ RNA surface ใน factory startup ของเวอร์ชันที่ตรวจ** เพิ่ม public routines ที่ประกาศตรงใน `bpy.types` และ public callable members ภายใน 36 โมดูล/วัตถุที่ระบุ
- ไม่รวม add-ons/extensions ของผู้ใช้ที่ยังไม่ได้เปิด ไม่เท่ากับ inventory ของทุกไฟล์ Python ภายใน Blender ทุกแพ็กเกจ และไม่ครอบคลุมสมาชิก Python ที่สร้างขึ้นแบบ dynamic ทุกชนิด
- ไม่ได้ execute และยืนยันผลของคำสั่ง 2,499 ตัวครบทุกบริบท ไม่ได้เขียน renderer, sculpt system, video editor หรือ animation UI ของ Blender ใหม่
- ไม่ได้ decompile ฟังก์ชัน native ภายใน executable ทั้งหมด การมี API ในรายการไม่เท่ากับการถอดและสร้าง implementation นั้นใหม่
- API ดั้งเดิมยังใช้ identifiers ภาษาอังกฤษเพื่อให้ Blender เรียกได้ถูกต้อง ภาษาไทยของแอปทำหน้าที่เป็นชื่อแสดงผลและตัวเชื่อม ไม่แก้ชื่อภายใน Blender หรือไฟล์ติดตั้ง
