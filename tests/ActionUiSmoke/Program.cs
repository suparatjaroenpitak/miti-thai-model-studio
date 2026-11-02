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
    }
}
