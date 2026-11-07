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
        var fixture = Path.Combine(root, "artifacts", "zbrush-index-smoke");
        var fixtureXml = Path.Combine(fixture, "ZData", "ZLang", "zcommands", "commands.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(fixtureXml)!);
        File.WriteAllText(Path.Combine(fixture, "ZBrush.exe"), "fixture");
        File.WriteAllText(fixtureXml, "<command_set><menu id='DRAW'><command id='Draw:Rgb Intensity'/><command id='Tool:Geometry:Divide'/></menu></command_set>");
        var fixtureIndex = ZBrushCommandIndex.Load(Path.Combine(fixture, "ZBrush.exe"));
        if (fixtureIndex.NativeCommandCount != 2 ||
            fixtureIndex.Items.Count(item => item.Path == "Tool:Geometry:Divide") != 1 ||
            !fixtureIndex.Items.Any(item => item.Id == "xml:Draw:Rgb Intensity"))
            throw new Exception("ZBrush command inventory was not merged correctly");
        var installed = ZBrushOnlyService.FindZBrush();
        if (installed != null && ZBrushCommandIndex.Load(installed).NativeCommandCount > 0)
        {
            var controls = Descendants(form).ToArray();
            var search = controls.OfType<TextBox>().Single(control => control.PlaceholderText.Contains("ค้นหาไทย", StringComparison.Ordinal));
            search.Text = "Draw:Rgb Intensity";
            Application.DoEvents();
            var list = controls.OfType<ListBox>().Single(control => control.Items.Cast<object>().Any(item => item is ZBrushCommandDefinition));
            if (list.SelectedItem is not ZBrushCommandDefinition command || command.Path != "Draw:Rgb Intensity")
                throw new Exception("The installed ZBrush menu was not found from search");
            var operation = controls.OfType<ComboBox>().Single(control => control.Items.Cast<object>()
                .Any(item => item?.ToString()?.Contains("Toggle", StringComparison.Ordinal) == true));
            operation.SelectedIndex = 1;
            controls.OfType<Button>().Single(control => control.Text.Contains("เพิ่มลงคิว", StringComparison.Ordinal)).PerformClick();
            var queue = controls.OfType<ListBox>().Single(control => control != list);
            if (queue.Items.Count != 1 || !queue.Items[0]!.ToString()!.Contains("ตั้งค่า · Draw:Rgb Intensity", StringComparison.Ordinal))
                throw new Exception("Set action was not added from the discovered menu");
        }
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

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
