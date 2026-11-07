# คู่มือ มิติ ZBrush ไทย 0.12

## โครงสร้าง

แอป WinForms นี้เป็นแผงควบคุมภาษาไทยที่สร้าง JSON job และ Python script จากคำสั่งที่เลือก เมื่อกดรันจะเปิด `ZBrush.exe -script` พร้อม `.ZPR` ที่เลือกได้ ZBrush Python API (`zbrush.commands`) เป็นผู้แก้โมเดลจริง มุมมอง 3 มิติและหัวแปรงอยู่ใน ZBrush เอง

รุ่นนี้ไม่คอมไพล์หรือแพ็ก `BlenderService`, Blender runner, Blender API catalog, viewport จำลอง หรือไฟล์ engine ของ Blender เก่า ไฟล์ต้นฉบับเก่ายังอยู่ใน repository เพื่อดูประวัติ แต่ไม่มีผลต่อแอปรุ่น 0.12

## รายการเมนูจากผล REA

แอปอ่าน `ZData/ZLang/zcommands/commands.xml` จากโฟลเดอร์ของ `ZBrush.exe` ที่เลือก แล้วรวมคำสั่ง 2,201 รายการกับคำสั่งแนะนำภาษาไทยโดยตัด path ที่ซ้ำกัน หมวดหลักของ ZBrush มีชื่อไทยนำหน้า ชื่อรายการที่ไม่ได้แปลยังแสดงอังกฤษจากไฟล์ที่ติดตั้ง เลือก **แนะนำ** เพื่อดูรายการพร้อมพารามิเตอร์ที่เตรียมไว้ หรือพิมพ์ในช่องค้นหาเพื่อค้นทุกหมวด เมนูที่อ่านจาก XML เลือกการทำงาน **Press / Set / Toggle** ได้ทางด้านขวา `commands.xml` เป็นดัชนี static; `zbc.exists()` ตรวจบริบทอีกครั้งเมื่อรัน

เมนู **ภาษาไทยใน ZBrush** นำไปยัง `Preferences:Language:Create`, `Edit` และ `Export` เพื่อใช้ระบบ Custom Language ที่มากับ ZBrush การสร้างชุดแปลจริงต้องทำใน ZBrush ที่เปิดและได้รับสิทธิ์ใช้งานแล้ว ฟอนต์ที่มากับโปรแกรมยังไม่มี glyph ไทยครบ จึงต้องตั้งฟอนต์รองรับภาษาไทยและทดสอบการแสดงผลจริงตามคู่มือ Maxon

## คำสั่ง

| หมวด | ตัวอย่าง | API |
| --- | --- | --- |
| ไฟล์ | นำเข้า/ส่งออก OBJ | `set_next_filename` + `press("Tool:Import/Export")` |
| โครงสร้าง | Divide, Lower Res | `press` |
| SubTool | Duplicate, เลือก SubTool, ตำแหน่ง X/Y/Z | `press`, `select_subtool`, `set` |
| Canvas | ย้าย/หมุน/สเกลการแสดงเครื่องมือ | `get_transform` + `set_transform` |
| หัวแปรง | ขนาด, Standard, Move, Clay, CurveTube, CurveStandard | `set`, `press` |
| มุมมอง | Edit, Move mode | `set` |
| เมนูอื่น | กด ตั้งค่า สลับเมนูตาม UI path | `exists` + `press/set/toggle` |

บาง UI path มาจากตัวอย่าง Python API ที่ติดตั้งกับ ZBrush 2027; รายการที่ไม่ได้ยืนยันแสดงข้อความ “ตรวจ UI path ตอนรัน” และตัวรันจะหยุดพร้อมข้อความหาก path ไม่มีอยู่ใน ZBrush รุ่นที่ใช้ ค่า Canvas Transform เป็นการ **เพิ่มระยะย้าย/องศาหมุนและคูณสเกล** จากค่าปัจจุบันของเครื่องมือบน canvas ตามนิยาม API ของ Maxon ไม่ใช่การย้าย vertex ใน SubTool

การรันคิวไม่เป็นธุรกรรม: หากคำสั่งลำดับหลังล้มเหลว คำสั่งก่อนหน้าที่ ZBrush ทำแล้วจะยังอยู่ ควรบันทึก `.ZPR` สำรองก่อนงานสำคัญ

## การใช้กับ ZBrush ที่เปิดอยู่

ปุ่มรันในแอปเปิด ZBrush อีก process พร้อมสคริปต์และโหลดไฟล์ `.ZPR` ที่เลือกเป็นอาร์กิวเมนต์ท้ายตามเอกสาร Maxon การสั่งแก้ฉากที่เปิดอยู่ใน ZBrush process เดิมให้ใช้ **บันทึกสคริปต์ Python** แล้วโหลดผ่าน ZBrush → ZScript → Python Scripting → Load สคริปต์ที่ส่งออกเป็นไฟล์เดียวและใช้ `zbrush.commands` โดยตรง

## หลักฐาน REA และข้อจำกัด

- ตรวจ `C:\Program Files\Maxon ZBrush 2027\ZBrush.exe` ด้วย REA 6.4.0: PE32+ x64, SHA-256 `94fcfec1070fb008b5687dba868182dea97d924738115e1956a3e083b63cd456`, resources 113 รายการ; evidence ID `ev_777af12f3d83410c9dbf3121e4d760b82b26dd1d2dbf6538f8b0ea5fea7ccef3` ใน `artifacts/rea-zbrush-resources.json`
- อ่าน `Documentation/python-api/api/zbrush/commands.py` และตัวอย่าง Python ที่ติดกับ ZBrush 2027 เพื่อออกแบบตัวรัน REA เป็นการตรวจไฟล์และ resources ไม่ได้ถอด modeling kernel
- ดาวน์โหลดใบรับรองตัวกลางจาก [DigiCert](https://knowledge.digicert.com/general-information/digicert-trusted-root-authority-certificates) และติดตั้งเฉพาะ `CurrentUser\CA` ตามการอนุญาตของผู้ใช้ ลายเซ็นไฟล์ตรวจผ่านใน Windows ปกติ
- ในการทดสอบล่าสุด ZBrush ยังอยู่ที่ `SplashScreen` และไม่มีไฟล์ผล Python API ภายในเวลาที่กำหนด Maxon service log ระบุ `No licenses to auto activate` จึงต้องตรวจสิทธิ์ใช้งานและเปิด ZBrush ด้วยตนเองก่อนยืนยันเส้นทางรันจริง

## ทดสอบ

`python tests/zbrush_only_runner_smoke.py` ตรวจตัวรันด้วย API จำลอง และ `./build.ps1 -Publish` ตรวจการคอมไพล์และแพ็กเกจจริง การทดสอบ API จำลองไม่เท่ากับการทดสอบเอนจิน ZBrush ที่มีสิทธิ์ใช้งาน
