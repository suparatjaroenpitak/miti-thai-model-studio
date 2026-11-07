using ThaiModelStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        form.Show();
        Application.DoEvents();
        if (!form.Visible || !form.Text.Contains("ZBrush", StringComparison.Ordinal))
            throw new Exception("ZBrush Thai form did not open");
        if (ZBrushCommandCatalog.All.Count < 20)
            throw new Exception("ZBrush command catalog is incomplete");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        using (var screenshot = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, screenshot.Size));
            screenshot.Save(Path.Combine(root, "artifacts", "zbrush-only-ui.png"));
        }
        var service = new ZBrushOnlyService();
        var job = service.CreateJob(Path.Combine(root, "artifacts", "zbrush-only-ui-smoke"),
            [new ZBrushAction("press", "Tool:Geometry:Divide")]);
        if (!File.Exists(job.ScriptPath) || !File.Exists(job.JobPath)) throw new Exception("ZBrush job missing");
        var standalone = Path.Combine(root, "artifacts", "zbrush-only-standalone-smoke.py");
        service.ExportStandaloneScript(standalone, [new ZBrushAction("set", "Draw:Draw Size", 42)]);
        if (!File.ReadAllText(standalone).Contains("zbrush", StringComparison.Ordinal))
            throw new Exception("Standalone ZBrush script missing API call");
        form.Close();
        Console.WriteLine("ZBRUSH_ONLY_UI_PASS");
    }
}
