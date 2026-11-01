# มิติ สตูดิโอ / Miti Studio

แอป **Windows Forms** ภาษาไทย สำหรับสร้างโมเดล 3 มิติด้วยโค้ด Python รูปแบบการทำงานคือ **เขียนโค้ด → กด F5 → ดูโมเดล → ส่งออก** ใช้ Blender ที่ติดตั้งในเครื่องเป็นเอนจินเบื้องหลัง

## รุ่น 0.2 — คลัง Blender ภาษาไทย

เพิ่มคลัง **22,689 รายการ** จาก Blender 5.2.2 LTS: คำสั่ง 2,499, ชนิดข้อมูล 4,012, คุณสมบัติ 10,159, เมธอด 5,234 และฟังก์ชันโมดูล 785 รายการ ชื่อทุกรายการมีภาษาไทย โดยเก็บ ID เดิมและที่มาคำแปลไว้

กด **คลัง Blender ภาษาไทย** ทางซ้าย หรือ **Ctrl+Space** เพื่อค้นหา เลือกพารามิเตอร์ และแทรกโค้ดภาษาไทย อ่าน [ขอบเขตและวิธีใช้คลัง API](docs/API-ภาษาไทย.md) และ [วิธีเรียกฟังก์ชันภาษาไทย](docs/thai-api-runtime.md)

```python
คำสั่ง("ตาข่าย.เพิ่มลูกบาศก์", ขนาด=2, ตำแหน่ง=(0, 0, 1))
ตั้งค่า(เบลนเดอร์.บริบท.วัตถุ, ชื่อ="กล่องภาษาไทย")
```

คำสั่งที่ต้องมีหน้าต่างใช้ **โมเดล → ใช้บริบทหน้าต่าง Blender ขณะรัน** โปรแกรมจะเปิด Blender process แยกเป็นภาษาไทยและปิดหลังสร้างเสร็จ โหมด วัตถุ และพื้นที่ทำงานต้องตรงตามเงื่อนไขของคำสั่งนั้น

## เปิดใช้งาน

1. เปิด `dist/MitiStudio-0.2/MitiStudio.exe` หรือแตก ZIP ทั้งโฟลเดอร์แล้วเปิด `MitiStudio.exe`
2. แอปค้นหา Blender ให้อัตโนมัติ หากไม่พบ เลือก **ตั้งค่า → ตำแหน่ง Blender** แล้วเลือก `blender.exe`
3. เริ่มจากตัวอย่างบ้าน หุ่นยนต์ เมือง หรือ Blender Python ทางซ้าย
4. แก้โค้ดและกด **F5 / สร้างโมเดล**
5. กด **ส่งออกโมเดล** เพื่อบันทึก GLB, BLEND, OBJ หรือ STL

ต้องมี **Windows และ .NET 10 Desktop Runtime x64** พร้อม Blender แยกต่างหาก เครื่องที่ใช้พัฒนาและทดสอบมีครบแล้ว แพ็กเกจนี้ไม่รวม runtime หรือ Blender จึงควรเก็บไฟล์แอป, DLL, `engine` และ `examples` ไว้ด้วยกัน

## ตัวอย่างโค้ด

```python
ล้างฉาก()
สีฟ้า = วัสดุ("สีฟ้า", "#62B6ED")
กล่องแรก = กล่อง("กล่องแรก", size=(2, 2, 2), location=(0, 0, 1), material=สีฟ้า)
ขอบมน(กล่องแรก, width=0.12, segments=4)
ทรงกลม("ลูกบอล", radius=0.7, location=(3, 0, 0.7), material=สีฟ้า)
```

คำสั่งช่วยภาษาไทยนำเข้าให้โดยอัตโนมัติ ชื่อฟังก์ชันมีทั้งไทยและอังกฤษ ส่วนชื่อพารามิเตอร์เป็นอังกฤษ ดูรายละเอียดใน [คู่มือคำสั่ง](docs/คำสั่งภาษาไทย.md)

เรียก Blender Python ได้โดยตรง เช่น `import bpy`, `bpy.ops.mesh.primitive_monkey_add()` หรือสร้าง modifier ผ่าน `obj.modifiers.new(...)` Python นี้รันด้วยสิทธิ์ผู้ใช้เครื่อง ควรใช้สคริปต์ที่ไว้วางใจ

## การใช้งานหน้าต่าง

- **โค้ด:** สี syntax, เลขบรรทัด, ย่อหน้าอัตโนมัติ, Tab 4 ช่อง, เปิด/บันทึก `.py` แบบ UTF-8
- **มุมมอง 3 มิติ:** ลากซ้ายหมุน, ลากขวาหรือกลางเลื่อน, ล้อเมาส์ซูม, F เมื่อโฟกัส viewport เพื่อจัดพอดี
- **รายการวัตถุ:** คลิกชื่อหรือโมเดลเพื่อไฮไลต์และดูจำนวนจุดยอด/สามเหลี่ยม
- **ไฟล์ → บันทึกภาพมุมมอง:** ส่งออก PNG จาก viewport
- **โมเดล → เปิดโมเดลใน Blender:** เปิดผลลัพธ์ล่าสุดใน Blender สำหรับตรวจต่อ
- **Escape / หยุด:** ยกเลิก process ที่กำลังสร้างโมเดล
- **F1:** คู่มือ, **Ctrl+O:** เปิด, **Ctrl+S:** บันทึก, **Ctrl+E:** ส่งออก

