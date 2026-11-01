# ผลตรวจ Blender และขอบเขตแอปภาษาไทย

ปรับขอบเขตเพิ่มเติมตามคำขอให้ดึงฟังก์ชันทั้งหมด: ดู [Blender-API-Coverage.md](Blender-API-Coverage.md) สำหรับ catalog 22,689 รายการที่ตรวจจาก API ของ Blender ที่ติดตั้ง รวม operators 2,499 ตัว ข้อมูลรอบนี้เป็น runtime introspection ไม่ใช่ native decompilation

ตรวจเมื่อ 1 พฤศจิกายน 2026 โดยใช้ Blender ที่ติดตั้งในเครื่องนี้เท่านั้น

## ตัวแอปที่ตรวจ

- ไฟล์: `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`
- ขนาด: 113,022,936 ไบต์
- SHA-256: `f141a3c5754500e6f84361099a4cd53f0bb7662b6a0d2cfaaa8fbb63e92234a0`
- เวอร์ชันที่ Blender รายงานขณะทำงาน: **5.2.2 LTS**
- Build: `d13f752e3b9c`, branch `blender-v5.2-release`
- Python ภายใน Blender: 3.13.13

## หลักฐานจาก REA จริง

ใช้ REA CLI 6.4.0 คำสั่ง `inspect-pe-resources` แบบอ่านไฟล์ ไม่เปิดหรือแก้ไข Blender:

```powershell
npx.cmd -y rea-agents@6.4.0 inspect-pe-resources 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --max-file-bytes 536870912 --format json --full-output
```

ผลเต็มเก็บที่ [rea/pe-resources.json](rea/pe-resources.json)

Evidence ID: `ev_c5d46a0af7d681dd1ffda7c78bf653edde5baa097869db03e3f7845e63c6bc95`

สิ่งที่ยืนยันจากหลักฐานนี้: ไฟล์เป็น PE32+, machine `34404` (`0x8664`), ตรวจ resource directory 44 entries ครบตามขอบเขต parser พบ 20 resources และ 2 icon groups พร้อม hash ของ payload แต่ละตัว

หลักฐานนี้ยืนยันตัวไฟล์และโครงสร้าง resource เท่านั้น ไม่ได้ถอดโค้ด modeling, จำลองการโหลดไอคอนของ Windows หรือยืนยันพฤติกรรมทั้งหมดของ Blender

ตรวจ readiness ด้วย `rea doctor --client codex --json` พบว่า Ghidra/Hopper/IDA ไม่ได้ตั้งค่า และ REA MCP ยังไม่ได้ลงทะเบียนใน Codex จึงใช้ CLI parser ที่ทำงานได้โดยตรง ไม่มีการเปลี่ยน config หรือติดตั้ง decompiler เพิ่ม

## สิ่งที่สังเกตจากไฟล์ Python ที่ Blender จัดส่ง

ส่วนนี้เป็นการอ่านไฟล์ด้วยเครื่องมือไฟล์ตามปกติ **ไม่ใช่ REA Evidence**:

| ไฟล์ภายในโฟลเดอร์ `5.2\scripts\startup\bl_ui` | ตำแหน่ง | สิ่งที่พบ |
| --- | --- | --- |
| `space_view3d.py` | บรรทัด 2428–2435 | เมนู primitive เรียก operators ของ plane, cube, UV sphere, cylinder, cone และ torus |
| `space_text.py` | บรรทัด 43, 280 | คำสั่งรันโค้ดใช้ `text.run_script` |
| `space_text.py` | บรรทัด 283–315 | เมนูตัวอย่างโค้ด Python และ template |
| `properties_data_modifier.py` | บรรทัด 151–184 | เมนู ARRAY, BEVEL, BOOLEAN, DECIMATE, MIRROR, SOLIDIFY, SUBSURF และอื่น ๆ |

Hash ของไฟล์ที่อ่านเก็บที่ [rea/packaged-scripts-hashes.json](rea/packaged-scripts-hashes.json) แอปใหม่ใช้ชื่อ operators/API เหล่านี้เป็นจุดเชื่อมกับ Blender ไม่คัดลอกเมนูหรือโค้ดส่วนติดต่อจาก Blender

