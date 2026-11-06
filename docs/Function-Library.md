# คลังฟังก์ชันรวม Blender + ZBrush-style / Combined Function Library

## สรุป / Summary

แอปรวมรายการฟังก์ชันไว้ในหน้าต่างเดียว เปิดด้วยปุ่ม **Functions · ฟังก์ชัน** บนแถบด้านบน หรือเมนู **Model → Function Library** (Ctrl+Shift+F) ค้นหาได้ทั้งชื่ออังกฤษ ชื่อไทย รหัส และคำอธิบาย

The app merges every function it knows into one searchable window. Open it with the **Functions · ฟังก์ชัน** button in the header or **Model → Function Library** (Ctrl+Shift+F).

| แหล่ง / Source | จำนวน / Entries | ที่มา / Origin |
| --- | --- | --- |
| Blender API | 22,689 | REA ของ `blender.exe` + runtime introspection ใน Blender 5.2.2 LTS |
| ZBrush-style | 65 | แมปงานแบบ ZBrush เข้ากับเครื่องมือที่แอปทำจริง |
| รวม / Total | 22,754 | แสดงในตารางเดียว |

## วิธีใช้ / How to use

1. พิมพ์คำค้น เช่น `DynaMesh`, `มาสก์`, `subdivide` หรือ `เกลี่ยผิว`
2. กรองแหล่งด้วยช่อง **All sources · ทุกแหล่ง**
3. เลือกแถวแล้วอ่านรายละเอียดทางขวา: ชื่อไทย–อังกฤษ, สถานะ, รหัส, และคำสั่ง Blender ที่ใช้จริง
4. กด **Open tool · เปิดเครื่องมือ** เพื่อเปิดแผง GUI ที่ตรงกับเครื่องมือนั้น หรือ **Use with object · ใช้กับวัตถุ** เพื่อส่งคำสั่ง Blender ไปยังแผง Functions ของวัตถุที่เลือก
5. กด Enter หรือดับเบิลคลิกเพื่อใช้งานรายการที่เลือก

The ZBrush column shows the honest status of each tool: `ใช้งานผ่าน GUI` (one-click GUI), `แมปกับ Blender` (run through a Blender operator or the Functions tab) or `ยังไม่รองรับ` (not implemented).

## REA ครอบคลุมอะไร / What REA covers

- **Blender:** อ่านไฟล์ `blender.exe` ด้วย REA `inspect-pe-resources` เก็บ hash และ resource evidence แล้วดึง API catalog จาก process `--background --factory-startup` ดู [Blender-REA.md](Blender-REA.md)
- **ZBrush:** เครื่องนี้ไม่ได้ติดตั้ง ZBrush จึงไม่มีไฟล์ให้ตรวจ REA รายการ ZBrush-style ทั้ง 65 รายการมาจากการแมปงานออกแบบ (SubTool, Brush, DynaMesh, ZRemesher, Mask, PolyPaint) เข้ากับ Blender operator และแผง GUI ของแอป ไม่ใช่การถอดโค้ดจาก ZBrush

REA ช่วยยืนยันตัวไฟล์และรายการ API ที่มีจริง แต่ไม่ได้ decompile modeling kernel ของ Blender และไม่ได้ยืนยันว่าทุกฟังก์ชันทำงานได้ในทุกบริบท รายการที่เป็น Blender API จึงแสดงรหัสจริงเพื่อให้ตรวจต่อได้ ส่วนคำสั่งที่ต้องมีบริบทเฉพาะจะแจ้งเตือนเมื่อใช้งาน

## GUI สองภาษา / Bilingual GUI

ทุกเมนู ปุ่ม แท็บ และป้ายสถานะแสดงอังกฤษนำหน้าภาษาไทย เช่น `Functions · ฟังก์ชัน`, `Sculpt · ปั้น`, `Topology · โครงสร้าง` ชื่อพารามิเตอร์ของ Blender แสดงภาษาอังกฤษคู่กับคำไทย

GUI เป็นโหมดหลักและเปิดมาให้ใช้ทันที โหมดสคริปต์ถูกซ่อนไว้เป็นตัวเลือกเสริม เปิดได้ที่ **View → Script Mode** เมื่อต้องแก้โค้ดเอง