ทุกครั้งที่สร้างจะใช้ Blender process ใหม่พร้อมฉากแยก และเก็บผลไว้ใต้ `UserData/Builds` ข้างแอป โค้ดผิดหรือยกเลิกจะไม่แทนที่โมเดลล่าสุดที่สำเร็จ การส่งออกจะเตรียมไฟล์ให้สำเร็จก่อนแทนที่ปลายทาง รวมไฟล์ `.mtl` ของ OBJ

## ขอบเขตการทำงาน

รองรับการสร้างโมเดลแบบคงที่, primitives, materials, transforms และ modifiers ผ่านโค้ด ภาพตัวอย่างเป็น mesh ที่ประเมิน modifier แล้ว พร้อมสีพื้นและแสงจำลองใน viewport แบบ native GDI ของ WinForms

คลัง API เปิดให้ค้นหาและเรียกการทำงานจริงของ Blender รวมถึง render, animation, rig, sculpt, nodes, physics และโมดูลอื่นตามเงื่อนไขของ API นั้น ส่วนภาพตัวอย่างใน WinForms แสดง mesh และสีพื้น ไม่มี animation timeline หรือ shader viewport แบบ Blender โมเดลขนาดใหญ่อาจลดรายละเอียดเฉพาะภาพตัวอย่างประมาณ 60,000 สามเหลี่ยม ส่วน `.blend` และไฟล์ส่งออกยังใช้ geometry จริง

ชื่อที่เพิ่มด้วยอภิธานศัพท์ประกอบคำอัตโนมัติอาจต้องปรับสำนวน คำอธิบายที่ไม่มีคำแปลทางการใช้สรุปภาษาไทยและเก็บต้นฉบับอังกฤษให้เทียบได้ คลังนี้ครอบคลุม API ที่ตรวจพบตามขอบเขตในรายงาน ไม่เท่ากับถอด native functions ภายในทั้งหมดหรือทดสอบทุก workflow

ตรวจและทดสอบกับ **Blender 5.2.2 LTS**, **.NET SDK 10.0.401** และ **WindowsDesktop Runtime 10.0.12** รายงานตรวจไฟล์ด้วย REA และ runtime แยกอยู่ใน [docs/Blender-REA.md](docs/Blender-REA.md) REA ตรวจตัวไฟล์และ resources; ไม่ได้ decompile modeling kernel

## พัฒนาและทดสอบ

ไม่มีแพ็กเกจ NuGet ภายนอก `NuGet.Config` ปิดแหล่งดาวน์โหลด จึง build แบบ offline ได้เมื่อมี SDK และ targeting packs ครบ

ใช้ `./build.ps1 -Publish` เพื่อสร้างแพ็กเกจ 0.2 ในเครื่องนี้ สคริปต์รองรับตัวเรียก SDK ภายในโปรเจกต์ที่เตรียมไว้ระหว่างพัฒนา หากไฟล์ `dotnet.exe` ของระบบยังไม่พร้อมใช้งาน

```powershell
dotnet restore ThaiModelStudio.csproj
dotnet build ThaiModelStudio.csproj -c Release --no-restore
dotnet run --project ThaiModelStudio.csproj -c Release --no-build
dotnet publish ThaiModelStudio.csproj -c Release --no-restore --self-contained false -o dist/MitiStudio
```

ทดสอบ pipeline จริงและความถูกต้องของ geometry/ไฟล์:

```powershell
python tests/backend_smoke.py
dotnet run --project tests/BackendHarness/BackendHarness.csproj -c Release
dotnet run --project tests/ViewportSmoke/ViewportSmoke.csproj
python tests/localization_smoke.py
dotnet run --project tests/ApiUiSmoke/ApiUiSmoke.csproj -c Release
```

สรุปการตรวจที่ผ่าน:

- 4 ตัวอย่างสร้างไฟล์ `.blend` และ GLB จริง
- ส่งออก GLB/BLEND/OBJ/STL พร้อมตรวจส่วนหัวและโครงสร้างที่เกี่ยวข้อง
- ชื่อไฟล์/วัตถุภาษาไทย และสี hex แปลงเป็น linear RGB ของ Blender
- โค้ดผิดคืน traceback และเก็บโมเดลเดิม
- ยกเลิก process ได้และไม่เผยแพร่ผลลัพธ์ที่ยังไม่เสร็จ
- ส่งออก OBJ + MTL ให้ชื่ออ้างอิงตรงกัน และยกเลิกโดยไม่ทำลายไฟล์ปลายทางเดิม
- viewport โหลด mesh, หมุนมุมมอง, โครงเส้น, ปฏิเสธข้อมูลผิด และแสดงโมเดล 60,000 สามเหลี่ยม
- เปิดหน้าต่างจริงและตรวจภาษาไทย/ตัวอย่างผ่าน Computer Use

โปรเจกต์นี้ไม่รวม executable หรือ source code ของ Blender; เรียกใช้ Blender ที่ติดตั้งแยกไว้ และไม่แก้ไขการติดตั้งหรือฉาก Blender ที่เปิดอยู่
