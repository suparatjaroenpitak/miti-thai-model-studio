namespace ThaiModelStudio;

public sealed record ZBrushCommandDefinition(
    string Id, string Category, string Thai, string English, string Mode, string Path,
    double DefaultValue, double Minimum, double Maximum, string Help, bool Documented = true)
{
    public override string ToString() => Thai + "  ·  " + English;
}

public sealed record ZBrushAction(
    string Mode, string Path = "", double Value = 0, string File = "", int Index = 0,
    double[]? Move = null, double[]? Rotate = null, double[]? Scale = null, double[]? Color = null)
{
    public string Summary => Mode switch
    {
        "press" => "กด · " + Path,
        "set" => "ตั้งค่า · " + Path + " = " + Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
        "toggle" => "สลับ · " + Path,
        "import" => "นำเข้า · " + System.IO.Path.GetFileName(File),
        "export" => "ส่งออก · " + System.IO.Path.GetFileName(File),
        "select_subtool" => "เลือก SubTool · " + Index,
        "transform" => "ย้าย/หมุน/สเกลมุมมองเครื่องมือ",
        "color" => "ตั้งสี · RGB",
        _ => Mode
    };
}

public static class ZBrushCommandCatalog
{
    // Paths marked Documented appear in the Python API examples or commands.xml shipped with ZBrush 2027.
    // XML inventory does not prove runtime availability; every path is checked with zbc.exists before use.
    public static IReadOnlyList<ZBrushCommandDefinition> All { get; } = new[]
    {
        new ZBrushCommandDefinition("cube3d", "สร้าง", "สร้างกล่อง 3 มิติ", "Cube3D", "press", "Tool:Cube3D", 0, 0, 0, "เลือกเครื่องมือ Cube3D ของ ZBrush"),
        new ZBrushCommandDefinition("make_polymesh", "สร้าง", "แปลงเป็น PolyMesh3D", "Make PolyMesh3D", "press", "Tool:Make PolyMesh3D", 0, 0, 0, "แปลง primitive ให้แก้รูปทรงได้ใน ZBrush"),
        new ZBrushCommandDefinition("import", "ไฟล์", "นำเข้า OBJ", "Import OBJ", "import", "Tool:Import", 0, 0, 0, "เลือกไฟล์ OBJ เพื่อเพิ่มเครื่องมือใน ZBrush"),
        new ZBrushCommandDefinition("export", "ไฟล์", "ส่งออก OBJ", "Export OBJ", "export", "Tool:Export", 0, 0, 0, "ส่งออก SubTool ที่ใช้งานเป็นไฟล์ OBJ"),
        new ZBrushCommandDefinition("divide", "โครงสร้าง", "แบ่งผิว", "Divide", "press", "Tool:Geometry:Divide", 0, 0, 0, "เพิ่มระดับ subdivision ของ SubTool"),
        new ZBrushCommandDefinition("lower", "โครงสร้าง", "ลดระดับรายละเอียด", "Lower Res", "press", "Tool:Geometry:Lower Res", 0, 0, 0, "กลับไประดับ subdivision ที่ต่ำกว่า"),
        new ZBrushCommandDefinition("higher", "โครงสร้าง", "เพิ่มระดับรายละเอียด", "Higher Res", "press", "Tool:Geometry:Higher Res", 0, 0, 0, "ไปยังระดับ subdivision ที่สูงกว่า"),
        new ZBrushCommandDefinition("dynamesh", "โครงสร้าง", "สร้างตาข่าย DynaMesh", "DynaMesh", "press", "Tool:Geometry:DynaMesh", 0, 0, 0, "ให้ ZBrush สร้างตาข่ายใหม่"),
        new ZBrushCommandDefinition("zremesher", "โครงสร้าง", "จัดตาข่าย ZRemesher", "ZRemesher", "press", "Tool:Geometry:ZRemesher", 0, 0, 0, "จัดตาข่ายใหม่ด้วย ZRemesher"),
        new ZBrushCommandDefinition("zremesh_target", "โครงสร้าง", "จำนวนโพลิกอนเป้าหมาย", "Target Polygons Count", "set", "Tool:Geometry:Target Polygons Count", 5, 0.1, 100, "ตั้งค่าเป้าหมาย ZRemesher"),
        new ZBrushCommandDefinition("duplicate", "SubTool", "ทำสำเนาวัตถุ", "Duplicate SubTool", "press", "Tool:SubTool:Duplicate", 0, 0, 0, "ทำสำเนา SubTool ที่เลือก"),
        new ZBrushCommandDefinition("select_subtool", "SubTool", "เลือกวัตถุตามลำดับ", "Select SubTool", "select_subtool", "", 0, 0, 10000, "เลือก SubTool ด้วยลำดับเริ่มจาก 0"),
        new ZBrushCommandDefinition("edit", "มุมมอง", "เปิดโหมดแก้ไข", "Edit Mode", "set", "Transform:Edit", 1, 0, 1, "เปิดโหมดแก้ไขเครื่องมือใน ZBrush"),
        new ZBrushCommandDefinition("move_mode", "มุมมอง", "เปิดโหมดย้าย", "Move Mode", "set", "Transform:Move", 1, 0, 1, "เปิดโหมด Move"),
        new ZBrushCommandDefinition("rotate_mode", "มุมมอง", "เปิดโหมดหมุน", "Rotate Mode", "press", "Transform:Rotate", 0, 0, 0, "เปิดโหมด Rotate"),
        new ZBrushCommandDefinition("fit_view", "มุมมอง", "จัดโมเดลให้พอดีจอ", "Fit Mesh To View", "press", "Transform:Fit Mesh To View", 0, 0, 0, "ปรับมุมมองให้เห็นโมเดลเต็ม", true),
        new ZBrushCommandDefinition("transform", "มุมมอง", "ย้าย หมุน และสเกลบน canvas", "Canvas Transform", "transform", "", 0, 0, 0, "ปรับการแสดงเครื่องมือบน canvas จากค่าเดิม; ไม่เปลี่ยนตำแหน่ง vertex ใน SubTool"),
        new ZBrushCommandDefinition("draw_size", "หัวแปรง", "ขนาดหัวแปรง", "Draw Size", "set", "Draw:Draw Size", 42, 1, 1000, "ตั้งขนาดหัวแปรง"),
        new ZBrushCommandDefinition("z_intensity", "หัวแปรง", "ความแรงหัวแปรง", "Z Intensity", "set", "Draw:  Z Intensity", 25, 0, 100, "ตั้งค่าความแรง Z Intensity"),
        new ZBrushCommandDefinition("focal_shift", "หัวแปรง", "ความนุ่มขอบแปรง", "Focal Shift", "set", "Draw:Focal Shift", 0, -100, 100, "ตั้งค่า Focal Shift"),
        new ZBrushCommandDefinition("standard", "หัวแปรง", "แปรงมาตรฐาน", "Standard Brush", "press", "Brush:Standard", 0, 0, 0, "เลือกหัวแปรง Standard", false),
        new ZBrushCommandDefinition("move_brush", "หัวแปรง", "แปรงดึงผิว", "Move Brush", "press", "Brush:Move", 0, 0, 0, "เลือกหัวแปรง Move", false),
        new ZBrushCommandDefinition("clay", "หัวแปรง", "แปรงเติมดิน", "Clay Brush", "press", "Brush:Clay", 0, 0, 0, "เลือกหัวแปรง Clay", false),
        new ZBrushCommandDefinition("smooth", "หัวแปรง", "แปรงเกลี่ยผิว", "Smooth Brush", "press", "Brush:Smooth", 0, 0, 0, "เลือกหัวแปรง Smooth", false),
        new ZBrushCommandDefinition("curve_standard", "หัวแปรง", "แปรงเส้นมาตรฐาน", "Curve Standard", "press", "Brush:CurveStandard", 0, 0, 0, "เลือกหัวแปรง CurveStandard"),
        new ZBrushCommandDefinition("curve_tube", "หัวแปรง", "แปรงท่อเส้นโค้ง", "Curve Tube", "press", "Brush:CurveTube", 0, 0, 0, "เลือกหัวแปรง CurveTube"),
        new ZBrushCommandDefinition("clone", "หัวแปรง", "แปรงโคลน", "Clone Brush", "press", "Brush:Clone", 0, 0, 0, "เลือกหัวแปรง Clone"),
        new ZBrushCommandDefinition("transpose", "หัวแปรง", "แปรง Transpose", "Transpose Brush", "press", "Brush:Transpose", 0, 0, 0, "เลือกหัวแปรง Transpose"),
        new ZBrushCommandDefinition("x_position", "SubTool", "ตำแหน่ง X", "X Position", "set", "Tool:Geometry:X Position", 0, -100000, 100000, "ตั้งตำแหน่ง X ของ SubTool"),
        new ZBrushCommandDefinition("y_position", "SubTool", "ตำแหน่ง Y", "Y Position", "set", "Tool:Geometry:Y Position", 0, -100000, 100000, "ตั้งตำแหน่ง Y ของ SubTool"),
        new ZBrushCommandDefinition("z_position", "SubTool", "ตำแหน่ง Z", "Z Position", "set", "Tool:Geometry:Z Position", 0, -100000, 100000, "ตั้งตำแหน่ง Z ของ SubTool"),
        new ZBrushCommandDefinition("unwrap", "UV และสี", "คลี่ UV", "Unwrap UV", "press", "Tool:UV Map:Create (Unwrap):Unwrap", 0, 0, 0, "คลี่ UV ด้วยเครื่องมือ ZBrush"),
        new ZBrushCommandDefinition("displacement", "UV และสี", "สร้าง Displacement Map", "Create DispMap", "press", "Tool:Displacement Map:Create DispMap", 0, 0, 0, "สร้างแผนที่ Displacement จากรายละเอียดผิว"),
        new ZBrushCommandDefinition("set_color", "UV และสี", "เลือกสี RGB", "Set Color", "color", "", 0, 0, 0, "ตั้งสีปัจจุบันใน ZBrush ด้วยค่า R/G/B 0–255"),
        new ZBrushCommandDefinition("fill_color", "UV และสี", "ระบายสีทั้งวัตถุ", "Fill Object", "press", "Color:FillObject", 0, 0, 0, "เติมสีปัจจุบันลงทั้งวัตถุ"),
        new ZBrushCommandDefinition("mrgb", "UV และสี", "เปิดการลงสีวัสดุ", "MRGB", "press", "Draw:Mrgb", 0, 0, 0, "เปิดโหมด MRGB ใน Draw"),
        new ZBrushCommandDefinition("mask_all", "มาสก์", "มาสก์ทั้งวัตถุ", "Mask All", "press", "Tool:Masking:Mask All", 0, 0, 0, "มาสก์ผิวทั้งหมด; UI path ตรวจเมื่อรัน", false),
        new ZBrushCommandDefinition("clear_mask", "มาสก์", "ล้างมาสก์", "Clear Mask", "press", "Tool:Masking:Clear", 0, 0, 0, "ล้างมาสก์"),
        new ZBrushCommandDefinition("invert_mask", "มาสก์", "กลับด้านมาสก์", "Inverse Mask", "press", "Tool:Masking:Inverse", 0, 0, 0, "สลับด้านมาสก์"),
        new ZBrushCommandDefinition("custom_press", "เมนูอื่น", "กดเมนูตาม UI path", "Press UI Path", "press", "", 0, 0, 0, "พิมพ์ UI path ของ ZBrush เช่น Tool:Geometry:Divide"),
        new ZBrushCommandDefinition("custom_set", "เมนูอื่น", "ตั้งค่าเมนูตาม UI path", "Set UI Path", "set", "", 0, -1000000, 1000000, "พิมพ์ UI path และค่าตัวเลข"),
        new ZBrushCommandDefinition("custom_toggle", "เมนูอื่น", "สลับเมนูตาม UI path", "Toggle UI Path", "toggle", "", 0, 0, 0, "พิมพ์ UI path ของสวิตช์ใน ZBrush")
    };
}
