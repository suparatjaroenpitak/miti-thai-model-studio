using System.Reflection;
using System.Text;
using System.Text.Json;
using ThaiModelStudio;
using ThaiModelStudio.Controls;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        using var panel = new ZBrushEnginePanel();
        using var host = new Form { Size = new Size(260, 700), ShowInTaskbar = false };
        panel.Dock = DockStyle.Fill; host.Controls.Add(panel); host.Show(); Application.DoEvents();
        panel.SetTargetObject("ตัวกล่อง");
        panel.SetObjects(["ตัวกล่อง", "ตัวตัด"]);

        var type = typeof(ZBrushEnginePanel);
        string Call(string name) => (string)type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null)!;
        void Select(string field, string value)
        {
            var combo = (ComboBox)type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
            for (var i = 0; i < combo.Items.Count; i++)
                if (combo.Items[i]!.ToString() == value) { combo.SelectedIndex = i; return; }
            throw new Exception($"Choice {value} not found in {field}");
        }
        Select("source", "ตัวตัด");
        Select("boolTool", "ตัวตัด");

        var code = new StringBuilder()
            .AppendLine("import bpy")
            .AppendLine("bpy.ops.mesh.primitive_cube_add(size=2)")
            .AppendLine("bpy.context.object.name = 'ตัวกล่อง'")
            .AppendLine("bpy.ops.mesh.primitive_uv_sphere_add(radius=1.4, location=(3, 0, 0))")
            .AppendLine("bpy.context.object.name = 'ตัวตัด'")
            .AppendLine(Call("InsertNewSnippet"))
            .AppendLine(Call("TransposeSnippet"))
            .AppendLine(Call("NoiseSnippet"))
            .AppendLine(Call("LayerSnippet"))
            .AppendLine(Call("ZSphereSnippet"))
            .AppendLine(Call("FibreSnippet"))
            .AppendLine(Call("NanoSnippet"))
            .AppendLine(Call("ProjectSnippet"))
            .AppendLine(Call("ArraySnippet"))
            .AppendLine(Call("BooleanSnippet"));

        var blender = BlenderService.FindBlender() ?? throw new Exception("Blender not installed");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ThaiModelStudio.csproj"))) directory = directory.Parent;
        var job = Path.Combine(directory!.FullName, "artifacts", "zbrush-panel-smoke");
        var previewPath = Task.Run(() => new BlenderService().PreviewAsync(blender, code.ToString(), job,
            CancellationToken.None)).GetAwaiter().GetResult();
        using var data = JsonDocument.Parse(File.ReadAllText(previewPath));
        var objects = data.RootElement.GetProperty("objects");
        var names = objects.EnumerateArray().Select(o => o.GetProperty("name").GetString()!).ToArray();
        if (!names.Contains("ZSphere")) throw new Exception("ZSphere base mesh missing: " + string.Join(",", names));
        if (!names.Contains("MitiFiberMesh")) throw new Exception("FiberMesh strands missing");
        if (names.Contains("ตัวตัด")) throw new Exception("SubTool Boolean did not consume the tool object");
        var cube = objects.EnumerateArray().First(o => o.GetProperty("name").GetString() == "ตัวกล่อง");
        if (cube.GetProperty("vertices").GetArrayLength() < 100) throw new Exception("ZBrush engine operations did not add geometry to the SubTool");
        Console.WriteLine($"ZBRUSH_PANEL_SMOKE_PASS objects={names.Length} verts={cube.GetProperty("vertices").GetArrayLength()}");

        var exchange = Path.Combine(job, "native-exchange");
        var simple = "import bpy\nbpy.ops.mesh.primitive_cube_add()\nbpy.context.object.name = 'Selected'\n" +
                     "bpy.ops.mesh.primitive_cube_add(location=(4, 0, 0))\nbpy.context.object.name = 'Other'\n";
        var service = new BlenderService();
        var built = Task.Run(() => service.BuildAsync(blender, simple, exchange, null, CancellationToken.None)).GetAwaiter().GetResult();
        var objPath = Path.Combine(exchange, "selected.obj");
        Task.Run(() => service.ExportSelectionObjAsync(blender, built.ScenePath, "Selected", objPath,
            null, CancellationToken.None)).GetAwaiter().GetResult();
        var objLines = File.ReadAllLines(objPath);
        var vertices = objLines.Count(line => line.StartsWith("v ", StringComparison.Ordinal));
        var objectsInObj = objLines.Where(line => line.StartsWith("o ", StringComparison.Ordinal)).ToArray();
        if (vertices != 8 || objectsInObj.Length != 1 || !objectsInObj[0].Contains("Selected", StringComparison.Ordinal))
            throw new Exception($"Selection export leaked another object: vertices={vertices}, objects={string.Join(",", objectsInObj)}");
        Console.WriteLine("ZBRUSH_SELECTION_EXPORT_PASS");
    }
}
