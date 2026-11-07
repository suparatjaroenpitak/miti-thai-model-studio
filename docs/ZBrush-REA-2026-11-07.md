# ผลตรวจ ZBrush ด้วย REA — 7 พฤศจิกายน 2026

## ขอบเขตและหลักฐาน

- เป้าหมาย: `C:\Program Files\Maxon ZBrush 2027\ZBrush.exe`, VersionInfo `2027.0.1.1`, ขนาด 265,392,824 ไบต์, SHA-256 `94fcfec1070fb008b5687dba868182dea97d924738115e1956a3e083b63cd456` (ตรวจ hash ปัจจุบันตรงกับการตรวจเดิม)
- หลักฐาน REA 6.4.0: `artifacts/rea-zbrush-resources.json`, Evidence ID `ev_777af12f3d83410c9dbf3121e4d760b82b26dd1d2dbf6538f8b0ea5fea7ccef3`, operation `inspect_pe_resources`, coverage `complete`: ตรวจ 237 resource-directory entries, พบ 113 resources
- รายการตรวจต่อจากไฟล์ที่ติดตั้งและ hash: `artifacts/zbrush-rea-inventory-2026-11-07.json` เป็นการอ่านไฟล์โดยตรง **ไม่ใช่** REA Evidence เพิ่มเติม

## สิ่งที่สังเกตได้

1. **ไฟล์หลักเป็น PE32+ x64** (`machine=34404`) และมี PE resources 113 รายการ รวม RT_STRING 22 blocks, RT_MENU 1, RT_DIALOG 12, icon/cursor และ manifest การถอด RT_STRING จาก byte ranges ที่ REA ระบุพบข้อความไม่ว่าง 115 รายการ ตัวอย่างเป็นข้อความ Windows ทั่วไป เช่น Open, Save As, Undo และข้อผิดพลาด ไม่ใช่รายการเครื่องมือปั้นทั้งหมด ดังนั้นการแก้ PE string resources ไม่ใช่จุดเริ่มที่เหมาะสำหรับแปล UI หลัก
2. **ระบบภาษาของ ZBrush อยู่ใน `ZData\ZLang`** มี `UInterface.zsc` สำหรับ 8 ภาษา (English, French, German, Spanish, Brazilian Portuguese, Chinese, Japanese, Korean) และ `ZMaster.zsc`; ไม่พบแพ็กภาษาไทยที่ติดตั้ง `startuplang.txt` มีค่า `en` ไฟล์อังกฤษและฝรั่งเศสมีคีย์อังกฤษร่วมกันและส่วนข้อความฝรั่งเศสต่างกัน นี่เป็นหลักฐานเชิงไฟล์ว่า ZBrush ใช้ชุดแปลแยกจาก PE resources
3. `ZData\ZLang\zcommands\commands.xml` มี **2,201 command IDs ที่ไม่ซ้ำ** และ **156 menu IDs** ใน 23 หมวดหลัก พบ `Preferences:Language:Custom`, `Create`, `Import`, `Export`, `Save`, `Edit`, `Auto-edit`, `Show English` และ `Show untranslated` อยู่ในรายการจริง
4. `Documentation\python-api\api\zbrush\commands.py` ที่ติดตั้งมี **145 ฟังก์ชัน** เช่น `exists`, `press`, `set`, `toggle`, `canvas_stroke`, `select_subtool`, `get_transform`, `set_transform`, `set_color` จึงมี API สำหรับสั่งเมนูและสร้างแผงควบคุม ส่วน `get_transform/set_transform` ระบุพิกัด **canvas space** ไม่ใช่การแก้ vertex ของ SubTool
5. `ZFontsWin.xml` มีรายการ `language="zc"` สำหรับภาษาที่ผู้ใช้กำหนด แต่ `NotoSans-Regular.ttf` ที่มากับ ZBrush ไม่มี glyph ในช่วงภาษาไทย U+0E00–U+0E7F ตามการตรวจ cmap การแสดงไทยจริงยังต้องทดสอบ font fallback หรือเพิ่มฟอนต์ที่รองรับไทยตามขั้นตอน Maxon

## ข้อสรุปสำหรับแอปภาษาไทย

- **ยืนยันได้:** ZBrush มีทางสร้างภาษาไทย *ภายในตัวโปรแกรม* ผ่าน Custom Language และมี Python API สำหรับคำสั่ง/แผงควบคุม การทำแอปภาษาไทยควรใช้ `commands.xml` เป็นดัชนีเริ่มต้นของ UI paths แล้วตรวจ `zbc.exists()` ตอนรัน เพราะสถานะคำสั่งขึ้นกับเครื่องมือและบริบท
- **อนุมาน:** การทำภาษาไทยใน ZBrush ตัวจริงน่าจะตอบโจทย์เมนู/หัวแปรงมากกว่าแผง WinForms ภายนอก แต่ต้องใช้ตัวแก้ภาษาของ ZBrush และทดสอบฟอนต์จริง ไม่ควรเขียนทับ `.zsc` แบบไบนารีโดยเดารูปแบบ
- **แก้ในแอป:** เปลี่ยน path เป็น `Tool:Geometry:ZRemesher`, `Tool:Geometry:Target Polygons Count` และ `Draw:  Z Intensity` ตาม `commands.xml`; ตัวรันยังตรวจ path ก่อนใช้
- **ยังไม่ยืนยัน:** การทำงานจริงของสคริปต์กับ ZBrush บนเครื่องนี้ เพราะการทดสอบครั้งก่อนหยุดที่ SplashScreen ก่อน Python API เริ่ม และ log บริการ Maxon มี `No licenses to auto activate` ซึ่งยังไม่พิสูจน์สาเหตุสุดท้าย

## ข้อจำกัดของ REA รอบนี้

REA MCP ไม่เชื่อมในเซสชันนี้ แต่พบตัวเรียก REA 6.4.0 ที่ cache ไว้และตรวจ provider แล้ว: Ghidra 12.1.x/Java ไม่ได้ตั้งค่า, IDA MCP ไม่ได้ตั้งค่า จึงไม่ได้ decompile modeling kernel หรือพิสูจน์ call graph ฟังก์ชัน native การตรวจ PE resources เป็นแบบ static เท่านั้น และการอ่าน `.zsc`, XML, API docs เป็นการตรวจไฟล์ประกอบ ไม่ใช่การสังเกตพฤติกรรมขณะรัน

เอกสาร Maxon: [Custom Language](https://help.maxon.net/zbr/en-us/Content/html/user-guide/multiple-languages/custom-language/custom-language.html), [Language Functions](https://help.maxon.net/zbr/en-us/Content/html/user-guide/multiple-languages/language-functions/language-functions.html), [Python API Overview](https://developers.maxon.net/docs/zbrush/py/2026_1_0/manuals/api_overview.html)
