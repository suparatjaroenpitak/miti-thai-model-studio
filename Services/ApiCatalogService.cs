using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ThaiModelStudio;

public sealed class ApiCatalog
{
    public int SchemaVersion { get; set; }
    public JsonElement Blender { get; set; }
    public JsonElement Counts { get; set; }
    public JsonElement Coverage { get; set; }
    public JsonElement TranslationStats { get; set; }
    public JsonElement Localization { get; set; }
    public List<ApiEntry> Entries { get; set; } = [];
}

public sealed class ApiEntry
{
    public string Id { get; set; } = "";
    public string? ApiPath { get; set; }
    public string Kind { get; set; } = "";
    public string Category { get; set; } = "";
    public string? ThaiCategory { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ThaiName { get; set; }
    public string? ThaiDescription { get; set; }
    public string? ThaiPath { get; set; }
    public string? ThaiIdentifier { get; set; }
    public string? NameTranslationSource { get; set; }
    public bool NameTranslationComplete { get; set; }
    public string? DescriptionTranslationSource { get; set; }
    public string? OwnerType { get; set; }
    public string? Type { get; set; }
    public bool ReadOnly { get; set; }
    public bool IsEnumFlag { get; set; }
    public bool? PollInFactoryContext { get; set; }
    public JsonElement Default { get; set; }
    public List<ApiParameter> Parameters { get; set; } = [];
    public List<ApiEnumItem> EnumItems { get; set; } = [];
    public string DisplayName => string.IsNullOrWhiteSpace(ThaiName) ? Name : ThaiName;
    public string KindThai => ApiCatalogService.KindThai(Kind);
}

public sealed class ApiParameter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ThaiName { get; set; }
    public string? ThaiIdentifier { get; set; }
    public string Type { get; set; } = "";
    public int ArrayLength { get; set; }
    public string? Subtype { get; set; }
    public JsonElement Minimum { get; set; }
    public JsonElement Maximum { get; set; }
    public string Description { get; set; } = "";
    public string? ThaiDescription { get; set; }
    public bool Required { get; set; }
    public string? ParameterKind { get; set; }
    public bool ReadOnly { get; set; }
    public bool IsEnumFlag { get; set; }
    public JsonElement Default { get; set; }
    public List<ApiEnumItem> EnumItems { get; set; } = [];
    public string DisplayName => string.IsNullOrWhiteSpace(ThaiName) ? Name : ThaiName;
}

public sealed class ApiEnumItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ThaiName { get; set; }
}

public static class ApiCatalogService
{
    public static string InstalledPath => Path.Combine(AppContext.BaseDirectory, "engine", "blender_api.json");
    public static string CustomPath => Path.Combine(AppContext.BaseDirectory, "UserData", "ApiCatalog", "blender_api.json");
    public static string ResolvePath()
    {
        var configured = Environment.GetEnvironmentVariable("MITI_API_CATALOG");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        return File.Exists(CustomPath) ? CustomPath : InstalledPath;
    }
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, MaxDepth = 80 };
    public static Task<ApiCatalog> LoadAsync(CancellationToken cancellation = default) => LoadAsync(ResolvePath(), cancellation);
    public static async Task<ApiCatalog> LoadAsync(string path, CancellationToken cancellation = default)
    {
        await using var stream = File.OpenRead(path);
        var catalog = await JsonSerializer.DeserializeAsync<ApiCatalog>(stream, Options, cancellation)
            ?? throw new InvalidDataException("อ่านคลังฟังก์ชัน Blender ไม่สำเร็จ");
        if (catalog.Entries.Count == 0) throw new InvalidDataException("คลังฟังก์ชันยังไม่มีข้อมูล กรุณาสแกน Blender อีกครั้ง");
        return catalog;
    }
    public static string KindThai(string kind) => kind switch
    {
        "operator" => "คำสั่ง", "type" => "ชนิดข้อมูล", "function" => "เมธอด",
        "property" => "คุณสมบัติ", "moduleFunction" => "ฟังก์ชันโมดูล", _ => kind
    };
    public static string TranslationThai(string? source) => source switch
    {
        "official" or "blender" => "คำแปลจาก Blender", "manual" or "curated" => "คำแปลที่จัดทำไว้",
        "glossary" => "แปลจากคำศัพท์", "untranslated" or null => "ยังมีคำที่ต้องแปล", _ => "คำแปล " + source
    };
    public static string DataTypeThai(string type) => type switch
    {
        "BOOLEAN" => "จริง/เท็จ", "INT" => "จำนวนเต็ม", "FLOAT" => "ทศนิยม", "STRING" => "ข้อความ",
        "ENUM" => "ตัวเลือก", "POINTER" => "อ้างอิง", "COLLECTION" => "ชุดข้อมูล", "PYTHON" => "ข้อมูล Python", _ => type
    };
    public static string PythonString(string value) => JsonSerializer.Serialize(value, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    public static string PythonValue(JsonElement value, string type = "") => value.ValueKind switch
    {
        JsonValueKind.String => PythonString(value.GetString() ?? ""),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "True", JsonValueKind.False => "False", JsonValueKind.Null => "None",
        JsonValueKind.Array => "(" + string.Join(", ", value.EnumerateArray().Select(v => PythonValue(v))) + (value.GetArrayLength() == 1 ? "," : "") + ")",
        _ => type switch { "STRING" or "ENUM" => "\"\"", "BOOLEAN" => "False", "INT" => "0", "FLOAT" => "0.0", _ => "None" }
    };
    public static string ParameterKey(ApiParameter p) => !string.IsNullOrWhiteSpace(p.ThaiIdentifier)
        ? p.ThaiIdentifier : p.Id;

    public static async Task ScanAsync(string blenderPath, IProgress<string>? progress, CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(CustomPath)!;
        Directory.CreateDirectory(directory);
        var scan = Path.Combine(directory, "scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scan);
        var raw = Path.Combine(scan, "raw.json"); var localized = Path.Combine(scan, "blender_api.json");
        var start = new ProcessStartInfo(blenderPath) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "--background", "--factory-startup", "--python-exit-code", "1", "--python",
            Path.Combine(AppContext.BaseDirectory, "engine", "extract_api.py"), "--", "--output", raw, "--localized-output", localized }) start.ArgumentList.Add(a);
        start.Environment["PYTHONIOENCODING"] = "utf-8"; start.Environment["PYTHONUTF8"] = "1";
        using var process = new Process { StartInfo = start };
        var tail = new List<string>(); var gate = new object();
        void Read(string? line) { if (line == null) return; lock (gate) { tail.Add(line); if (tail.Count > 30) tail.RemoveAt(0); } progress?.Report(line); }
        process.OutputDataReceived += (_, e) => Read(e.Data); process.ErrorDataReceived += (_, e) => Read(e.Data);
        cancellation.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("เปิด Blender เพื่อสแกนไม่สำเร็จ");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        await process.WaitForExitAsync(cancellation); process.WaitForExit(); cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new InvalidOperationException("สแกน Blender ไม่สำเร็จ\n" + string.Join("\n", tail));
        await LoadAsync(localized, cancellation);
        File.Move(localized, CustomPath, true);
        File.Move(raw, Path.Combine(directory, "raw.json"), true);
        progress?.Report("บันทึกคลัง API ใหม่แล้ว");
    }
}
