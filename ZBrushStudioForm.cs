using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>Thai-first ZBrush command panel. All modeling is performed by ZBrush itself.</summary>
public sealed class MainForm : Form
{
    private readonly ZBrushOnlyService service = new();
    private readonly List<ZBrushAction> queue = [];
    private readonly TextBox search = Input();
    private readonly ComboBox categories = Choice();
    private readonly ListBox commands = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel, ForeColor = Theme.Text, IntegralHeight = false,
        DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 43 };
    private readonly ListBox queued = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel, ForeColor = Theme.Text, IntegralHeight = false, ItemHeight = 27 };
    private readonly TextBox uiPath = Input();
    private readonly TextBox filePath = Input();
    private readonly TextBox projectPath = Input();
    private readonly NumericUpDown value = Number(0, -1000000, 1000000, 2);
    private readonly NumericUpDown subtoolIndex = Number(0, 0, 10000, 0);
    private readonly NumericUpDown[] move = [Number(0, -100000, 100000, 2), Number(0, -100000, 100000, 2), Number(0, -100000, 100000, 2)];
    private readonly NumericUpDown[] rotate = [Number(0, -36000, 36000, 1), Number(0, -36000, 36000, 1), Number(0, -36000, 36000, 1)];
    private readonly NumericUpDown[] scale = [Number(1, 0.001m, 1000, 3), Number(1, 0.001m, 1000, 3), Number(1, 0.001m, 1000, 3)];
    private readonly NumericUpDown[] color = [Number(255, 0, 255, 0), Number(255, 0, 255, 0), Number(255, 0, 255, 0)];
    private readonly Label selectedTitle = Theme.Label("เลือกคำสั่ง · Select a command", 15, Theme.Text, true);
    private readonly Label selectedHelp = Theme.Label("", 9, Theme.Muted);
    private readonly Label status = Theme.Label("พร้อมใช้งาน · Ready", 9, Theme.Muted);
    private readonly Label engine = Theme.Label("", 9, Theme.Muted);
    private readonly RichTextBox log = new() { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Background,
        ForeColor = Theme.Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9), WordWrap = true };
    private readonly Panel pathRow = new() { Width = 555, Height = 70, BackColor = Theme.Panel };
    private readonly Panel projectRow = new() { Width = 555, Height = 76, BackColor = Theme.Panel };
    private readonly Panel valueRow = new() { Width = 555, Height = 70, BackColor = Theme.Panel };
    private readonly Panel fileRow = new() { Width = 555, Height = 76, BackColor = Theme.Panel };
    private readonly Panel indexRow = new() { Width = 555, Height = 70, BackColor = Theme.Panel };
    private readonly Panel transformRow = new() { Width = 555, Height = 176, BackColor = Theme.Panel };
    private readonly Panel colorRow = new() { Width = 555, Height = 78, BackColor = Theme.Panel };
    private readonly Button runButton;
    private readonly Button stopButton;
    private ZBrushCommandDefinition? selected;
    private string? zbrushPath;
    private CancellationTokenSource? operation;
    private readonly string dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MitiZBrushThai");
    private bool Busy => operation != null;

    public MainForm()
    {
        Text = "มิติ ZBrush ไทย · Thai ZBrush Studio 0.11";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1380, 900); MinimumSize = new Size(1050, 700);
        BackColor = Theme.Background; ForeColor = Theme.Text;
        AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;
        Theme.UseDarkWindow(this);
        Theme.UseDarkControls(commands, queued, log, search, uiPath, filePath, projectPath, value, subtoolIndex);
        foreach (var numeric in move.Concat(rotate).Concat(scale).Concat(color)) Theme.UseDarkControls(numeric);

        runButton = Theme.Button("▶  รันคิวใน ZBrush", async (_, _) => await RunQueueAsync(), true);
        stopButton = Theme.Button("■  หยุด", (_, _) => operation?.Cancel());
        stopButton.Enabled = false;

        var menu = BuildMenu();
        var header = BuildHeader();
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 37, Padding = new Padding(18, 7, 18, 0), BackColor = Theme.Panel };
        status.Dock = DockStyle.Fill; engine.Dock = DockStyle.Right; engine.AutoSize = true;
        footer.Controls.Add(status); footer.Controls.Add(engine);
        var main = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border, Size = new Size(1200, 680),
            SplitterWidth = 4, SplitterDistance = 330, Panel1MinSize = 255, Panel2MinSize = 580 };
        main.Panel1.Controls.Add(BuildCatalog());
        main.Panel2.Controls.Add(BuildDetails());
        Controls.Add(main); Controls.Add(footer); Controls.Add(header); Controls.Add(menu);
        MainMenuStrip = menu;

        search.TextChanged += (_, _) => FilterCommands();
        categories.SelectedIndexChanged += (_, _) => FilterCommands();
        commands.SelectedIndexChanged += (_, _) => ShowSelected();
        commands.DrawItem += DrawCommand;
        queued.DrawItem += DrawQueued;
        foreach (var category in new[] { "ทั้งหมด", "สร้าง", "ไฟล์", "SubTool", "โครงสร้าง", "หัวแปรง", "มุมมอง", "UV และสี", "มาสก์", "เมนูอื่น" })
            categories.Items.Add(category);
        categories.SelectedIndex = 0;
        LoadSettings();
        FilterCommands();
        FormClosing += (_, _) => operation?.Cancel();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Theme.Panel, ForeColor = Theme.Text,
            Font = new Font("Leelawadee UI", 10), Padding = new Padding(16, 6, 0, 6) };
        var file = new ToolStripMenuItem("ไฟล์ · File");
        file.DropDownItems.Add("เลือก ZBrush.exe…", null, (_, _) => SelectZBrush());
        file.DropDownItems.Add("เลือกโปรเจกต์ .ZPR…", null, (_, _) => BrowseProject());
        file.DropDownItems.Add("บันทึกสคริปต์ Python…", null, (_, _) => SaveScript());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("ออก · Exit", null, (_, _) => Close());
        var action = new ToolStripMenuItem("คำสั่ง · Actions");
        action.DropDownItems.Add("เพิ่มคำสั่งลงคิว", null, (_, _) => AddToQueue());
        action.DropDownItems.Add("รันคำสั่งที่เลือก", null, async (_, _) => await RunSelectedAsync());
        action.DropDownItems.Add("รันทั้งคิว", null, async (_, _) => await RunQueueAsync());
        var help = new ToolStripMenuItem("ช่วยเหลือ · Help");
        help.DropDownItems.Add("วิธีใช้งาน", null, (_, _) => MessageBox.Show(this,
            "1. เลือกคำสั่งภาษาไทยทางซ้าย\n2. ปรับพารามิเตอร์ด้านขวา\n3. กดรันทันที หรือเพิ่มหลายคำสั่งลงคิว\n4. ใช้หน้าต่าง ZBrush จริงเป็น viewport\n\nถ้ามี ZBrush เปิดอยู่ ให้บันทึกสคริปต์ .py แล้วโหลดผ่าน ZBrush > ZScript > Python Scripting > Load\nการกดรันจากแอปจะเปิด ZBrush process ใหม่; เลือก .ZPR เมื่อต้องการโหลดโปรเจกต์เดิม",
            "คู่มือ · Guide", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange([file, action, help]);
        return menu;
    }

    private Control BuildHeader()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 91, BackColor = Theme.Panel,
            Padding = new Padding(20, 10, 20, 8) };
        var title = Theme.Label("มิติ ZBrush ไทย", 20, Theme.Text, true);
        title.Location = new Point(22, 12); bar.Controls.Add(title);
        var caption = Theme.Label("คำสั่งภาษาไทย · ปรับค่าเป็นตัวเลข · ใช้เอนจิน ZBrush จริง", 9.4f, Theme.Muted);
        caption.Location = new Point(24, 54); bar.Controls.Add(caption);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 540, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, BackColor = Theme.Panel };
        runButton.Width = 175; stopButton.Width = 75;
        var open = Theme.Button("เปิด ZBrush", (_, _) => OpenZBrush()); open.Width = 120;
        buttons.Controls.Add(runButton); buttons.Controls.Add(stopButton); buttons.Controls.Add(open);
        bar.Controls.Add(buttons);
        return bar;
    }

    private Control BuildCatalog()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(14, 16, 12, 12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Panel };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        var heading = Theme.Label("ค้นหาเครื่องมือ ZBrush", 12, Theme.Text, true);
        table.Controls.Add(heading, 0, 0);
        search.PlaceholderText = "ค้นหาไทย / English / UI path"; search.Dock = DockStyle.Fill;
        table.Controls.Add(search, 0, 1);
        categories.Dock = DockStyle.Fill; table.Controls.Add(categories, 0, 2);
        table.Controls.Add(commands, 0, 3);
        var note = Theme.Label("เมนูอื่น: ใส่ UI path ของ ZBrush ได้เอง\nรายการที่ไม่ยืนยันจะตรวจใน ZBrush ตอนรัน", 8.3f, Theme.Muted);
        table.Controls.Add(note, 0, 4);
        panel.Controls.Add(table);
        return panel;
    }

    private Control BuildDetails()
    {
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = Theme.Background, Padding = new Padding(18, 14, 18, 10) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 193));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        selectedTitle.Location = new Point(4, 4); heading.Controls.Add(selectedTitle);
        selectedHelp.Location = new Point(5, 39); selectedHelp.MaximumSize = new Size(790, 42); heading.Controls.Add(selectedHelp);
        body.Controls.Add(heading, 0, 0);
        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, BackColor = Theme.Panel, Padding = new Padding(15, 13, 8, 12) };
        settings.Controls.Add(MakeProjectRow());
        settings.Controls.Add(MakePathRow());
        settings.Controls.Add(MakeValueRow());
        settings.Controls.Add(MakeFileRow());
        settings.Controls.Add(MakeIndexRow());
        settings.Controls.Add(MakeTransformRow());
        settings.Controls.Add(MakeColorRow());
        var actionButtons = new FlowLayoutPanel { Width = 555, Height = 49, BackColor = Theme.Panel, WrapContents = false };
        var add = Theme.Button("＋ เพิ่มลงคิว", (_, _) => AddToQueue()); add.Width = 150;
        var now = Theme.Button("▶ รันคำสั่งนี้", async (_, _) => await RunSelectedAsync(), true); now.Width = 175;
        actionButtons.Controls.Add(add); actionButtons.Controls.Add(now); settings.Controls.Add(actionButtons);
        body.Controls.Add(settings, 0, 1);
        body.Controls.Add(BuildQueue(), 0, 2);
        var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(0, 8, 0, 0) };
        var logTitle = Theme.Label("ผลการทำงาน · Activity", 10, Theme.Muted, true);
        logTitle.Dock = DockStyle.Top; logTitle.Height = 28; logPanel.Controls.Add(log); logPanel.Controls.Add(logTitle);
        body.Controls.Add(logPanel, 0, 3);
        return body;
    }

    private Control BuildQueue()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(14, 9, 14, 10) };
        var title = Theme.Label("ลำดับคำสั่ง · Action queue", 10.5f, Theme.Text, true);
        title.Dock = DockStyle.Top; title.Height = 30;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 39, BackColor = Theme.Panel, WrapContents = false };
        var remove = Theme.Button("ลบที่เลือก", (_, _) => RemoveFromQueue()); remove.Width = 110;
        var clear = Theme.Button("ล้างคิว", (_, _) => { queue.Clear(); RefreshQueue(); }); clear.Width = 90;
        var save = Theme.Button("บันทึก .py", (_, _) => SaveScript()); save.Width = 120;
        buttons.Controls.Add(remove); buttons.Controls.Add(clear); buttons.Controls.Add(save);
        panel.Controls.Add(queued); panel.Controls.Add(buttons); panel.Controls.Add(title);
        return panel;
    }

    private Control MakePathRow()
    {
        AddField(pathRow, "ZBrush UI path · ที่อยู่เมนู", uiPath);
        uiPath.Width = 505;
        return pathRow;
    }
    private Control MakeProjectRow()
    {
        var caption = Theme.Label("Project .ZPR · โปรเจกต์ที่จะเปิดใน ZBrush (ไม่บังคับ)", 9, Theme.Muted);
        caption.Location = new Point(0, 2); projectRow.Controls.Add(caption);
        projectPath.Location = new Point(0, 28); projectPath.Width = 410; projectRow.Controls.Add(projectPath);
        projectPath.Leave += (_, _) => SaveSettings();
        var browse = Theme.Button("เลือก .ZPR…", (_, _) => BrowseProject()); browse.Location = new Point(418, 26); browse.Width = 110;
        projectRow.Controls.Add(browse);
        return projectRow;
    }
    private Control MakeValueRow()
    {
        AddField(valueRow, "Value · ค่าตัวเลข", value);
        value.Width = 185;
        return valueRow;
    }
    private Control MakeFileRow()
    {
        var caption = Theme.Label("File · ไฟล์ OBJ", 9, Theme.Muted);
        caption.Location = new Point(0, 2); fileRow.Controls.Add(caption);
        filePath.Location = new Point(0, 28); filePath.Width = 410; fileRow.Controls.Add(filePath);
        var browse = Theme.Button("เลือกไฟล์…", (_, _) => BrowseMesh()); browse.Location = new Point(418, 26); browse.Width = 110;
        fileRow.Controls.Add(browse); return fileRow;
    }
    private Control MakeIndexRow()
    {
        AddField(indexRow, "SubTool index · ลำดับวัตถุ (เริ่มที่ 0)", subtoolIndex);
        subtoolIndex.Width = 185; return indexRow;
    }
    private Control MakeTransformRow()
    {
        var caption = Theme.Label("Canvas Transform · ปรับการแสดงเครื่องมือ", 9, Theme.Muted);
        caption.Location = new Point(0, 0); transformRow.Controls.Add(caption);
        AddVector(transformRow, "ย้าย · Move", move, 31);
        AddVector(transformRow, "หมุน ° · Rotate", rotate, 79);
        AddVector(transformRow, "สเกล × · Scale", scale, 127);
        return transformRow;
    }
    private Control MakeColorRow()
    {
        var caption = Theme.Label("Color · สี RGB", 9, Theme.Muted);
        caption.Location = new Point(0, 0); colorRow.Controls.Add(caption);
        AddVector(colorRow, "สี · Color", color, 28, "RGB");
        return colorRow;
    }

    private static void AddField(Panel panel, string label, Control control)
    {
        var caption = Theme.Label(label, 9, Theme.Muted); caption.Location = new Point(0, 2); panel.Controls.Add(caption);
        control.Location = new Point(0, 28); panel.Controls.Add(control);
    }
    private static void AddVector(Panel panel, string label, NumericUpDown[] inputs, int y, string axes = "XYZ")
    {
        var caption = Theme.Label(label, 8.8f, Theme.Text); caption.Location = new Point(0, y); panel.Controls.Add(caption);
        for (int i = 0; i < 3; i++)
        {
            var axis = Theme.Label(axes[i].ToString(), 8, Theme.Muted);
            axis.Location = new Point(135 + i * 130, y + 3); panel.Controls.Add(axis);
            inputs[i].Location = new Point(155 + i * 130, y); inputs[i].Width = 101; panel.Controls.Add(inputs[i]);
        }
    }

    private void FilterCommands()
    {
        var previous = (commands.SelectedItem as ZBrushCommandDefinition)?.Id;
        var query = search.Text.Trim();
        var category = categories.SelectedItem?.ToString() ?? "ทั้งหมด";
        commands.BeginUpdate(); commands.Items.Clear();
        foreach (var command in ZBrushCommandCatalog.All)
        {
            if (category != "ทั้งหมด" && command.Category != category) continue;
            if (query.Length > 0 && !new[] { command.Thai, command.English, command.Path, command.Category }
                    .Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase))) continue;
            commands.Items.Add(command);
        }
        commands.EndUpdate();
        var old = Enumerable.Range(0, commands.Items.Count).FirstOrDefault(i =>
            (commands.Items[i] as ZBrushCommandDefinition)?.Id == previous, -1);
        commands.SelectedIndex = old >= 0 ? old : commands.Items.Count > 0 ? 0 : -1;
    }

    private void ShowSelected()
    {
        selected = commands.SelectedItem as ZBrushCommandDefinition;
        if (selected == null)
        {
            selectedTitle.Text = "ไม่พบคำสั่ง · No command";
            selectedHelp.Text = "ลองเปลี่ยนคำค้นหา";
            return;
        }
        selectedTitle.Text = selected.Thai + "  ·  " + selected.English;
        selectedHelp.Text = selected.Help + (selected.Documented ? "" : "  ·  ตรวจ UI path ตอนรัน");
        uiPath.Text = selected.Path;
        uiPath.ReadOnly = selected.Path.Length > 0;
        pathRow.Visible = selected.Path.Length == 0 && selected.Mode is "press" or "set" or "toggle";
        valueRow.Visible = selected.Mode == "set";
        fileRow.Visible = selected.Mode is "import" or "export";
        indexRow.Visible = selected.Mode == "select_subtool";
        transformRow.Visible = selected.Mode == "transform";
        colorRow.Visible = selected.Mode == "color";
        value.Minimum = (decimal)selected.Minimum; value.Maximum = (decimal)selected.Maximum;
        value.Value = Math.Clamp((decimal)selected.DefaultValue, value.Minimum, value.Maximum);
        filePath.Text = "";
    }

    private ZBrushAction? CurrentAction()
    {
        if (selected == null) return null;
        var path = uiPath.Text.Trim();
        if (selected.Mode is "press" or "set" or "toggle" && path.Length == 0)
        {
            ShowInfo("ใส่ ZBrush UI path ก่อนใช้คำสั่ง"); return null;
        }
        if (selected.Mode is "import" or "export" && filePath.Text.Trim().Length == 0)
        {
            ShowInfo("เลือกไฟล์ OBJ ก่อนใช้คำสั่ง"); return null;
        }
        return new ZBrushAction(selected.Mode, selected.Mode is "import" or "export" ? selected.Path : path,
            (double)value.Value, selected.Mode is "import" or "export" ? Path.GetFullPath(filePath.Text.Trim()) : "", (int)subtoolIndex.Value,
            move.Select(input => (double)input.Value).ToArray(),
            rotate.Select(input => (double)input.Value).ToArray(),
            scale.Select(input => (double)input.Value).ToArray(),
            color.Select(input => (double)input.Value).ToArray());
    }

    private void AddToQueue()
    {
        if (Busy) return;
        var action = CurrentAction(); if (action == null) return;
        queue.Add(action); RefreshQueue();
        status.Text = "เพิ่มคำสั่งลงคิวแล้ว · Added to queue";
    }
    private void RemoveFromQueue()
    {
        if (Busy || queued.SelectedIndex < 0) return;
        queue.RemoveAt(queued.SelectedIndex); RefreshQueue();
    }
    private void RefreshQueue()
    {
        queued.Items.Clear();
        for (int i = 0; i < queue.Count; i++) queued.Items.Add($"{i + 1}. {queue[i].Summary}");
    }
    private async Task RunSelectedAsync()
    {
        var action = CurrentAction();
        if (action != null) await RunActionsAsync([action]);
    }
    private async Task RunQueueAsync()
    {
        if (queue.Count == 0) { ShowInfo("คิวยังว่าง · Add commands to the queue first"); return; }
        await RunActionsAsync(queue.ToArray());
    }
    private async Task RunActionsAsync(IReadOnlyList<ZBrushAction> actions)
    {
        if (Busy) return;
        if (zbrushPath == null || !File.Exists(zbrushPath))
        {
            SelectZBrush();
            if (zbrushPath == null || !File.Exists(zbrushPath)) return;
        }
        var dir = Path.Combine(dataDirectory, "Jobs", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        ZBrushJobFiles files;
        try { files = service.CreateJob(dir, actions); }
        catch (Exception ex) { ShowError(ex); return; }
        var cts = new CancellationTokenSource(); operation = cts;
        runButton.Enabled = false; stopButton.Enabled = true;
        status.Text = "กำลังรันใน ZBrush… · Running…";
        Log("สคริปต์: " + files.ScriptPath);
        try
        {
            var result = await service.RunAsync(zbrushPath!, files, projectPath.Text.Trim(), new Progress<string>(Log), cts.Token);
            status.Text = "ZBrush ทำงานสำเร็จ · Complete"; Log(result);
        }
        catch (OperationCanceledException) { status.Text = "หยุดแล้ว · Cancelled"; Log("ยกเลิกการทำงาน"); }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            operation = null; cts.Dispose();
            if (!IsDisposed) { runButton.Enabled = true; stopButton.Enabled = false; }
        }
    }

    private void BrowseMesh()
    {
        if (selected?.Mode == "import")
        {
            using var dialog = new OpenFileDialog { Title = "นำเข้า OBJ · Import OBJ", Filter = "OBJ (*.obj)|*.obj" };
            if (dialog.ShowDialog(this) == DialogResult.OK) filePath.Text = dialog.FileName;
        }
        else
        {
            using var dialog = new SaveFileDialog { Title = "ส่งออก OBJ · Export OBJ", Filter = "OBJ (*.obj)|*.obj", AddExtension = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) filePath.Text = dialog.FileName;
        }
    }
    private void BrowseProject()
    {
        using var dialog = new OpenFileDialog { Title = "เลือกโปรเจกต์ ZBrush", Filter = "ZBrush Project (*.zpr)|*.zpr" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        projectPath.Text = dialog.FileName; SaveSettings();
    }
    private void SelectZBrush()
    {
        if (Busy) return;
        using var dialog = new OpenFileDialog { Title = "เลือก ZBrush.exe", Filter = "ZBrush (ZBrush.exe)|ZBrush.exe", FileName = "ZBrush.exe" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        zbrushPath = dialog.FileName; SaveSettings(); UpdateEngine();
    }
    private void OpenZBrush()
    {
        if (zbrushPath == null || !File.Exists(zbrushPath)) { SelectZBrush(); if (zbrushPath == null) return; }
        try { Process.Start(new ProcessStartInfo(zbrushPath!) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveScript()
    {
        var actions = queue.Count > 0 ? queue.ToArray() : CurrentAction() is { } action ? [action] : [];
        if (actions.Length == 0) return;
        using var dialog = new SaveFileDialog { Title = "บันทึก Python สำหรับ ZBrush", Filter = "Python (*.py)|*.py", FileName = "miti_zbrush.py", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { service.ExportStandaloneScript(dialog.FileName, actions); status.Text = "บันทึกสคริปต์แล้ว · Script saved"; Log("สคริปต์: " + dialog.FileName); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadSettings()
    {
        try
        {
            var path = Path.Combine(dataDirectory, "settings.json");
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                zbrushPath = settings?.ZBrushPath;
                projectPath.Text = settings?.ProjectPath ?? "";
            }
        }
        catch (Exception ex) { Log("อ่านการตั้งค่าไม่ได้: " + ex.Message); }
        if (zbrushPath == null || !File.Exists(zbrushPath)) zbrushPath = ZBrushOnlyService.FindZBrush();
        UpdateEngine();
    }
    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(Path.Combine(dataDirectory, "settings.json"),
                JsonSerializer.Serialize(new Settings(zbrushPath, projectPath.Text.Trim())));
        }
        catch (Exception ex) { Log("บันทึกการตั้งค่าไม่ได้: " + ex.Message); }
    }
    private void UpdateEngine() => engine.Text = zbrushPath == null ? "ไม่พบ ZBrush.exe" : "ZBRUSH · พบโปรแกรม (ยังไม่ตรวจสิทธิ์)";
    private void Log(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Log(message)); return; }
        log.AppendText(message.TrimEnd() + Environment.NewLine);
        log.SelectionStart = log.TextLength; log.ScrollToCaret();
    }
    private void ShowInfo(string message) { status.Text = message; MessageBox.Show(this, message, "มิติ ZBrush ไทย", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    private void ShowError(Exception ex) { status.Text = "ZBrush ไม่สำเร็จ · ดูบันทึก"; Log("ข้อผิดพลาด: " + ex.Message); MessageBox.Show(this, ex.Message, "ZBrush ไม่สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private void DrawCommand(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var command = (ZBrushCommandDefinition)commands.Items[e.Index]!;
        var selectedItem = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(selectedItem ? Theme.Raised : Theme.Panel);
        e.Graphics.FillRectangle(brush, e.Bounds);
        using var titleFont = new Font("Leelawadee UI", 10, FontStyle.Bold);
        using var subtitleFont = new Font("Leelawadee UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, command.Thai, titleFont,
            new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 3, e.Bounds.Width - 20, 23), Theme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, command.English + "  ·  " + command.Category, subtitleFont,
            new Rectangle(e.Bounds.X + 12, e.Bounds.Y + 24, e.Bounds.Width - 20, 18), Theme.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
    private void DrawQueued(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Raised : Theme.Panel);
        e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, queued.Items[e.Index]?.ToString() ?? "", queued.Font,
            new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 14, e.Bounds.Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private static TextBox Input() => new() { BackColor = Theme.Raised, ForeColor = Theme.Text,
        BorderStyle = BorderStyle.FixedSingle, Font = new Font("Leelawadee UI", 10) };
    private static ComboBox Choice() => new StudioComboBox();
    private static NumericUpDown Number(decimal initial, decimal minimum, decimal maximum, int decimals) =>
        new() { Minimum = minimum, Maximum = maximum, Value = initial, DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1 : 0.1m, BackColor = Theme.Raised, ForeColor = Theme.Text };
    private sealed record Settings(string? ZBrushPath, string? ProjectPath);
}