## การทดสอบ runtime แบบแยกฉาก

ใช้ [rea/probe.py](rea/probe.py) กับ `--background --factory-startup` จึงเป็น process ใหม่ ไม่ได้เชื่อมต่อหรือเปลี่ยนฉากในหน้าต่าง Blender ที่ผู้ใช้อาจเปิดไว้:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' --background --factory-startup --python 'D:\MSC AI MODEL\docs\rea\probe.py'
```

ผลเต็ม: [rea/runtime-probe.json](rea/runtime-probe.json) และ [rea/runtime-probe.log](rea/runtime-probe.log)

ผลที่ทำจริง:

- สร้าง cube ได้ 8 vertices / 12 triangles
- เพิ่ม Bevel modifier และประเมิน mesh ได้ 96 vertices / 188 triangles
- สร้าง material แบบ nodes และกำหนด Base Color ผ่าน node ชนิด `BSDF_PRINCIPLED`
- บันทึก `.blend` และส่งออก `.glb`, `.obj`, `.stl` ได้ โดยไฟล์ผลลัพธ์อยู่ใน `rea/runtime-artifacts`
- ตั้ง `file_preview_type = 'NONE'` ใน process ทดสอบ เพื่อไม่สร้าง thumbnail ในโฟลเดอร์โปรไฟล์

ผลที่ตรวจได้เพียงการมีอยู่ของ API ยังไม่ได้ทดสอบผลลัพธ์ทุกชนิด:

- primitives ทั้ง 7 แบบใน probe รวม monkey
- duplicate, join, smooth shading และ apply modifier
- enum ของ modifiers รวม BOOLEAN, ARRAY, MIRROR, SUBSURF, SOLIDIFY, DECIMATE, NODES และ ARMATURE

ไฟล์ JSON ส่วนนี้มีป้าย `ordinary-shell-controlled-runtime-observation-not-REA-Evidence` เพื่อแยกจากหลักฐาน parser ของ REA ชัดเจน

## การนำมาออกแบบ Thai Model Studio

ตารางนี้เป็น **การตัดสินใจออกแบบแอปใหม่** ไม่ใช่การอ้างว่า Blender มีเมนูภาษาไทยตามนี้:

| ความสามารถของ Blender ที่ตรวจ | รูปแบบในแอปใหม่ |
| --- | --- |
| Python สร้างวัตถุและ modifiers | พิมพ์ Python แล้วกดสร้างโมเดล |
| mesh primitives | คำสั่งช่วยและตัวอย่างภาษาไทย เช่น กล่อง ทรงกลม ทรงกระบอก |
| shader material | กำหนดสีผ่านโค้ดและดูสีพื้นฐานในภาพตัวอย่าง |
| scene/object data | รายชื่อวัตถุข้าง viewport |
| save/export operators | บันทึกผลลัพธ์และส่งออกไฟล์โมเดล |
| Text Editor templates | ตัวอย่างสคริปต์สำหรับเริ่มต้น |

แอปใช้ Windows Forms เป็นหน้าต่างหลัก และเรียก Blender ที่ติดตั้งไว้เป็น process เบื้องหลัง ผู้ใช้แก้รูปทรงผ่านโค้ด ส่วน viewport ใช้ดู หมุน ซูม และตรวจวัตถุ รูปทรงต้นฉบับและไฟล์ส่งออกสร้างด้วย Blender จริง

## ขอบเขตและสิ่งที่ยังไม่ได้ยืนยัน

- นี่คือรุ่นเริ่มต้นของแอปสร้างโมเดลด้วยโค้ด ไม่ใช่การสร้าง Blender ทั้งหมดใหม่
- ไม่ได้ decompile native modeling/renderer ของ Blender และไม่ได้ตรวจ every function หรือทุก UI workflow
- runtime probe ทดสอบ cube + Bevel + material + 4 formats เท่านั้น การพบ operator/modifier ใน enum ไม่เท่ากับทดสอบการใช้งานทุกกรณี
- viewport ของแอปเป็นภาพตัวอย่าง mesh; ไม่ได้จำลอง shader nodes, textures, ray tracing, animation timeline, sculpting, compositing, video editing หรือ render ของ Blender ทั้งชุด
- สคริปต์ผู้ใช้เรียก `bpy` ได้ตาม Blender ที่ติดตั้ง ความเข้ากันได้กับ Blender รุ่นอื่นยังต้องทดสอบเพิ่มเติม
- `copyright.txt` ที่จัดส่งกับ Blender ระบุ GNU GPL และมีเอกสาร third-party licenses ในโฟลเดอร์ `license`; โปรเจกต์นี้ไม่ได้แนบ Blender executable หรือคัดลอกโค้ด Blender มาไว้ในแอป

## สถานะคำถามเดิม

| คำถาม | สถานะ |
| --- | --- |
| Blender ในเครื่องคือไฟล์/เวอร์ชันใด | ตอบแล้วด้วย hash + REA + runtime |
| สร้างโมเดลด้วยโค้ดผ่าน Blender ได้หรือไม่ | ทดสอบแล้วใน process แยก |
| ใช้เมนูไทยและลดความซับซ้อนได้อย่างไร | กำหนดแนวทางเป็นแอปใหม่ด้านบน |
| ส่งออกไฟล์โมเดลจริงได้หรือไม่ | ทดสอบ `.blend`, `.glb`, `.obj`, `.stl` แล้ว |
| API ภาษาไทยทั้งชุดที่ตรวจพบใน build นี้ | เพิ่มในรุ่น 0.2 ตามผลด้านล่าง |
| ถอด native functions ภายใน Blender ทั้งหมด | ยังไม่ได้ทำ; ไม่มีหลักฐาน decompilation |

## การขยายตามคำขอครั้งถัดมา — รุ่น 0.2

ใช้ `engine/extract_api.py` introspect ใน process `--background --factory-startup` และตรวจเทียบชุด ID ด้วย `tests/api_inventory_smoke.py` ผลปัจจุบันอยู่ `rea/blender-api-catalog.json`:

- 2,499 operators ครบชุดที่ `dir(bpy.ops)` รายงานใน factory-startup
- 4,012 RNA types, 10,159 คุณสมบัติที่ประกาศ ณ ชนิดต้นทาง
- 5,234 เมธอด รวม RNA functions และ public routine members ของชนิดข้อมูล
- 785 ฟังก์ชัน/constructor ของโมดูลและ object ที่ระบุ 36 จุด
- รวม **22,689 ID ที่ไม่ซ้ำ**; ไม่พบ extraction errors ตามขอบเขตที่สแกน

ตรวจ `poll()` โดยไม่รัน operators ทั้งชุด ผ่านในบริบทเริ่มต้น 672 รายการ ต้องเตรียมบริบท 1,827 รายการ การมี API หรือ poll ผ่านไม่เท่ากับทดสอบพฤติกรรมของคำสั่งทุกกรณี

ผลแปลอยู่ `engine/blender_api.json` มีชื่อไทยครบ 22,689 รายการ ผสมคำแปล gettext ที่จัดส่งกับ Blender, วลีที่เพิ่ม และคำประกอบจากอภิธานศัพท์ พร้อม provenance และ hashes ชื่อ API เดิมยังอยู่เพื่อเรียก native engine จริง ชื่อที่ประกอบอัตโนมัติยังอาจต้องปรับสำนวน คำอธิบายที่ไม่มีคำแปลทางการเป็นสรุปไทยพร้อมต้นฉบับอังกฤษ

ทดสอบเพิ่ม: alias คำสั่ง/พารามิเตอร์/enum, property access, RNA method, `bmesh`, `mathutils`, การสร้างโค้ดจาก WinForms 5 ประเภท และใช้ VIEW_3D operator ใน Blender process ที่มีหน้าต่างจริง กระบวนการนี้ยังเป็น **runtime API inspection ไม่ใช่ REA native decompilation** หลักฐาน REA parser เดิมด้านบนยังแยกไว้เหมือนเดิม

รายละเอียดความครบถ้วนและบริบทที่ต้องเตรียม: [API-ภาษาไทย.md](API-ภาษาไทย.md)
