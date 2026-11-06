using System.Numerics;
using System.Reflection;
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
        using var panel = new ShapeTransformPanel(); panel.CreateControl();
        using var host = new Form { Size = new Size(240, 650), ShowInTaskbar = false };
        panel.Dock = DockStyle.Fill; host.Controls.Add(panel); host.Show(); Application.DoEvents();
        var type = typeof(ShapeTransformPanel);
        var shapes = (ComboBox)type.GetField("shapes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        if (!shapes.Items[0]!.ToString()!.StartsWith("Cube · กล่อง", StringComparison.Ordinal))
            throw new Exception("Shape choices should show Blender English before Thai");
        var visibleLabels = Descendants(panel).OfType<Label>().Select(l => l.Text).ToArray();
        if (!visibleLabels.Contains("Location X/Y/Z · ตำแหน่ง") || !visibleLabels.Contains("Rotation X/Y/Z (°) · มุมหมุน")
            || !visibleLabels.Contains("Scale X/Y/Z · สเกล"))
            throw new Exception("Transform parameters should show Blender English before Thai");
        var create = type.GetMethod("CreateSnippet", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var source = "import bpy\n";
        for (var i = 0; i < shapes.Items.Count; i++)
        {
            shapes.SelectedIndex = i;
            source += (string)create.Invoke(panel, null)!;
        }
        if (shapes.Items.Count != 8) throw new Exception("Expected eight primitive shapes");
        panel.SelectObject(new SceneObjectInfo("กล่องใหม่", 8, 12));
        foreach (var (field, vector) in new[] { ("position", new Vector3(2, 3, 4)),
            ("rotation", new Vector3(0, 0, 45)), ("scale", new Vector3(1.5f, 2, 1)) })
        {
            var input = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
            input.GetType().GetMethod("Set")!.Invoke(input, [vector]);
        }
        source += (string)type.GetMethod("TransformSnippet", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, null)!;
        var blender = BlenderService.FindBlender() ?? throw new Exception("Blender not installed");
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ThaiModelStudio.csproj"))) directory = directory.Parent;
        using (var image = new Bitmap(panel.Width, panel.Height))
        {
            panel.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            var screenshot = Path.Combine(directory!.FullName, "artifacts", "shape-tools-panel.png");
            Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
            image.Save(screenshot);
        }
        var previewPath = Task.Run(() => new BlenderService().PreviewAsync(blender, source,
            Path.Combine(directory!.FullName, "artifacts", "shape-tools-smoke"), CancellationToken.None)).GetAwaiter().GetResult();
        using var data = JsonDocument.Parse(File.ReadAllText(previewPath));
        var objects = data.RootElement.GetProperty("objects");
        if (objects.GetArrayLength() != 8) throw new Exception("Blender did not create eight shapes");
        var cube = objects.EnumerateArray().First(o => o.GetProperty("name").GetString() == "กล่องใหม่");
        var location = cube.GetProperty("location");
        if (location[0].GetSingle() != 2 || location[1].GetSingle() != 3 || location[2].GetSingle() != 4)
            throw new Exception("Move parameters did not reach Blender");
        var scale = cube.GetProperty("scale");
        if (Math.Abs(scale[0].GetSingle() - 1.5f) > 0.001f || scale[1].GetSingle() != 2)
            throw new Exception("Scale parameters did not reach Blender");
        var rotation = cube.GetProperty("rotationEuler");
        if (Math.Abs(rotation[2].GetSingle() - MathF.PI / 4) > 0.001f)
            throw new Exception("Rotation parameters did not reach Blender");
        using var viewport = new ModelViewport { Size = new Size(640, 480) };
        viewport.CreateControl(); viewport.LoadScene(previewPath);
        var selected = viewport.Objects.First(o => o.Name == "กล่องใหม่");
        if (selected.Location != new Vector3(2, 3, 4) || Math.Abs(selected.RotationDegrees.Z - 45) > 0.01f)
            throw new Exception("Selected object's transform was not restored to the right-side panel");
        Console.WriteLine("SHAPE_TOOLS_SUCCESS: eight shapes and move/rotate/scale verified in Blender");
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
