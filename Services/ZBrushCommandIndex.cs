using System.Xml;
using System.Xml.Linq;

namespace ThaiModelStudio;

/// <summary>Combines hand-labelled commands with the UI command inventory shipped by the selected ZBrush.</summary>
public sealed record ZBrushCommandIndex(
    IReadOnlyList<ZBrushCommandDefinition> Items, int NativeCommandCount, string? SourcePath)
{
    private static readonly IReadOnlyDictionary<string, string> PaletteNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ALPHA"] = "อัลฟา · Alpha", ["BRUSH"] = "หัวแปรง · Brush", ["COLOR"] = "สี · Color",
        ["DOCUMENT"] = "เอกสาร · Document", ["DRAW"] = "การวาด · Draw", ["DYNAMICS"] = "ไดนามิก · Dynamics",
        ["EDIT"] = "แก้ไข · Edit", ["FILE"] = "ไฟล์ · File", ["LAYER"] = "เลเยอร์ · Layer",
        ["LIGHT"] = "แสง · Light", ["MACRO"] = "มาโคร · Macro", ["MARKER"] = "เครื่องหมาย · Marker",
        ["MATERIAL"] = "วัสดุ · Material", ["MOVIE"] = "วิดีโอ · Movie", ["PICKER"] = "ตัวเลือก · Picker",
        ["PREFERENCES"] = "การตั้งค่า · Preferences", ["RENDER"] = "เรนเดอร์ · Render",
        ["STENCIL"] = "ลายฉลุ · Stencil", ["STROKE"] = "เส้นแปรง · Stroke",
        ["TEXTURE"] = "พื้นผิว · Texture", ["TOOL"] = "เครื่องมือ · Tool",
        ["TRANSFORM"] = "แปลงรูป · Transform", ["ZSCRIPT"] = "สคริปต์ · ZScript"
    };

    private static readonly IReadOnlyDictionary<string, string> CommonNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Rgb Intensity"] = "ความเข้มสี RGB", ["Create"] = "สร้าง", ["Edit"] = "แก้ไข",
        ["Import"] = "นำเข้า", ["Export"] = "ส่งออก", ["Save"] = "บันทึก",
        ["Delete"] = "ลบ", ["Clear"] = "ล้าง", ["Duplicate"] = "ทำสำเนา",
        ["Show English"] = "แสดงภาษาอังกฤษ", ["Show untranslated"] = "แสดงรายการที่ยังไม่แปล",
        ["Custom"] = "ภาษากำหนดเอง", ["Auto-edit"] = "แก้คำแปลด้วยคลิก",
        ["Reload Notes"] = "โหลดคำอธิบายใหม่", ["Reload Icons"] = "โหลดไอคอนใหม่"
    };

    public static ZBrushCommandIndex Load(string? zbrushExecutable)
    {
        var items = new List<ZBrushCommandDefinition>(ZBrushCommandCatalog.All);
        if (string.IsNullOrWhiteSpace(zbrushExecutable) || !File.Exists(zbrushExecutable))
            return new ZBrushCommandIndex(items, 0, null);

        var source = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(zbrushExecutable))!,
            "ZData", "ZLang", "zcommands", "commands.xml");
        if (!File.Exists(source)) return new ZBrushCommandIndex(items, 0, null);

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(source, settings);
        var document = XDocument.Load(reader);
        var paths = document.Descendants("command")
            .Select(element => (string?)element.Attribute("id"))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var known = new HashSet<string>(items.Select(item => Normalize(item.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!known.Add(Normalize(path))) continue;
            var palette = path.Split(':', 2)[0];
            var category = PaletteNames.GetValueOrDefault(palette, "เมนู · " + palette);
            var leaf = path[(path.LastIndexOf(':') + 1)..];
            items.Add(new ZBrushCommandDefinition("xml:" + path, category,
                CommonNames.GetValueOrDefault(leaf, "เมนู " + leaf), path, "press", path, 0, -1_000_000, 1_000_000,
                "พบใน commands.xml ของ ZBrush ที่ติดตั้ง · เลือก กด/ตั้งค่า/สลับให้ตรงชนิดเมนู; ตรวจการใช้งานจริงตอนรัน"));
        }
        return new ZBrushCommandIndex(items, paths.Length, source);
    }

    private static string Normalize(string path) => new(path.Where(character => !char.IsWhiteSpace(character)).ToArray());
}
