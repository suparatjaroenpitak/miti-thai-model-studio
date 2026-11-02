using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

public sealed class MainForm : Form
{
    private readonly CodeEditor editor = new() { Dock = DockStyle.Fill };
    private readonly ModelViewport viewport = new() { Dock = DockStyle.Fill };
    private readonly ListBox objects = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel, ForeColor = Theme.Text, IntegralHeight = false, ItemHeight = 30,
        DrawMode = DrawMode.OwnerDrawFixed };
    private readonly RichTextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true,
        BackColor = Theme.Background, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 9.5F), WordWrap = false, DetectUrls = false };
    private readonly Label status = Theme.Label("พร้อมสร้างโมเดล", 9, Theme.Muted);
    private readonly Label fileLabel = Theme.Label("โมเดลใหม่.py", 10);
    private readonly Label modelLabel = Theme.Label("ยังไม่มีโมเดล", 9, Theme.Muted);
    private readonly Label objectDetails = Theme.Label("เลือกวัตถุเพื่อดูรายละเอียด", 9, Theme.Muted);
    private readonly Label engineLabel = Theme.Label("", 9, Theme.Muted);
    private readonly Button run;
    private readonly Button stop;
    private readonly Button export;
    private readonly BlenderService blender = new();
    private readonly string dataDir = Path.Combine(AppContext.BaseDirectory, "UserData");
    private readonly List<ToolStripMenuItem> idleItems = [];
    private CancellationTokenSource? operation;
    private BuildResult? lastBuild;
    private string? lastBuiltSource;
    private string? currentPath;
    private string? blenderPath;
    private bool dirty;
    private bool loading;
    private bool closing;
    private readonly SplitContainer workspaceSplit;
    private readonly SplitContainer editorSplit;
    private readonly SplitContainer outputSplit;
    private bool Busy => operation != null;

    public MainForm()
    {
        Text = "มิติ สตูดิโอ — สร้างโมเดลด้วยโค้ด";
        BackColor = Theme.Background; ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen; Size = new Size(1560, 980);
        MinimumSize = new Size(1120, 760); KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ToolStripManager.Renderer = new DarkRenderer();
        run = Theme.Button("▶  สร้างโมเดล  F5", async (_, _) => await BuildModelAsync(), true);
        stop = Theme.Button("■  หยุด", (_, _) => operation?.Cancel()); stop.Enabled = false;
        export = Theme.Button("ส่งออกโมเดล ↗", async (_, _) => await ExportModelAsync());

        var menu = BuildMenu();
        var header = BuildHeader();
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Theme.Panel, Padding = new Padding(14, 4, 12, 0) };
        status.Dock = DockStyle.Fill; engineLabel.Dock = DockStyle.Right;
        footer.Controls.Add(status); footer.Controls.Add(engineLabel);

        workspaceSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border,
            Size = new Size(1400, 700), SplitterDistance = 205,
            SplitterWidth = 1, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 170, Panel2MinSize = 600 };
        workspaceSplit.Panel1.BackColor = Theme.Panel;
        workspaceSplit.Panel1.Controls.Add(BuildSidebar());

        outputSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            BackColor = Theme.Border, SplitterWidth = 4, FixedPanel = FixedPanel.Panel2 };
        editorSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border, SplitterWidth = 4 };
        editorSplit.Panel1.Controls.Add(BuildEditor());
        editorSplit.Panel2.Controls.Add(BuildViewport());
        outputSplit.Panel1.Controls.Add(editorSplit);
        outputSplit.Panel2.Controls.Add(BuildOutput());
        workspaceSplit.Panel2.Controls.Add(outputSplit);
        Controls.Add(workspaceSplit); Controls.Add(footer); Controls.Add(header); Controls.Add(menu);
        MainMenuStrip = menu;

        editor.CodeChanged += (_, _) =>
        {
            if (loading) return; dirty = true; UpdateTitle();
            status.Text = "โค้ดเปลี่ยนแล้ว — กด F5 เพื่อสร้างโมเดลใหม่";
        };
        viewport.ObjectSelected += (_, name) => objects.SelectedIndex = objects.Items.IndexOf(name);
        objects.SelectedIndexChanged += (_, _) =>
        {
            var i = objects.SelectedIndex;
            if (i < 0 || i >= viewport.Objects.Count) { viewport.SelectedObject = null; return; }
            var obj = viewport.Objects[i]; viewport.SelectedObject = obj.Name;
            objectDetails.Text = $"{obj.VertexCount:N0} จุดยอด\n{obj.TriangleCount:N0} สามเหลี่ยม";
        };
        objects.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var bg = new SolidBrush(selected ? Theme.Raised : Theme.Panel);
            e.Graphics.FillRectangle(bg, e.Bounds);
            TextRenderer.DrawText(e.Graphics, "◇  " + objects.Items[e.Index], Font,
                new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 15, e.Bounds.Height),
                selected ? Theme.Accent : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        KeyDown += OnShortcut;
        FormClosing += OnClosing;
        Shown += async (_, _) =>
        {
            workspaceSplit.SplitterDistance = 205;
            outputSplit.SplitterDistance = Math.Max(250, outputSplit.Height - 165);
            editorSplit.SplitterDistance = editorSplit.Width / 2;
            LoadSettings();
            var example = Path.Combine(AppContext.BaseDirectory, "examples", "01_house.py");
            SetCode(File.Exists(example) ? File.ReadAllText(example, Encoding.UTF8) : DefaultCode);
            if (blenderPath != null) await BuildModelAsync();
            else Log("ยังไม่พบ Blender — เลือกไฟล์ blender.exe ที่เมนู ตั้งค่า > ตำแหน่ง Blender");
        };
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Theme.Panel, ForeColor = Theme.Text,
            Padding = new Padding(8, 3, 0, 3), Font = new Font("Leelawadee UI", 10) };
        var file = new ToolStripMenuItem("ไฟล์");
        Add(file, "สร้างไฟล์ใหม่", (_, _) => NewFile(), Keys.Control | Keys.N);
        Add(file, "เปิดโค้ด…", (_, _) => OpenCode(), Keys.Control | Keys.O);
        Add(file, "บันทึกโค้ด", (_, _) => SaveCode(), Keys.Control | Keys.S);
        Add(file, "บันทึกโค้ดเป็น…", (_, _) => SaveCode(true), Keys.Control | Keys.Shift | Keys.S);
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "ส่งออกโมเดล…", async (_, _) => await ExportModelAsync(), Keys.Control | Keys.E);
        Add(file, "บันทึกภาพมุมมอง…", (_, _) => SaveScreenshot());
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "ออก", (_, _) => Close(), idleOnly: false);
        var model = new ToolStripMenuItem("โมเดล");
        Add(model, "คลังฟังก์ชัน Blender ภาษาไทย…", (_, _) => ShowApiCatalog(), Keys.Control | Keys.Space);
        Add(model, "สร้างจากโค้ด", async (_, _) => await BuildModelAsync(), Keys.F5);
        Add(model, "เปิดโมเดลใน Blender", (_, _) => OpenInBlender());
        var windowContext = new ToolStripMenuItem("ใช้บริบทหน้าต่าง Blender ขณะรัน") { CheckOnClick = true };
        windowContext.CheckedChanged += (_, _) =>
        {
            blender.UseWindowContext = windowContext.Checked;
            status.Text = windowContext.Checked ? "ใช้หน้าต่าง Blender แยกขณะรัน — รองรับคำสั่งที่ต้องใช้บริบทมุมมอง" : "ใช้ Blender แบบเบื้องหลัง";
        };
        model.DropDownItems.Add(windowContext); idleItems.Add(windowContext);
        var view = new ToolStripMenuItem("มุมมอง");
        Add(view, "จัดโมเดลให้พอดี", (_, _) => viewport.FitScene());
        Add(view, "มองเฉียง", (_, _) => viewport.SetView("isometric"));
        Add(view, "มองด้านหน้า", (_, _) => viewport.SetView("front"));
        Add(view, "มองด้านบน", (_, _) => viewport.SetView("top"));
        var grid = new ToolStripMenuItem("แสดงเส้นตาราง") { Checked = true, CheckOnClick = true };
        grid.CheckedChanged += (_, _) => { viewport.ShowGrid = grid.Checked; viewport.Invalidate(); };
        var wire = new ToolStripMenuItem("แสดงโครงเส้น") { CheckOnClick = true };
        wire.CheckedChanged += (_, _) => { viewport.Wireframe = wire.Checked; viewport.Invalidate(); };
        view.DropDownItems.AddRange([new ToolStripSeparator(), grid, wire]);
        var settings = new ToolStripMenuItem("ตั้งค่า");
        Add(settings, "ตำแหน่ง Blender…", (_, _) => SelectBlender());
        var help = new ToolStripMenuItem("คู่มือ");
        Add(help, "คำสั่งและวิธีใช้", (_, _) => ShowHelp(), Keys.F1, false);
        Add(help, "เกี่ยวกับ มิติ สตูดิโอ", (_, _) => MessageBox.Show(this,
            "มิติ สตูดิโอ 0.2\nแอป Windows Forms สำหรับสร้างโมเดลด้วย Python\n\nคลัง API ของ Blender พร้อมชื่อและพารามิเตอร์ภาษาไทย\nCtrl+Space ค้นหาและแทรกโค้ด\n\nใช้ Blender จริงเป็นเอนจิน\nมุมมองใน WinForms แสดงเมชและสีพื้น\nฟังก์ชันบางชนิดต้องเตรียมวัตถุ โหมด หรือหน้าต่างให้เหมาะสม",
            "เกี่ยวกับ", MessageBoxButtons.OK, MessageBoxIcon.Information), idleOnly: false);
        menu.Items.AddRange([file, model, view, settings, help]);
        return menu;
    }

    private void Add(ToolStripMenuItem parent, string text, EventHandler handler, Keys shortcut = Keys.None, bool idleOnly = true)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += handler; parent.DropDownItems.Add(item); if (idleOnly) idleItems.Add(item);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 83, BackColor = Theme.Panel, Padding = new Padding(18, 9, 15, 10) };
        var brand = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 370, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(Theme.Label("◇  มิติ สตูดิโอ", 20, Theme.Text, true));
        brand.Controls.Add(Theme.Label("เปลี่ยนไอเดียเป็นโมเดล ด้วยโค้ดของคุณ", 9, Theme.Muted));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 13, 0, 0) };
        actions.Controls.Add(Theme.Button("เปิดโค้ด", (_, _) => OpenCode()));
        actions.Controls.Add(Theme.Button("บันทึก", (_, _) => SaveCode()));
        actions.Controls.Add(run); actions.Controls.Add(stop); actions.Controls.Add(export);
        header.Controls.Add(actions); header.Controls.Add(brand); return header;
    }

    private Control BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 14, 12, 12) };
        var title = Theme.Label("วัตถุในฉาก", 11, Theme.Text, true); title.Dock = DockStyle.Top; title.Height = 32; title.AutoSize = false;
        var details = new Panel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(6, 8, 0, 0) };
        objectDetails.Dock = DockStyle.Fill; details.Controls.Add(objectDetails);
        var examples = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 278, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 14, 0, 0) };
        var apiButton = Theme.Button("คลัง Blender ภาษาไทย", (_, _) => ShowApiCatalog(), true);
        apiButton.Width = 173; apiButton.Margin = new Padding(0, 0, 0, 12); examples.Controls.Add(apiButton);
        examples.Controls.Add(Theme.Label("เริ่มจากตัวอย่าง", 10, Theme.Muted, true));
        foreach (var entry in new[] { ("01_house.py", "⌂  บ้านหลังเล็ก"), ("02_robot.py", "▣  หุ่นยนต์"), ("03_city.py", "▥  เมืองจำลอง"), ("04_bpy.py", "◇  Blender Python") })
        {
            var b = Theme.Button(entry.Item2, (_, _) => LoadExample(entry.Item1));
            b.Width = 173; b.TextAlign = ContentAlignment.MiddleLeft; b.Margin = new Padding(0, 3, 0, 3);
            examples.Controls.Add(b);
        }
        panel.Controls.Add(objects); panel.Controls.Add(title); panel.Controls.Add(details); panel.Controls.Add(examples);
        return panel;
    }

    private Control BuildEditor()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel, Padding = new Padding(13, 11, 10, 0) };
        fileLabel.Dock = DockStyle.Left;
        var language = Theme.Label("PYTHON  /  UTF-8", 8.5F, Theme.Muted); language.Dock = DockStyle.Right;
        top.Controls.Add(fileLabel); top.Controls.Add(language);
        var hint = Theme.Label("Ctrl+Space  ค้นหาฟังก์ชัน Blender ภาษาไทยและแทรกโค้ด", 9, Theme.Accent);
        hint.AutoSize = false; hint.Dock = DockStyle.Bottom; hint.Height = 32; hint.Padding = new Padding(12, 6, 0, 0);
        p.Controls.Add(editor); p.Controls.Add(hint); p.Controls.Add(top); return p;
    }

    private void ShowApiCatalog()
    {
        if (Busy) return;
        using var dialog = new ApiCatalogForm(() => blenderPath);
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedCode != null)
            editor.InsertSnippet(dialog.SelectedCode);
    }

    private Control BuildViewport()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel, Padding = new Padding(12, 8, 5, 0) };
        var title = Theme.Label("มุมมอง 3 มิติ", 10, Theme.Text, true); title.Dock = DockStyle.Left;
        var views = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        foreach (var pair in new[] { ("พอดี", "fit"), ("เฉียง", "isometric"), ("หน้า", "front"), ("บน", "top") })
        {
            var b = Theme.Button(pair.Item1, (_, _) => { if (pair.Item2 == "fit") viewport.FitScene(); else viewport.SetView(pair.Item2); });
            b.MinimumSize = new Size(45, 27); b.Height = 27; b.Font = new Font("Leelawadee UI", 8.5F); b.Padding = new Padding(5, 0, 5, 0);
            views.Controls.Add(b);
        }
        top.Controls.Add(views); top.Controls.Add(title);
        var info = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Theme.Panel, Padding = new Padding(12, 6, 0, 0) };
        modelLabel.Dock = DockStyle.Fill; info.Controls.Add(modelLabel);
        p.Controls.Add(viewport); p.Controls.Add(info); p.Controls.Add(top); return p;
    }

    private Control BuildOutput()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(12, 0, 10, 6) };
        var top = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Theme.Background };
        var label = Theme.Label("ผลการทำงาน", 9, Theme.Muted, true); label.Dock = DockStyle.Left; label.Padding = new Padding(0, 6, 0, 0);
        var clear = Theme.Button("ล้างข้อความ", (_, _) => output.Clear()); clear.Dock = DockStyle.Right;
        clear.Height = 26; clear.MinimumSize = new Size(65, 26); clear.Font = new Font("Leelawadee UI", 8);
        top.Controls.Add(label); top.Controls.Add(clear); p.Controls.Add(output); p.Controls.Add(top); return p;
    }

    private void SetCode(string code, string? path = null)
    {
        loading = true; editor.Code = code; loading = false;
        currentPath = path; dirty = false; UpdateTitle();
    }
    private void UpdateTitle()
    {
        var name = currentPath == null ? "โมเดลใหม่.py" : Path.GetFileName(currentPath);
        fileLabel.Text = name + (dirty ? "  •" : "");
        Text = $"{(dirty ? "• " : "")}{name} — มิติ สตูดิโอ";
    }
    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "บันทึกโค้ดที่แก้ไขก่อนหรือไม่?", "โค้ดยังไม่ได้บันทึก", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && SaveCode();
    }
    private void NewFile()
    { if (!Busy && ConfirmDiscard()) { SetCode(DefaultCode); editor.FocusCode(); } }
    private void OpenCode()
    {
        if (Busy || !ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog { Title = "เปิดโค้ด Python", Filter = "โค้ด Python (*.py)|*.py|ไฟล์ทั้งหมด (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SetCode(File.ReadAllText(dialog.FileName, Encoding.UTF8), dialog.FileName); }
        catch (Exception ex) { ShowError("เปิดไฟล์ไม่สำเร็จ", ex); }
    }
    private bool SaveCode(bool saveAs = false)
    {
        var path = currentPath;
        if (saveAs || path == null)
        {
            using var dialog = new SaveFileDialog { Title = "บันทึกโค้ด Python", Filter = "โค้ด Python (*.py)|*.py", FileName = path == null ? "โมเดลของฉัน.py" : Path.GetFileName(path), DefaultExt = "py" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try { File.WriteAllText(path, editor.Code, new UTF8Encoding(false)); currentPath = path; dirty = false; UpdateTitle(); status.Text = "บันทึกโค้ดแล้ว"; return true; }
        catch (Exception ex) { ShowError("บันทึกไฟล์ไม่สำเร็จ", ex); return false; }
    }
    private async void LoadExample(string name)
    {
        if (Busy || !ConfirmDiscard()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "examples", name);
        try { SetCode(File.ReadAllText(path, Encoding.UTF8)); await BuildModelAsync(); }
        catch (Exception ex) { ShowError("เปิดตัวอย่างไม่สำเร็จ", ex); }
    }

    private async Task<bool> BuildModelAsync()
    {
        if (Busy) return false;
        if (!EnsureBlender()) return false;
        var source = editor.Code;
        var cts = BeginOperation("กำลังสร้างโมเดล…"); output.Clear();
        var watch = Stopwatch.StartNew();
        try
        {
            Log("เริ่มสร้างโมเดลด้วย Blender…");
            var jobDir = Path.Combine(dataDir, "Builds", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var result = await blender.BuildAsync(blenderPath!, source, jobDir, new Progress<string>(Log), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            viewport.LoadScene(result.PreviewPath);
            lastBuild = result; lastBuiltSource = source;
            objects.Items.Clear(); foreach (var item in viewport.Objects) objects.Items.Add(item.Name);
            objectDetails.Text = "เลือกวัตถุเพื่อดูรายละเอียด";
            modelLabel.Text = $"{viewport.Objects.Count:N0} วัตถุ  ·  {viewport.VertexCount:N0} จุดยอด  ·  {viewport.TriangleCount:N0} สามเหลี่ยม";
            using var json = JsonDocument.Parse(File.ReadAllText(result.PreviewPath));
            if (json.RootElement.TryGetProperty("simplified", out var simplified) && simplified.ValueKind == JsonValueKind.True)
            { modelLabel.Text += "  ·  ย่อรายละเอียดเฉพาะภาพตัวอย่าง"; Log("ภาพตัวอย่างลดจำนวนสามเหลี่ยมเพื่อความลื่นไหล ไฟล์โมเดลยังมีรายละเอียดครบ"); }
            status.Text = $"สร้างสำเร็จ  ·  {watch.Elapsed.TotalSeconds:N1} วินาที  ·  พร้อมส่งออก";
            Log($"สำเร็จ • {viewport.Objects.Count} วัตถุ • {watch.Elapsed.TotalSeconds:N1} วินาที");
            return true;
        }
        catch (OperationCanceledException) { status.Text = "หยุดสร้างโมเดลแล้ว"; Log("ผู้ใช้หยุดการทำงาน"); return false; }
        catch (Exception ex)
        {
            status.Text = "สร้างไม่สำเร็จ — ดูข้อผิดพลาดด้านล่าง";
            Log("ข้อผิดพลาด: " + ex.Message);
            if (lastBuild != null) Log("ยังแสดงโมเดลจากการสร้างครั้งก่อน");
            return false;
        }
        finally { EndOperation(cts); }
    }

    private async Task ExportModelAsync()
    {
        if (Busy) return;
        if ((lastBuild == null || lastBuiltSource != editor.Code) && !await BuildModelAsync()) return;
        await Task.Delay(5000);
        using var dialog = new SaveFileDialog { Title = "ส่งออกโมเดล", FileName = "โมเดลของฉัน",
            Filter = "โมเดล GLB — Godot / เว็บ (*.glb)|*.glb|โปรเจกต์ Blender (*.blend)|*.blend|โมเดล OBJ (*.obj)|*.obj|งานพิมพ์ 3 มิติ STL (*.stl)|*.stl", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var format = new[] { "glb", "blend", "obj", "stl" }[dialog.FilterIndex - 1];
        var cts = BeginOperation("กำลังส่งออกโมเดล…");
        try
        {
            await blender.ExportAsync(blenderPath!, lastBuild!.ScenePath, dialog.FileName, format, new Progress<string>(Log), cts.Token);
            status.Text = "ส่งออกสำเร็จ: " + Path.GetFileName(dialog.FileName); Log("บันทึกโมเดล: " + dialog.FileName);
            MessageBox.Show(this, "ส่งออกโมเดลสำเร็จ\n" + dialog.FileName, "บันทึกแล้ว", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { status.Text = "หยุดส่งออกแล้ว"; }
        catch (Exception ex) { ShowError("ส่งออกไม่สำเร็จ", ex); }
        finally { EndOperation(cts); }
    }

    private CancellationTokenSource BeginOperation(string message)
    {
        operation = new CancellationTokenSource(); status.Text = message;
        run.Enabled = false; export.Enabled = false; stop.Enabled = true; editor.IsReadOnly = true;
        foreach (var item in idleItems) item.Enabled = false;
        UseWaitCursor = false;
        return operation;
    }
    private void EndOperation(CancellationTokenSource cts)
    {
        if (operation == cts) operation = null;
        cts.Dispose(); if (closing || IsDisposed) return;
        run.Enabled = true; export.Enabled = true; stop.Enabled = false; editor.IsReadOnly = false;
        foreach (var item in idleItems) item.Enabled = true;
    }
    private void Log(string text)
    {
        if (IsDisposed || closing) return;
        if (InvokeRequired) { BeginInvoke(() => Log(text)); return; }
        if (output.TextLength > 100000) { output.Select(0, output.TextLength - 80000); output.SelectedText = ""; }
        output.AppendText(text.TrimEnd() + Environment.NewLine); output.SelectionStart = output.TextLength; output.ScrollToCaret();
    }
    private void ShowError(string title, Exception ex)
    { Log(title + ": " + ex.Message); status.Text = title; MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }

    private void LoadSettings()
    {
        try
        {
            var path = Path.Combine(dataDir, "settings.json");
            if (File.Exists(path)) blenderPath = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))?.BlenderPath;
        }
        catch (Exception ex) { Log("อ่านการตั้งค่าไม่ได้: " + ex.Message); }
        if (blenderPath == null || !File.Exists(blenderPath)) blenderPath = BlenderService.FindBlender();
        UpdateEngineLabel();
    }
    private void UpdateEngineLabel()
    {
        engineLabel.Text = blenderPath == null ? "ยังไม่พบ Blender" : "BLENDER  ·  พร้อมใช้งาน";
    }
    private bool EnsureBlender()
    {
        if (blenderPath != null && File.Exists(blenderPath)) return true;
        MessageBox.Show(this, "เลือกไฟล์ blender.exe เพื่อใช้สร้างโมเดล", "เชื่อมต่อ Blender", MessageBoxButtons.OK, MessageBoxIcon.Information);
        SelectBlender(); return blenderPath != null && File.Exists(blenderPath);
    }
    private void SelectBlender()
    {
        if (Busy) return;
        using var dialog = new OpenFileDialog { Title = "เลือก blender.exe", Filter = "Blender (blender.exe)|blender.exe", FileName = "blender.exe" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        blenderPath = dialog.FileName;
        try { Directory.CreateDirectory(dataDir); File.WriteAllText(Path.Combine(dataDir, "settings.json"), JsonSerializer.Serialize(new Settings(blenderPath))); }
        catch (Exception ex) { Log("จำตำแหน่ง Blender ไม่สำเร็จ: " + ex.Message); }
        UpdateEngineLabel(); Log("ใช้ Blender: " + blenderPath);
    }
    private void OpenInBlender()
    {
        if (lastBuild == null) { status.Text = "สร้างโมเดลก่อนเปิดใน Blender"; return; }
        if (!EnsureBlender()) return;
        try
        {
            var start = new ProcessStartInfo(blenderPath!) { UseShellExecute = false };
            start.ArgumentList.Add(lastBuild.ScenePath); start.ArgumentList.Add("--python");
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "engine", "thai_interface.py"));
            start.Environment["MITI_API_CATALOG"] = ApiCatalogService.ResolvePath();
            Process.Start(start);
        }
        catch (Exception ex) { ShowError("เปิด Blender ไม่สำเร็จ", ex); }
    }
    private void SaveScreenshot()
    {
        if (lastBuild == null) { status.Text = "สร้างโมเดลก่อนบันทึกภาพ"; return; }
        using var dialog = new SaveFileDialog { Title = "บันทึกภาพมุมมอง", Filter = "ภาพ PNG (*.png)|*.png", FileName = "โมเดลของฉัน.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { viewport.SaveScreenshot(dialog.FileName); status.Text = "บันทึกภาพแล้ว"; }
        catch (Exception ex) { ShowError("บันทึกภาพไม่สำเร็จ", ex); }
    }
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && Busy) { operation?.Cancel(); e.SuppressKeyPress = true; }
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        closing = true; operation?.Cancel();
    }
    private void ShowHelp()
    {
        using var dialog = new Form { Text = "คู่มือ — มิติ สตูดิโอ", Size = new Size(820, 740), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Panel, ForeColor = Theme.Text, MinimizeBox = false, MaximizeBox = false };
        var text = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Panel,
            ForeColor = Theme.Text, Font = new Font("Leelawadee UI", 11), BorderStyle = BorderStyle.None,
            DetectUrls = true, Padding = new Padding(20), Text = HelpText };
        dialog.Padding = new Padding(22); dialog.Controls.Add(text); dialog.ShowDialog(this);
    }
    private sealed record Settings(string? BlenderPath);

    private const string DefaultCode = "# เริ่มสร้างโมเดลของคุณ\nล้างฉาก()\nสีฟ้า = วัสดุ(\"สีฟ้า\", \"#62B6ED\")\nกล่อง(\"กล่องแรก\", size=(2, 2, 2), location=(0, 0, 1), material=สีฟ้า)\n";
    private const string HelpText = """
มิติ สตูดิโอ — จากโค้ดสู่โมเดล 3 มิติ

คลัง Blender ภาษาไทย — รุ่น 0.2
Ctrl+Space หรือปุ่ม คลัง Blender ภาษาไทย
ค้นหาคำสั่ง เมธอด คุณสมบัติ ชนิดข้อมูล และฟังก์ชันโมดูล
เลือกพารามิเตอร์แล้วแทรกโค้ด ชื่อไทยเชื่อมไปยัง Blender จริง
ตัวอย่าง: คำสั่ง("ตาข่าย.เพิ่มลูกบาศก์", ขนาด=2, ตำแหน่ง=(0,0,1))
คำสั่งที่ต้องมีหน้าต่าง: โมเดล > ใช้บริบทหน้าต่าง Blender ขณะรัน
ชื่อจากอภิธานศัพท์อาจต้องปรับสำนวน คำอธิบายสรุปมีต้นฉบับให้เทียบ

1. เลือกตัวอย่างด้านซ้าย หรือเขียน Python ของคุณ
2. กด F5 / สร้างโมเดล แล้วรอ Blender ทำงาน
3. ลากเมาส์ซ้ายเพื่อหมุน • ลากขวา/กลางเพื่อเลื่อน • หมุนล้อเพื่อซูม
4. กดส่งออกโมเดล เลือก GLB, Blender, OBJ หรือ STL

คำสั่งเริ่มต้น
ล้างฉาก()
สีฟ้า = วัสดุ("สีฟ้า", "#62B6ED")
กล่อง("กล่อง", size=(2, 2, 2), location=(0, 0, 1), material=สีฟ้า)

รูปทรงและการจัดวาง
กล่อง / cube — กล่องตามขนาด XYZ
ทรงกลม / sphere — ทรงกลม
ทรงกระบอก / cylinder — ทรงกระบอก
กรวย / cone — กรวย
วงแหวน / torus — วงแหวน
พื้น / plane — พื้นระนาบ
วัสดุ / material — สร้างสีและวัสดุ
ย้าย / move • หมุน / rotate • ปรับขนาด / scale
ทำซ้ำ / duplicate • ขอบมน / bevel • ผิวเรียบ / smooth

ชื่อฟังก์ชันใช้ได้ทั้งไทยและอังกฤษ คีย์เวิร์ดพารามิเตอร์ใช้ภาษาอังกฤษ
ดูพารามิเตอร์และตัวอย่างครบในไฟล์ engine/thai_model.py และโฟลเดอร์ examples
แกน X/Y อยู่บนพื้น แกน Z คือความสูง หน่วยเริ่มต้นเป็นเมตร

ใช้ Blender Python ได้โดยตรง
import bpy
bpy.ops.mesh.primitive_monkey_add(location=(0, 0, 1))
obj = bpy.context.object
ขอบมน(obj, width=0.1, segments=3)

คำสั่ง bpy และตัวปรับแต่งทำงานผ่าน Blender ที่ติดตั้งในเครื่อง
รองรับโมเดลแบบคงที่ ภาพตัวอย่างแสดงเมชและสีพื้น
วัสดุแบบโหนด แสง กล้อง และแอนิเมชัน ตรวจต่อได้ใน Blender
หากโมเดลใหญ่ ภาพตัวอย่างอาจลดรายละเอียด แต่ไฟล์ส่งออกใช้โมเดลจริง

คีย์ลัด
F5 สร้างโมเดล • Escape หยุด • F1 คู่มือ
Ctrl+N ใหม่ • Ctrl+O เปิด • Ctrl+S บันทึก • Ctrl+E ส่งออก
Tab เว้นวรรค 4 ช่อง • Enter ย่อหน้าตาม Python

โค้ด Python มีสิทธิ์ทำงานตามผู้ใช้เครื่องนี้ ใช้โค้ดจากแหล่งที่คุณไว้วางใจ
แอปใช้ Blender ที่ติดตั้งแยกไว้ ไม่ได้รวม Blender มาในโปรแกรม
""";
}
