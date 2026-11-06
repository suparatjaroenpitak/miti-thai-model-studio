using System.Text.Json;

namespace ThaiModelStudio;

public sealed class ZBrushFunction
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ThaiName { get; set; } = "";
    public string Category { get; set; } = "";
    public string ThaiCategory { get; set; } = "";
    public int? Panel { get; set; }
    public string Status { get; set; } = "";
    public string Blender { get; set; } = "";
    public string Description { get; set; } = "";
    public string ThaiDescription { get; set; } = "";

    public string DisplayName => ApiCatalogService.BilingualName(Name, ThaiName);
    public string CategoryName => ApiCatalogService.BilingualName(Category, ThaiCategory);
    public string StatusThai => Status switch
    {
        "gui" => "ใช้งานผ่าน GUI",
        "mapped" => "แมปกับ Blender",
        _ => "ยังไม่รองรับ"
    };
}

public static class ZBrushCatalogService
{
    public static string CatalogPath => Path.Combine(AppContext.BaseDirectory, "engine", "zbrush_functions.json");
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<ZBrushFunction> Load()
    {
        if (!File.Exists(CatalogPath)) return [];
        var catalog = JsonSerializer.Deserialize<ZBrushCatalog>(File.ReadAllText(CatalogPath), Options)
            ?? throw new InvalidDataException("อ่านคลังฟังก์ชัน ZBrush-style ไม่สำเร็จ");
        return catalog.Entries;
    }

    private sealed class ZBrushCatalog
    {
        public int SchemaVersion { get; set; }
        public string Kind { get; set; } = "";
        public List<ZBrushFunction> Entries { get; set; } = [];
    }
}
