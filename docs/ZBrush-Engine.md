# เอนจิน ZBrush-style บน Blender / ZBrush-style engine on Blender

## สิ่งที่ทำ / What this is

รุ่น 0.10 มีเครื่องมือ ZBrush-style ที่ทำงานบน **engine จริงของ Blender** (mesh, modifier, shape key, vertex colour, curve) และเพิ่มทางเชื่อมไปยัง **ZBrush 2027 ที่ติดตั้งจริง** ผ่าน OBJ กับ Python API ของ Maxon สำหรับวัตถุที่เลือก

The native handoff exports the selected mesh from Blender and asks ZBrush's documented Python API to import it. Other ZBrush-style tools still execute in Blender.

> **REA / ขอบเขตหลักฐาน:** ตรวจ `C:\Program Files\Maxon ZBrush 2027\ZBrush.exe` ด้วย REA 6.4.0 ได้ PE32+ x64, ขนาด 265,392,824 ไบต์, SHA-256 `94fcfec1070fb008b5687dba868182dea97d924738115e1956a3e083b63cd456`, resources 113 รายการ (evidence ID `ev_777af12f3d83410c9dbf3121e4d760b82b26dd1d2dbf6538f8b0ea5fea7ccef3`; รายละเอียด `artifacts/rea-zbrush-resources.json`). ตรวจ `Documentation/python-api/api/zbrush/commands.py` ที่ติดมากับ ZBrush พบ `press`, `exists`, `set_next_filename` และตัวอย่างส่งออก OBJ อย่างเป็นทางการ การตรวจนี้เป็น static resource inventory และ API documentation ไม่ได้ถอด modeling kernel

## ตารางเทียบ / Mapping

| ZBrush | ความหมาย | สิ่งที่แอปทำจริงบน Blender | ไฟล์ |
| --- | --- | --- | --- |
| Insert Mesh | แทรกชิ้นงาน | สร้าง primitive แล้ว join เข้า SubTool หรือแยกวัตถุใหม่ | `zbrush_engine.insert_mesh` |
| Clip Curve | ตัดด้วยระนาบ | `bmesh.ops.bisect_plane` + `holes_fill` ปิดผิว | `zbrush_engine.clip_plane` |
| Transpose | ย้าย/หมุน/สเกลตามสัดส่วน | เลื่อนจุดด้วย falloff radius (smoothstep) | `zbrush_engine.transpose` |
| Project All | ฉายลงผิวอื่น | Shrinkwrap modifier แล้ว apply | `zbrush_engine.project_all` |
| Surface Noise | ลายผิว | Clouds texture + Displace modifier | `zbrush_engine.surface_noise` |
| Sculpt Layers | เลเยอร์ปั้น | shape key เก็บระยะดัน พร้อม intensity | `zbrush_engine.sculpt_layer` |
| ZSpheres | โครงกระดูกทรงกลม | metaball chain แปลงเป็น mesh | `zbrush_engine.zsphere` |
| FiberMesh | เส้นใย | curve 3 จุดต่อเส้น งอกจากผิว | `zbrush_engine.fibermesh` |
| NanoMesh | กระจายชิ้นงาน | กระจายสำเนาไอโคสเฟียร์แล้ว join | `zbrush_engine.nanomesh` |
| ArrayMesh | เรียงชิ้นงาน | ทำสำเนาเว้นระยะ/หมุน/สเกลแล้ว join | `zbrush_engine.array_mesh` |
| PolyPaint | ระบายสีจุด | `FLOAT_COLOR` attribute ชื่อ `MitiPaint` | `zbrush_engine.polypaint` |
| SubTool Boolean | รวม/ตัดวัตถุ | Boolean modifier (EXACT) แล้วลบ tool | `zbrush_engine.subtool_boolean` |
| Mesh exchange | รับส่งไฟล์ | import OBJ/GLB/STL/FBX ใน Blender | `zbrush_engine.import_mesh` |
| Native ZBrush handoff | เปิดวัตถุใน ZBrush | Blender export เฉพาะวัตถุเป็น OBJ → `ZBrush.exe -script` → `Tool:Import` | `ZBrushService`, `zbrush_open.py` |

รายการที่ยังไม่รองรับจะแสดงสถานะ `ยังไม่รองรับ` ในคลังฟังก์ชัน เช่น Timeline และ turntable animation

## วิธีใช้ / How to use

1. เลือกวัตถุใน SubTools ด้านซ้าย
2. เปิดแท็บ **ZBrush · เอนจิน** ทางขวา (หรือเมนู **Model → ZBrush Engine**)
3. ตั้งค่าแล้วกดปุ่ม เช่น **Transpose move**, **Grow fibers**, **Scatter NanoMesh**
4. แอปจะเพิ่มคำสั่งลงโปรเจกต์และ rebuild ด้วย Blender อัตโนมัติ
5. กด **F5** เพื่อสั่ง rebuild ซ้ำ หรือ **Export** เพื่อบันทึกโมเดล
6. หากต้องการส่งวัตถุที่เลือกเข้า ZBrush จริง กด **Open in ZBrush · เปิดวัตถุใน ZBrush** ในแท็บนี้หรือเมนู Model ถ้า ZBrush ไม่ได้อยู่ในโฟลเดอร์มาตรฐาน ให้เลือก `ZBrush.exe` ที่ Settings → ZBrush Path
7. หลังปั้นใน ZBrush ส่งออก OBJ จาก ZBrush แล้วกด **Import ZBrush OBJ · นำโมเดลกลับ** เพื่อเพิ่มเป็นวัตถุใหม่ใน Blender

