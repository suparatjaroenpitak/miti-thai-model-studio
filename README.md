# มิติ ZBrush ไทย

แอป Windows Forms ภาษาไทยสำหรับจัดคำสั่งและพารามิเตอร์ของ **ZBrush ที่ติดตั้งในเครื่อง** รุ่น 0.11 ใช้ Maxon ZBrush Python API โดยตรง ไม่มี Blender ในแพ็กเกจหรือเส้นทางรัน

## ใช้งาน

1. เปิด `MitiStudio.exe` แล้วเลือก `ZBrush.exe` หากแอปหาไม่เจอ
2. ค้นหาคำสั่งภาษาไทยหรืออังกฤษทางซ้าย เช่น **แบ่งผิว / Divide**
3. ปรับ UI path, ค่า, ไฟล์ OBJ, SubTool index หรือ transform ด้านขวา
4. กด **รันคำสั่งนี้** หรือเพิ่มหลายคำสั่งลงคิวแล้วกด **รันคิวใน ZBrush**
5. แอปเปิด ZBrush process ใหม่ด้วย `-script`; เลือกไฟล์ `.ZPR` ที่เมนูไฟล์ก่อนรันหากต้องการทำงานต่อจากโปรเจกต์เดิม

การปั้นด้วยเมาส์และ viewport ใช้หน้าต่าง **ZBrush จริง** หากมี ZBrush เปิดอยู่แล้ว ให้กด **บันทึกสคริปต์ Python** แล้วโหลดไฟล์ `.py` ใน ZBrush ผ่าน **ZScript → Python Scripting → Load** เพื่อให้คำสั่งทำงานกับฉากที่เปิดอยู่

เมนู **เมนูอื่น** รับ ZBrush UI path สำหรับ `press`, `set` และ `toggle` จึงเข้าถึงเมนูที่ไม่ได้ใส่ไว้ในรายการเริ่มต้นได้เมื่อทราบ path ที่ถูกต้อง ตัวรันตรวจว่า path มีอยู่จริงก่อนใช้

## สถานะการทดสอบ

- ตรวจ `ZBrush.exe` 2027 และ resources ด้วย REA; ตรวจ Python API จากเอกสารที่ติดมากับโปรแกรม
- ติดตั้งใบรับรองตัวกลาง DigiCert สองใบใน `CurrentUser\CA` ตามที่ผู้ใช้อนุญาต ลายเซ็น `ZBrush.exe` และ corelibs ตรวจผ่านในบริบท Windows ปกติ
- ZBrush บนเครื่องทดสอบยังค้างที่ `SplashScreen` ก่อน Python API; log ของ Maxon ระบุ `No licenses to auto activate` จึงยัง **ไม่ยืนยัน** ว่าคำสั่งทำงานใน ZBrush จริง ผู้ใช้ต้องเปิด ZBrush ให้ผ่านหน้าเริ่มต้นและตรวจสิทธิ์ใช้งานก่อน
- ตัวรัน Python และเส้นทางสร้างสคริปต์ตรวจด้วย API จำลอง; การ build .NET ผ่าน

รายละเอียดและขอบเขตอยู่ที่ [คู่มือ ZBrush ภาษาไทย](docs/ZBrush-Thai.md)

## Build

```powershell
./build.ps1 -Publish
```

ผลลัพธ์อยู่ใน `dist/MitiStudio-0.11.0/` ต้องมี Windows และ .NET 10 Desktop Runtime พร้อม ZBrush แยกต่างหาก
