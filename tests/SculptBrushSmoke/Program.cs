using System.Reflection;
using System.Numerics;
using System.Text.Json;
using ThaiModelStudio;
using ThaiModelStudio.Controls;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var blender = BlenderService.FindBlender() ?? throw new Exception("Blender not installed");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "ThaiModelStudio.csproj"))) root = root.Parent;
        var output = Path.Combine(root!.FullName, "artifacts", "sculpt-brush-smoke");
        var service = new BlenderService();
        const string source = "import bpy\nbpy.ops.mesh.primitive_cube_add()\nbpy.context.object.name = 'BrushCube'\n";
        var beforePath = Task.Run(() => service.PreviewAsync(blender, source, output, CancellationToken.None)).GetAwaiter().GetResult();
        using var host = new Form { ClientSize = new Size(640, 480), ShowInTaskbar = false };
        var viewport = new ModelViewport { Dock = DockStyle.Fill, SculptEnabled = true, SculptRadius = 0.8f };
        host.Controls.Add(viewport); host.Show(); Application.DoEvents(); viewport.LoadScene(beforePath);
        using (var image = new Bitmap(viewport.Width, viewport.Height))
        {
            viewport.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            Directory.CreateDirectory(output);
            image.Save(Path.Combine(output, "viewport.png"));
        }
        SculptStroke? stroke = null;
        viewport.SculptStrokeCompleted += (_, value) => stroke = value;
        var mouse = typeof(ModelViewport).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var move = typeof(ModelViewport).GetMethod("OnMouseMove", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var up = typeof(ModelViewport).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance)!;
        mouse.Invoke(viewport, [new MouseEventArgs(MouseButtons.Left, 1, 320, 240, 0)]);
        move.Invoke(viewport, [new MouseEventArgs(MouseButtons.Left, 0, 334, 243, 0)]);
        up.Invoke(viewport, [new MouseEventArgs(MouseButtons.Left, 1, 334, 243, 0)]);
        if (stroke is null || stroke.ObjectName != "BrushCube" || stroke.Samples.Count == 0)
            throw new Exception("Viewport did not record a brush stroke on the cube");
        using var controls = new SculptBrushPanel();
        controls.SetActive(true);
        using var brushHost = new Form { ClientSize = new Size(240, 480), ShowInTaskbar = false };
        controls.Dock = DockStyle.Fill; brushHost.Controls.Add(controls); brushHost.Show(); Application.DoEvents();
        using (var image = new Bitmap(controls.Width, controls.Height))
        {
            controls.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(output, "brush-panel.png"));
        }
        var snippet = controls.GenerateSnippet(stroke);
        if (!snippet.Contains("_miti_sculpt_stroke") || !snippet.Contains("mode=\"STANDARD\""))
            throw new Exception("Brush stroke was not written into source code");
        var afterPath = Task.Run(() => service.PreviewAsync(blender, source + snippet, output, CancellationToken.None)).GetAwaiter().GetResult();
        using var before = JsonDocument.Parse(File.ReadAllText(beforePath));
        using var after = JsonDocument.Parse(File.ReadAllText(afterPath));
        var previous = before.RootElement.GetProperty("objects")[0].GetProperty("vertices");
        var current = after.RootElement.GetProperty("objects")[0].GetProperty("vertices");
        if (current.GetArrayLength() <= previous.GetArrayLength()) throw new Exception("Brush did not add sculpt detail to the cube");
        if (!current.EnumerateArray().Any(v => Math.Abs(v[0].GetDouble()) != 1 && Math.Abs(v[1].GetDouble()) != 1 && Math.Abs(v[2].GetDouble()) != 1))
            throw new Exception("Brush did not deform vertices");
        var built = Task.Run(() => service.BuildAsync(blender, source + snippet, output, null, CancellationToken.None)).GetAwaiter().GetResult();
        if (!File.Exists(built.ScenePath) || !File.Exists(built.GlbPath)) throw new Exception("Sculpted scene was not saved");
        foreach (var mode in new[] { "CLAY", "INFLATE", "SMOOTH", "FLATTEN", "PINCH", "MOVE", "CREASE" })
        {
            controls.SetMode(mode);
            var modePath = Task.Run(() => service.PreviewAsync(blender, source + snippet + controls.GenerateSnippet(stroke), output, CancellationToken.None)).GetAwaiter().GetResult();
            using var modeResult = JsonDocument.Parse(File.ReadAllText(modePath));
            if (modeResult.RootElement.GetProperty("objects")[0].GetProperty("vertices").GetArrayLength() == 0)
                throw new Exception("Brush mode " + mode + " produced no mesh");
        }
        controls.SetMode("STANDARD"); controls.ToggleSymmetryX();
        var symmetricStroke = new SculptStroke("BrushCube", [new SculptSample(new Vector3(0.7f, 0, 1), Vector3.UnitZ)]);
        var symmetricPath = Task.Run(() => service.PreviewAsync(blender, source + controls.GenerateSnippet(symmetricStroke), output, CancellationToken.None)).GetAwaiter().GetResult();
        using var symmetric = JsonDocument.Parse(File.ReadAllText(symmetricPath));
        var symmetricVertices = symmetric.RootElement.GetProperty("objects")[0].GetProperty("vertices").EnumerateArray().ToArray();
        if (!symmetricVertices.Any(v => v[0].GetDouble() > 0.1 && v[2].GetDouble() > 1.0001)
            || !symmetricVertices.Any(v => v[0].GetDouble() < -0.1 && v[2].GetDouble() > 1.0001))
            throw new Exception("X symmetry did not sculpt both sides");
        controls.SetSubtract(true);
        var subtractPath = Task.Run(() => service.PreviewAsync(blender, source + controls.GenerateSnippet(symmetricStroke), output, CancellationToken.None)).GetAwaiter().GetResult();
        using var subtracted = JsonDocument.Parse(File.ReadAllText(subtractPath));
        if (!subtracted.RootElement.GetProperty("objects")[0].GetProperty("vertices").EnumerateArray().Any(v => v[2].GetDouble() < 0.9999 && v[2].GetDouble() > 0.5))
            throw new Exception("ZSub did not cut into the surface");
        using var main = new MainForm(); main.CreateControl();
        var integratedPanel = (SculptBrushPanel)typeof(MainForm).GetField("sculptTools", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        var integratedViewport = (ModelViewport)typeof(MainForm).GetField("viewport", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
        integratedPanel.SetActive(true);
        if (!integratedViewport.SculptEnabled || Math.Abs(integratedViewport.SculptRadius - integratedPanel.Radius) > 0.001f)
            throw new Exception("Main window did not enable its viewport brush");
        Console.WriteLine($"SCULPT_BRUSH_SUCCESS: {stroke.Samples.Count} samples, {previous.GetArrayLength()} -> {current.GetArrayLength()} vertices");
    }
}