เครื่องมือ ZBrush-style ใช้กับวัตถุที่เลือก (ยกเว้น ZSpheres/Import ที่สร้างวัตถุใหม่) และเก็บเป็นขั้นตอนในโปรเจกต์ `.miti` ปุ่ม Open in ZBrush ส่งไฟล์ OBJ แยกไปยังโปรแกรม ZBrush ไม่ได้เพิ่มคำสั่งในโปรเจกต์

## ขอบเขตที่ต่างจาก ZBrush / Limits

- เครื่องมือ 13 คำสั่งยังเป็นการเลียนแบบ (emulation) บน Blender: ไม่มี ZSphere rigging เต็มรูปแบบ, ไม่มี DynaTopology แบบเรียลไทม์, ไม่มี subdivision levels ซ้อนหลายชั้นแบบ ZBrush
- เลเยอร์ปั้นใช้ shape key: เมื่อใช้คำสั่งที่ต้อง apply modifier (Project All, Surface Noise, Boolean) เลเยอร์จะถูก freeze ลง mesh ก่อน เป็นข้อจำกัดของ Blender ที่ modifier ใช้กับ mesh ที่มี shape keys ไม่ได้
- FiberMesh เป็น curve จริงแต่ไม่ใช่ระบบ grooming/combing; NanoMesh/ArrayMesh เป็น geometry ที่ join แล้ว ไม่ใช่ instance แบบประหยัดหน่วยความจำ
- PolyPaint เก็บเป็น vertex colour และแสดงใน GLB/โปรแกรมอื่น; viewport ของแอปยังใช้สีวัสดุเป็นหลัก
- ทางเชื่อม native ใช้ OBJ ไม่ใช่ GoZ; การนำผลกลับ Blender ต้อง export OBJ จาก ZBrush และเลือกไฟล์ด้วยตนเอง
- เริ่มแรก ZBrush แจ้ง `CRITICAL: The following libraries seem to be broken` เพราะ Windows ขาดใบรับรองตัวกลางของ DigiCert สองใบ ผู้ใช้อนุญาตให้ติดตั้งทั้งสองใบจาก [DigiCert](https://knowledge.digicert.com/general-information/digicert-trusted-root-authority-certificates) เฉพาะ `CurrentUser\CA`: Code Signing SHA-384 2021 CA1 (SHA-256 `46011EDE1C147EB2BC731A539B7C047B7EE93E48B9D3C3BA710CE132BBDFAC6B`) และ TimeStamping SHA-256 2025 CA1 (SHA-256 `CA0B1554ECD901EA19DCAD8749E9F2648C8D6DFCEA1ADD9D2C2109415BB82CCD`) การตรวจ `Get-AuthenticodeSignature` ของ `ZBrush.exe` และ corelibs ตัวอย่างผ่านในบริบทผู้ใช้ปกติ
- หลังแก้ใบรับรอง ZBrush เปิดได้ถึง `SplashScreen` แต่ไม่รันสคริปต์ภายในเวลาทดสอบและไม่มีไฟล์ผลลัพธ์ Python API; log ของ Maxon ระบุ `No licenses to auto activate` จึงควรตรวจสิทธิ์ใช้งานใน Maxon App และเปิด ZBrush ด้วยตนเองให้ผ่านหน้าเริ่มต้นก่อนทดสอบทางเชื่อมอีกครั้ง ยังไม่มีการยืนยันว่า native import ทำงานจริง

## การตรวจสอบ / Verification

รันจริงกับ Blender 5.2.2 LTS:

```powershell
python tests/zbrush_engine_smoke.py
dotnet run --project tests/ZBrushEngineSmoke/ZBrushEngineSmoke.csproj -c Release
python tests/zbrush_native_bridge_smoke.py
```

- `tests/zbrush_engine_smoke.py` ตรวจครบ 13 คำสั่ง: insert, clip, transpose, project, noise, layer (พร้อมมาสก์), zsphere, fibermesh, nanomesh, array, polypaint, boolean, import
- `tests/ZBrushEngineSmoke` สร้าง snippet จากแผง GUI จริง แล้วรันผ่าน runner จนได้ preview: 4 objects, ตัวกล่องมีจุดยอดเพิ่มขึ้น, ZSphere และ MitiFiberMesh ปรากฏ, วัตถุที่ใช้ตัดถูก consume
- ทดสอบส่งออก OBJ เฉพาะวัตถุที่เลือกจาก Blender จริง และทดสอบลำดับคำสั่ง native bridge ด้วย API จำลอง
- ผลลัพธ์คือ `ZBRUSH_ENGINE_SMOKE_PASS` และ `ZBRUSH_PANEL_SMOKE_PASS`
