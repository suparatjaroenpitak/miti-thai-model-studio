using System.Reflection;
using System.Diagnostics;
using ThaiModelStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        using var main = new MainForm();
        main.CreateControl();
        var method = typeof(MainForm).GetMethod("OpenObjectActions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var menu = (ContextMenuStrip)typeof(MainForm).GetField("objectMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var search = (ToolStripTextBox)typeof(MainForm).GetField("objectMenuSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        for (var round = 0; round < 2; round++)
        {
            method.Invoke(main, ["CubeActionSmoke"]);
            search.Text = "subdivide";
            Application.DoEvents();
            if (menu.IsDisposed) throw new Exception("Context menu was disposed while in use");
            menu.Close();
            Application.DoEvents();
        }
        Console.WriteLine("ACTION_UI_SUCCESS: context menu reused after close");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ThaiModelStudio.csproj"))) directory = directory.Parent;
        Environment.SetEnvironmentVariable("MITI_API_CATALOG", Path.Combine(directory!.FullName, "engine", "blender_api.json"));
        using var host = new Form { Size = new Size(320, 700), ShowInTaskbar = false };
        var panel = new ObjectActionPanel { Dock = DockStyle.Fill };
        host.Controls.Add(panel); host.Show();
        panel.PickObject("CubeActionSmoke"); panel.SetQuery("bpy.ops.mesh.subdivide");
        var timer = Stopwatch.StartNew();
        while (panel.Matches.Count == 0 && timer.Elapsed < TimeSpan.FromSeconds(20)) { Application.DoEvents(); Thread.Sleep(20); }
        if (panel.Matches.Count == 0) throw new Exception("Operator search did not load");
        panel.Choose(panel.Matches.First(e => e.Id == "bpy.ops.mesh.subdivide"));
        var fields = (FlowLayoutPanel)typeof(ObjectActionPanel).GetField("fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        var cut = fields.Controls.OfType<NumericUpDown>().First();
        if (cut.Minimum != 1 || cut.Maximum != 100 || cut.Value != 1) throw new Exception("Number of Cuts controls do not match Blender metadata");
        cut.Value = 2;
        string? emitted = null; panel.ApplyRequested += (_, code) => emitted = code;
        var apply = (Button)typeof(ObjectActionPanel).GetField("apply", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        apply.PerformClick();
        if (emitted == null || !emitted.Contains("number_cuts=2")) throw new Exception("Parameter selection did not generate the expected command");
        Console.WriteLine("PARAMETER_UI_SUCCESS: typed input and Blender bounds");
        if (ObjectActionPanel.IsModelingOperator("bpy.ops.mask.add_vertex")) throw new Exception("Mask editor command appears as mesh modeling");
        panel.SetQuery("mask.add_vertex");
        if (panel.Matches.Count != 0) throw new Exception("Mask editor command appears in object search");
        panel.SetQuery("bpy.ops.mesh.subdivide"); panel.Choose(panel.Matches.First(e => e.Id == "bpy.ops.mesh.subdivide"));
        string? preview = null; panel.PreviewRequested += (_, code) => preview = code;
        fields = (FlowLayoutPanel)typeof(ObjectActionPanel).GetField("fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(panel)!;
        fields.Controls.OfType<NumericUpDown>().First().Value = 3;
        timer.Restart();
        while (preview == null && timer.Elapsed < TimeSpan.FromSeconds(3)) { Application.DoEvents(); Thread.Sleep(20); }
        if (preview == null || !preview.Contains("number_cuts=3")) throw new Exception("Live preview was not scheduled with updated parameter");
        Console.WriteLine("LIVE_PREVIEW_UI_SUCCESS: debounce generated updated command");
        var blender = BlenderService.FindBlender() ?? throw new Exception("Blender not installed for preview smoke test");
        var source = "import bpy\nbpy.ops.mesh.primitive_cube_add()\nbpy.context.object.name = 'CubeActionSmoke'\n" + preview;
        var previewPath = Task.Run(() => new BlenderService().PreviewAsync(blender, source,
            Path.Combine(directory.FullName, "artifacts", "preview-service-smoke"), CancellationToken.None)).GetAwaiter().GetResult();
        if (!File.Exists(previewPath) || File.Exists(Path.Combine(Path.GetDirectoryName(previewPath)!, "scene.glb")))
            throw new Exception("Preview service did not create a lightweight preview");
        Console.WriteLine("PREVIEW_SERVICE_SUCCESS: Blender generated updated preview without export");
        panel.SetTargetObject("OtherCube");
        panel.SetQuery("bpy.ops.mesh.subdivide"); panel.Choose(panel.Matches.First(e => e.Id == "bpy.ops.mesh.subdivide"));
        emitted = null; apply.PerformClick();
        if (emitted == null || !emitted.Contains("bpy.data.objects.get(\"OtherCube\")"))
            throw new Exception("Changing object selection did not update the action target");
        Console.WriteLine("TARGET_SYNC_SUCCESS: action follows selected object");
    }
}
