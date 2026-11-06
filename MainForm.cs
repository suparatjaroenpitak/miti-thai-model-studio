using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

public sealed class MainForm : Form
{
    private readonly CodeEditor editor = new() { Dock = DockStyle.Fill };
    private readonly ModelViewport viewport = new() { Dock = DockStyle.Fill };
    private readonly ShapeTransformPanel shapeTools = new() { Dock = DockStyle.Fill };
    private readonly SculptBrushPanel sculptTools = new() { Dock = DockStyle.Fill };
    private readonly TopologyPanel topologyTools = new() { Dock = DockStyle.Fill };
    private readonly MaskPanel maskTools = new() { Dock = DockStyle.Fill };
    private readonly MaterialPanel materialTools = new() { Dock = DockStyle.Fill };
    private readonly ObjectActionPanel objectActions = new() { Dock = DockStyle.Fill };
    private readonly ToolPalette modelToolTabs = new() { Dock = DockStyle.Fill };
    private readonly ContextMenuStrip objectMenu = new();
    private readonly ToolStripTextBox objectMenuSearch = new() { Width = 230 };
    private readonly ListBox objects = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel, ForeColor = Theme.Text, IntegralHeight = false, ItemHeight = 30,
        DrawMode = DrawMode.OwnerDrawFixed };
    private readonly RichTextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true,
        BackColor = Theme.Background, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 9.5F), WordWrap = false, DetectUrls = false };
    private readonly Label status = Theme.Label("พร้อมสร้างโมเดล · Ready", 9, Theme.Muted);
    private readonly Label fileLabel = Theme.Label("ฉากใหม่ · Untitled", 10);
    private readonly Label modelLabel = Theme.Label("ยังไม่มีโมเดล · No model yet", 9, Theme.Muted);
    private readonly Label objectDetails = Theme.Label("เลือกวัตถุเพื่อดูรายละเอียด\nSelect an object", 9, Theme.Muted);
    private readonly Label engineLabel = Theme.Label("", 9, Theme.Muted);
    private readonly Button run;
    private readonly Button stop;
    private readonly Button export;
    private readonly BlenderService blender = new();
    private readonly Stack<(string After, string Before)> sculptHistory = new();
    private readonly string dataDir = Path.Combine(AppContext.BaseDirectory, "UserData");
    private readonly List<ToolStripMenuItem> idleItems = [];
    private readonly SplitContainer workspaceSplit;
    private readonly SplitContainer scriptSplit;
    private readonly SplitContainer studioSplit;
    private readonly SplitContainer outputSplit;
    private ToolStripMenuItem scriptModeItem = null!;
    private CancellationTokenSource? operation;
    private CancellationTokenSource? previewOperation;
    private int previewVersion;
    private bool livePreviewShowing;
    private bool scriptMode;
    private BuildResult? lastBuild;
    private string? lastBuiltSource;
    private string? currentPath;
    private string? blenderPath;
    private bool dirty;
    private bool loading;
    private bool closing;
    private bool Busy => operation != null;

    public MainForm()
    {
        Text = "มิติ สตูดิโอ — สตูดิโอโมเดล 3 มิติ (Blender + ZBrush-style)";
        BackColor = Theme.Background; ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen; Size = new Size(1600, 1000);
        MinimumSize = new Size(1180, 780); KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ToolStripManager.Renderer = new DarkRenderer();
        objectMenu.BackColor = Theme.Panel; objectMenu.ForeColor = Theme.Text;
        objectMenu.Font = new Font("Leelawadee UI", 9);
        objectMenu.Items.Add(objectMenuSearch);
        objectMenuSearch.TextChanged += (_, _) => RefreshObjectMenu();
        FormClosed += (_, _) => objectMenu.Dispose();

        run = Theme.Button("▶  Build · สร้าง  F5", async (_, _) => await BuildModelAsync(), true);
        stop = Theme.Button("■  Stop · หยุด", (_, _) => operation?.Cancel()); stop.Enabled = false;
        export = Theme.Button("Export · ส่งออก ↗", async (_, _) => await ExportModelAsync());

        var menu = BuildMenu();
        var header = BuildHeader();
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 35, BackColor = Theme.Panel, Padding = new Padding(16, 6, 16, 0) };
        status.Dock = DockStyle.Fill; engineLabel.Dock = DockStyle.Right;
        footer.Controls.Add(status); footer.Controls.Add(engineLabel);

        workspaceSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border,
            Size = new Size(1500, 760), SplitterDistance = 238, SplitterWidth = 2,
            FixedPanel = FixedPanel.Panel1, Panel1MinSize = 220, Panel2MinSize = 720 };
        workspaceSplit.Panel1.BackColor = Theme.Panel;
        workspaceSplit.Panel1.Controls.Add(BuildSidebar());

        outputSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            BackColor = Theme.Border, SplitterWidth = 4, FixedPanel = FixedPanel.Panel2, Panel2MinSize = 96 };
        scriptSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border, Size = new Size(1200, 700),
            SplitterWidth = 4, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 320, Panel2MinSize = 620 };
        studioSplit = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border, Size = new Size(1100, 700),
            SplitterWidth = 3, FixedPanel = FixedPanel.Panel2, Panel1MinSize = 420, Panel2MinSize = 320 };
        scriptSplit.Panel1.Controls.Add(BuildEditor());
        studioSplit.Panel1.Controls.Add(BuildViewport());
        studioSplit.Panel2.Controls.Add(BuildToolPalette());
        scriptSplit.Panel2.Controls.Add(studioSplit);
        scriptSplit.Panel1Collapsed = true;
        outputSplit.Panel1.Controls.Add(scriptSplit);
        outputSplit.Panel2.Controls.Add(BuildOutput());
        workspaceSplit.Panel2.Controls.Add(outputSplit);
        Controls.Add(workspaceSplit); Controls.Add(footer); Controls.Add(header); Controls.Add(menu);
        MainMenuStrip = menu;
        Theme.UseDarkWindow(this);
        Theme.UseDarkControls(objects, output);

        editor.CodeChanged += (_, _) =>
        {
            if (loading) return; dirty = true; UpdateTitle();
            status.Text = "โค้ดเปลี่ยนแล้ว — กด F5 เพื่อสร้างโมเดลใหม่ · Code changed, press F5";
        };
        viewport.ObjectSelected += (_, name) => objects.SelectedIndex = objects.Items.IndexOf(name);
        viewport.ObjectContextRequested += (_, name) => OpenObjectActions(name);
        sculptTools.SettingsChanged += (_, _) =>
        {
            viewport.SculptEnabled = sculptTools.Active;
            viewport.SculptRadius = sculptTools.Radius;
            viewport.SculptFocalShift = sculptTools.FocalShift;
            viewport.SculptBrushLabel = sculptTools.BrushLabel;
            status.Text = sculptTools.Active
                ? "โหมดแปรงปั้นพร้อมใช้งาน — ลากเมาส์ซ้ายบนผิวโมเดล · Sculpt brush ready"
                : "ปิดโหมดแปรงปั้นแล้ว · Sculpt brush off";
        };
        maskTools.SettingsChanged += (_, _) =>
        {
            sculptTools.UseMask = maskTools.UseMask;
            status.Text = maskTools.UseMask
                ? "แปรงจะเคารพมาสก์ MitiMask · Brushes respect the MitiMask"
                : "ปิดการใช้มาสก์ขณะปั้น · Sculpting ignores the mask";
        };
        sculptTools.UndoRequested += async (_, _) => await UndoSculptAsync();
        viewport.SculptStrokeCompleted += async (_, stroke) =>
        {
            if (Busy) return;
            try
            {
                var previous = editor.Code;
                editor.AppendSnippet(sculptTools.GenerateSnippet(stroke), focus: false);
                sculptHistory.Push((editor.Code, previous));
                status.Text = "กำลังบันทึกรอยแปรงด้วย Blender…";
                await BuildModelAsync();
                if (!IsDisposed) viewport.Focus();
            }
            catch (Exception ex) { ShowError("ปั้นโมเดลไม่สำเร็จ", ex); }
        };
        objectActions.ApplyRequested += async (_, code) => await ApplySnippetAsync(code, "กำลังใช้คำสั่งกับวัตถุ…");
        objectActions.PreviewInvalidated += (_, _) => CancelLivePreview();
        objectActions.PreviewRequested += async (_, code) => await ShowLivePreviewAsync(code);
        shapeTools.PreviewInvalidated += (_, _) => CancelLivePreview();
        shapeTools.PreviewRequested += async (_, code) => await ShowLivePreviewAsync(code);
        shapeTools.ApplyRequested += async (_, action) =>
        {
            if (!await ApplySnippetAsync(action.Code, "กำลังสร้างรูปทรง…") || action.CreatedName == null) return;
            SelectObject(action.CreatedName);
        };
        topologyTools.ApplyRequested += async (_, code) => await ApplySnippetAsync(code, "กำลังปรับโครงสร้างโมเดล…");
        maskTools.ApplyRequested += async (_, code) => await ApplySnippetAsync(code, "กำลังปรับมาสก์…");
        materialTools.ApplyRequested += async (_, code) => await ApplySnippetAsync(code, "กำลังใช้วัสดุ…");
        objectActions.ActionSelected += (_, id) =>
        {
            viewport.ShowTopology = id == "bpy.ops.mesh.subdivide";
            if (id != "bpy.ops.mesh.subdivide") viewport.PreviewCuts = 0;
        };
        objectActions.QuickSubdivisionChanged += (_, cuts) => viewport.PreviewCuts = cuts;
        objects.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = objects.IndexFromPoint(e.Location);
            if (index < 0 || index >= objects.Items.Count) return;
            objects.SelectedIndex = index;
            OpenObjectActions(objects.Items[index]?.ToString() ?? "");
        };
        objects.SelectedIndexChanged += (_, _) =>
        {
            var i = objects.SelectedIndex;
            if (i < 0 || i >= viewport.Objects.Count)
            {
                viewport.SelectedObject = null;
                SelectPanels(null);
                return;
            }
            var obj = viewport.Objects[i]; viewport.SelectedObject = obj.Name;
            SelectPanels(obj.Name);
            objectDetails.Text = $"{obj.VertexCount:N0} จุดยอด · vertices\n{obj.TriangleCount:N0} สามเหลี่ยม · triangles";
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
            workspaceSplit.SplitterDistance = 238;
            outputSplit.SplitterDistance = Math.Max(420, outputSplit.Height - 132);
            scriptSplit.Panel1Collapsed = true;
            studioSplit.SplitterDistance = Math.Max(320, studioSplit.Width - 350);
            LoadSettings();
            SetCode(DefaultCode, null);
            dirty = false; UpdateTitle();
            if (blenderPath != null) await BuildModelAsync();
            else Log("ยังไม่พบ Blender — เลือก blender.exe ที่เมนู ตั้งค่า · Blender not found, pick it in Settings");
        };
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Top, BackColor = Theme.Panel, ForeColor = Theme.Text,
            Padding = new Padding(16, 6, 0, 6), Font = new Font("Leelawadee UI", 10) };
        var file = new ToolStripMenuItem("File · ไฟล์");
        Add(file, "New Scene · ฉากใหม่", (_, _) => NewScene(), Keys.Control | Keys.N);
        Add(file, "Open Project · เปิดโปรเจกต์…", (_, _) => OpenProject(), Keys.Control | Keys.O);
        Add(file, "Save Project · บันทึกโปรเจกต์", (_, _) => SaveProject(), Keys.Control | Keys.S);
        Add(file, "Save Project As · บันทึกเป็น…", (_, _) => SaveProject(true), Keys.Control | Keys.Shift | Keys.S);
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "Export Model · ส่งออกโมเดล…", async (_, _) => await ExportModelAsync(), Keys.Control | Keys.E);
        Add(file, "Save View · บันทึกภาพมุมมอง…", (_, _) => SaveScreenshot());
        file.DropDownItems.Add(new ToolStripSeparator());
        Add(file, "Exit · ออก", (_, _) => Close(), idleOnly: false);

        var model = new ToolStripMenuItem("Model · โมเดล");
        Add(model, "Add Shape · เพิ่มรูปทรง…", (_, _) => ShowTool(0, () => shapeTools.ShowCreateTab()));
        Add(model, "Transform · ย้าย หมุน สเกล…", (_, _) => ShowTool(0, () => shapeTools.ShowTransformTab()));
        Add(model, "Topology Tools · โครงสร้าง…", (_, _) => ShowTool(3));
        Add(model, "Mask Tools · มาสก์…", (_, _) => ShowTool(4));
        Add(model, "Material · วัสดุ…", (_, _) => ShowTool(5));
        Add(model, "Function Library · คลังฟังก์ชัน Blender + ZBrush…", (_, _) => ShowFunctionLibrary(), Keys.Control | Keys.Shift | Keys.F);
        Add(model, "Build · สร้างจากเครื่องมือ", async (_, _) => await BuildModelAsync(), Keys.F5);
        Add(model, "Open in Blender · เปิดใน Blender", (_, _) => OpenInBlender());

        var sculpt = new ToolStripMenuItem("Sculpt · ปั้น");
        Add(sculpt, "Sculpt Mode · เปิดโหมดปั้น", (_, _) => OpenSculpt());
        var brushes = new ToolStripMenuItem("Brush · หัวแปรง");
        foreach (var (id, label) in BrushChoices)
            Add(brushes, label, (_, _) => OpenSculpt(id));
        sculpt.DropDownItems.Add(brushes);
        Add(sculpt, "ZAdd · เพิ่มผิว", (_, _) => { OpenSculpt(); sculptTools.SetSubtract(false); });
        Add(sculpt, "ZSub · ลดผิว", (_, _) => { OpenSculpt(); sculptTools.SetSubtract(true); });
        Add(sculpt, "Symmetry X · สมมาตร X", (_, _) => { OpenSculpt(); sculptTools.ToggleSymmetryX(); });
        Add(sculpt, "Undo Stroke · ย้อนรอยล่าสุด", async (_, _) => await UndoSculptAsync());

        var view = new ToolStripMenuItem("View · มุมมอง");
        Add(view, "Frame All · จัดให้พอดี", (_, _) => viewport.FitScene());
        Add(view, "Isometric · มองเฉียง", (_, _) => viewport.SetView("isometric"));
        Add(view, "Front · มองด้านหน้า", (_, _) => viewport.SetView("front"));
        Add(view, "Top · มองด้านบน", (_, _) => viewport.SetView("top"));
        var grid = new ToolStripMenuItem("Grid · เส้นตาราง") { Checked = true, CheckOnClick = true };
        grid.CheckedChanged += (_, _) => { viewport.ShowGrid = grid.Checked; viewport.Invalidate(); };
        var wire = new ToolStripMenuItem("Wireframe · โครงเส้น") { CheckOnClick = true };
        wire.CheckedChanged += (_, _) => { viewport.Wireframe = wire.Checked; viewport.Invalidate(); };
        scriptModeItem = new ToolStripMenuItem("Script Mode · โหมดสคริปต์") { CheckOnClick = true };
        scriptModeItem.CheckedChanged += (_, _) => SetScriptMode(scriptModeItem.Checked);
        view.DropDownItems.AddRange([new ToolStripSeparator(), grid, wire, new ToolStripSeparator(), scriptModeItem]);

        var script = new ToolStripMenuItem("Script · สคริปต์");
        Add(script, "Open Python · เปิดโค้ด…", (_, _) => { SetScriptMode(true); OpenCode(); }, idleOnly: false);
        Add(script, "Save Python · บันทึกโค้ด…", (_, _) => { SetScriptMode(true); SaveCode(true); }, idleOnly: false);
        script.DropDownItems.Add(new ToolStripSeparator());
        Add(script, "Blender API · คลังฟังก์ชัน…", (_, _) => ShowApiCatalog(), Keys.Control | Keys.Space);
        Add(script, "Blender Menus · เมนูทั้งหมด…", (_, _) => ShowWorkbench(), Keys.Control | Keys.Shift | Keys.Space);

        var settings = new ToolStripMenuItem("Settings · ตั้งค่า");
        Add(settings, "Blender Path · ตำแหน่ง Blender…", (_, _) => SelectBlender());
        var help = new ToolStripMenuItem("Help · ช่วยเหลือ");
        Add(help, "Guide · คำสั่งและวิธีใช้", (_, _) => ShowHelp(), Keys.F1, false);
        Add(help, "ZBrush-style tools · ตารางเทียบ", (_, _) => ShowZBrushMap(), idleOnly: false);
        Add(help, "About · เกี่ยวกับ มิติ สตูดิโอ", (_, _) => MessageBox.Show(this,
            "มิติ สตูดิโอ 0.9.0\nสตูดิโอโมเดล 3 มิติแบบ GUI · Bilingual GUI studio\n\n" +
            "สร้างรูปทรง ปั้นด้วยหัวแปรงแบบ ZBrush ปรับโครงสร้าง มาสก์ ใส่วัสดุ และส่งออก\n" +
            "ทุกคำสั่งทำงานผ่าน Blender ที่ติดตั้งแยกไว้\n" +
            "โหมดสคริปต์เป็นตัวเลือกเสริม ปิดไว้เป็นค่าเริ่มต้น\n\n" +
            "REA: ตรวจ Blender executable/resources และ API catalog เท่านั้น\n" +
            "ZBrush ไม่ได้ติดตั้งในเครื่องนี้ จึงยังไม่มีหลักฐาน REA ของ ZBrush",
            "เกี่ยวกับ · About", MessageBoxButtons.OK, MessageBoxIcon.Information), idleOnly: false);
        menu.Items.AddRange([file, model, sculpt, view, script, settings, help]);
        return menu;
    }

    private static readonly (string Id, string Label)[] BrushChoices =
    [
        ("STANDARD", "Standard · มาตรฐาน"), ("CLAY", "Clay · เติมดิน"),
        ("CLAY_TUBES", "Clay Tubes · เติมดินก้อน"), ("CLAY_STRIPS", "Clay Strips · เติมดินเส้น"),
        ("MOVE", "Move · ดึงผิว"), ("SMOOTH", "Smooth · เกลี่ยผิว"),
        ("FLATTEN", "Flatten · ปรับแบน"), ("PINCH", "Pinch · บีบผิว"),
        ("INFLATE", "Inflate · พองผิว"), ("CREASE", "Crease · เซาะร่อง"),
        ("DAM_STANDARD", "Dam Standard · ร่องลึก"), ("TRIM_DYNAMIC", "Trim Dynamic · ตัดระนาบ"),
        ("SNAKE_HOOK", "Snake Hook · ดึงยื่น"), ("POLISH", "Polish · ขัดผิว"),
        ("LAYER", "Layer · ยกชั้น"), ("FILL", "Fill · ถมเต็ม")
    ];

    private void Add(ToolStripMenuItem parent, string text, EventHandler handler, Keys shortcut = Keys.None, bool idleOnly = true)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
        item.Click += handler; parent.DropDownItems.Add(item); if (idleOnly) idleItems.Add(item);
    }

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 94, BackColor = Theme.Panel, Padding = new Padding(20, 12, 18, 10) };
        var brand = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 390, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        brand.Controls.Add(Theme.Label("◈  มิติ สตูดิโอ", 21, Theme.Text, true));
        brand.Controls.Add(Theme.Label("GUI 3D Studio · Blender + ZBrush-style tools", 9.5f, Theme.Muted));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
        actions.Controls.Add(Theme.Button("New · ใหม่", (_, _) => NewScene()));
        actions.Controls.Add(Theme.Button("Open · เปิด", (_, _) => OpenProject()));
        actions.Controls.Add(Theme.Button("Save · บันทึก", (_, _) => SaveProject()));
        actions.Controls.Add(Theme.Button("Functions · ฟังก์ชัน", (_, _) => ShowFunctionLibrary()));
        actions.Controls.Add(run); actions.Controls.Add(stop); actions.Controls.Add(export);
        header.Controls.Add(actions); header.Controls.Add(brand); return header;
    }

    private Control BuildSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(12, 14, 12, 10) };
        var title = Theme.Label("SubTools · วัตถุย่อย  ◇", 11, Theme.Text, true);
        title.Dock = DockStyle.Top; title.Height = 34; title.AutoSize = false;
        var details = new Panel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(6, 6, 0, 0) };
        objectDetails.Dock = DockStyle.Fill; details.Controls.Add(objectDetails);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 172, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        var add = Theme.Button("Add Shape · เพิ่มรูปทรง", (_, _) => ShowTool(0, () => shapeTools.ShowCreateTab()), true);
        add.Width = 190; add.Margin = new Padding(0, 2, 0, 6); actions.Controls.Add(add);
        var duplicate = Theme.Button("Duplicate · ทำสำเนา", async (_, _) => await DuplicateSelectedAsync());
        duplicate.Width = 190; actions.Controls.Add(duplicate);
        var rename = Theme.Button("Rename · เปลี่ยนชื่อ", async (_, _) => await RenameSelectedAsync());
        rename.Width = 190; actions.Controls.Add(rename);
        var delete = Theme.Button("Delete · ลบวัตถุ", async (_, _) => await DeleteSelectedAsync());
        delete.Width = 190; actions.Controls.Add(delete);
        var samples = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 148, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 8, 0, 0) };
        samples.Controls.Add(Theme.Label("Samples · ตัวอย่าง", 10, Theme.Muted, true));
        foreach (var entry in new[] { ("01_house.py", "⌂  House · บ้าน"), ("02_robot.py", "▣  Robot · หุ่นยนต์"), ("03_city.py", "▥  City · เมือง") })
        {
            var b = Theme.Button(entry.Item2, (_, _) => LoadExample(entry.Item1));
            b.Width = 190; b.TextAlign = ContentAlignment.MiddleLeft; b.Margin = new Padding(0, 3, 0, 3);
            samples.Controls.Add(b);
        }
        var note = Theme.Label("Right-click a mesh for Blender functions\nคลิกขวาที่โมเดลเพื่อค้นหาคำสั่ง", 8.5f, Theme.Muted);
        note.AutoSize = false; note.Width = 190; note.Height = 40; samples.Controls.Add(note);
        panel.Controls.Add(objects); panel.Controls.Add(title); panel.Controls.Add(details); panel.Controls.Add(actions); panel.Controls.Add(samples);
        return panel;
    }

    private Control BuildEditor()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel, Padding = new Padding(13, 11, 10, 0) };
        fileLabel.Dock = DockStyle.Left;
        var language = Theme.Label("SCRIPT MODE · โหมดสคริปต์", 8.5F, Theme.Accent);
        language.Dock = DockStyle.Right;
        top.Controls.Add(fileLabel); top.Controls.Add(language);
        var hint = Theme.Label("Ctrl+Space ค้นหาฟังก์ชัน Blender · ตัวเลือกเสริม ปิดได้ที่ View > Script Mode", 9, Theme.Accent);
        hint.AutoSize = false; hint.Dock = DockStyle.Bottom; hint.Height = 30; hint.Padding = new Padding(12, 6, 0, 0);
        p.Controls.Add(editor); p.Controls.Add(hint); p.Controls.Add(top); return p;
    }

    private Control BuildViewport()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Panel, Padding = new Padding(12, 8, 5, 0) };
        var title = Theme.Label("Viewport · มุมมอง 3 มิติ", 10, Theme.Text, true); title.Dock = DockStyle.Left;
        var views = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        foreach (var pair in new[] { ("พอดี", "fit"), ("เฉียง", "isometric"), ("หน้า", "front"), ("บน", "top") })
        {
            var b = Theme.Button(pair.Item1, (_, _) => { if (pair.Item2 == "fit") viewport.FitScene(); else viewport.SetView(pair.Item2); });
            b.MinimumSize = new Size(48, 27); b.Height = 27; b.Font = new Font("Leelawadee UI", 8.5F); b.Padding = new Padding(5, 0, 5, 0);
            views.Controls.Add(b);
        }
        top.Controls.Add(views); top.Controls.Add(title);
        var info = new Panel { Dock = DockStyle.Bottom, Height = 32, BackColor = Theme.Panel, Padding = new Padding(12, 6, 0, 0) };
        modelLabel.Dock = DockStyle.Fill; info.Controls.Add(modelLabel);
        p.Controls.Add(viewport); p.Controls.Add(info); p.Controls.Add(top); return p;
    }

    private Control BuildToolPalette()
    {
        var create = modelToolTabs.Add("Create\nสร้าง");
        create.Controls.Add(shapeTools);
        var functions = modelToolTabs.Add("Functions\nฟังก์ชัน");
        functions.Controls.Add(objectActions);
        var sculpt = modelToolTabs.Add("Sculpt\nปั้น");
        sculpt.Controls.Add(sculptTools);
        var topology = modelToolTabs.Add("Topology\nโครงสร้าง");
        topology.Controls.Add(topologyTools);
        var mask = modelToolTabs.Add("Mask\nมาสก์");
        mask.Controls.Add(maskTools);
        var material = modelToolTabs.Add("Material\nวัสดุ");
        material.Controls.Add(materialTools);
        modelToolTabs.SelectedIndex = 2;
        modelToolTabs.SelectedIndexChanged += (_, _) =>
        {
            if (modelToolTabs.SelectedIndex != 2 && sculptTools.Active) sculptTools.SetActive(false);
        };
        return modelToolTabs;
    }

    private void ShowTool(int index, Action? after = null)
    {
        if (Busy) return;
        modelToolTabs.SelectedIndex = index;
        after?.Invoke();
    }

    private Control BuildOutput()
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(12, 0, 10, 6) };
        var top = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Theme.Background };
        var label = Theme.Label("Output · ผลการทำงาน", 9, Theme.Muted, true);
        label.Dock = DockStyle.Left; label.Padding = new Padding(0, 5, 0, 0);
        var clear = Theme.Button("Clear · ล้าง", (_, _) => output.Clear());
        clear.Dock = DockStyle.Right; clear.Height = 25; clear.MinimumSize = new Size(64, 25); clear.Font = new Font("Leelawadee UI", 8);
        top.Controls.Add(label); top.Controls.Add(clear); p.Controls.Add(output); p.Controls.Add(top); return p;
    }

    private void SelectPanels(string? name)
    {
        objectActions.SetTargetObject(name ?? "");
        topologyTools.SetTargetObject(name);
        maskTools.SetTargetObject(name);
        materialTools.SetTargetObject(name);
        if (name != null)
        {
            var obj = viewport.Objects.FirstOrDefault(o => o.Name == name);
            if (obj != null) shapeTools.SelectObject(obj);
        }
    }

    private void SelectObject(string name)
    {
        for (var i = 0; i < objects.Items.Count; i++)
            if (objects.Items[i]?.ToString() == name) { objects.SelectedIndex = i; return; }
    }

    private void SetScriptMode(bool enabled)
    {
        if (scriptMode == enabled) return;
        scriptMode = enabled;
        scriptSplit.Panel1Collapsed = !enabled;
        scriptModeItem.Checked = enabled;
        if (enabled)
        {
            if (scriptSplit.Width > 700) scriptSplit.SplitterDistance = (int)(scriptSplit.Width * 0.44);
            status.Text = "เปิดโหมดสคริปต์ — แก้โค้ดแล้วกด F5 · Script mode enabled";
            editor.FocusCode();
        }
        else
        {
            status.Text = "โหมด GUI — ใช้แผงเครื่องมือด้านขวา · GUI mode";
            viewport.Focus();
        }
    }

    private void OpenSculpt(string? mode = null)
    {
        if (Busy) return;
        modelToolTabs.SelectedIndex = 2;
        if (mode is not null) sculptTools.SetMode(mode);
        sculptTools.SetActive(true);
        viewport.Focus();
    }

    private void OpenObjectActions(string name)
    {
        if (Busy || string.IsNullOrEmpty(name)) return;
        modelToolTabs.SelectedIndex = 1;
        objectActions.PickObject(name);
        objectMenuSearch.Text = "";
        RefreshObjectMenu();
        objectMenu.Show(Cursor.Position);
        objectMenuSearch.Focus();
    }

    private void RefreshObjectMenu()
    {
        objectActions.SetQuery(objectMenuSearch.Text);
        while (objectMenu.Items.Count > 1) objectMenu.Items.RemoveAt(1);
        var transformItem = new ToolStripMenuItem("Transform · ย้าย / หมุน / สเกล");
        transformItem.Click += (_, _) => ShowTool(0, () => shapeTools.ShowTransformTab());
        objectMenu.Items.Add(transformItem);
        var createItem = new ToolStripMenuItem("Add Shape · เพิ่มรูปทรงใหม่…");
        createItem.Click += (_, _) => ShowTool(0, () => shapeTools.ShowCreateTab());
        objectMenu.Items.Add(createItem);
        objectMenu.Items.Add(new ToolStripSeparator());
        foreach (var entry in objectActions.Matches.Take(8))
        {
            var option = new ToolStripMenuItem(ApiCatalogService.BilingualName(entry.Name, entry.ThaiName) + "  ·  " + entry.Id);
            option.Click += (_, _) => objectActions.Choose(entry);
            objectMenu.Items.Add(option);
        }
    }

    private void ShowApiCatalog()
    {
        if (Busy) return;
        using var dialog = new ApiCatalogForm(() => blenderPath);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedCode == null) return;
        SetScriptMode(true);
        editor.InsertSnippet(dialog.SelectedCode);
    }

    private void ShowFunctionLibrary()
    {
        if (Busy) return;
        using var dialog = new FunctionCatalogForm();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.BlenderOperator is { } entry)
        {
            ShowTool(1);
            objectActions.Choose(entry);
            status.Text = $"เลือก {ApiCatalogService.BilingualName(entry.Name, entry.ThaiName)} แล้ว — ตั้งค่าและกดใช้กับวัตถุ · Tool selected";
        }
        else if (dialog.ToolPanel is int panel)
        {
            ShowTool(panel);
            if (panel == 2) OpenSculpt();
        }
    }

    private void ShowWorkbench()
    {
        if (Busy) return;
        using var dialog = new BlenderWorkbenchForm();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedCode == null) return;
        SetScriptMode(true);
        editor.InsertSnippet(dialog.SelectedCode);
        if (dialog.RequiresWindowContext) status.Text = "คำสั่งนี้ใช้บริบทหน้าต่าง Blender ขณะรัน · Needs a Blender window context";
    }

    private void SetCode(string code, string? path)
    {
        loading = true; editor.Code = code; loading = false;
        sculptHistory.Clear();
        currentPath = path; dirty = false; UpdateTitle();
    }

    private async Task<bool> ApplySnippetAsync(string code, string message)
    {
        if (Busy) return false;
        CancelLivePreview();
        editor.AppendSnippet(code, focus: false);
        dirty = true; UpdateTitle();
        status.Text = message;
        return await BuildModelAsync();
    }

    private async Task UndoSculptAsync()
    {
        if (Busy || sculptHistory.Count == 0) { status.Text = "ยังไม่มีรอยแปรงให้ย้อน · Nothing to undo"; return; }
        var (after, before) = sculptHistory.Peek();
        if (editor.Code != after) { status.Text = "โค้ดเปลี่ยนหลังปั้น — ย้อนในโหมดสคริปต์ · Code changed after the stroke"; return; }
        sculptHistory.Pop(); editor.Code = before;
        dirty = true; UpdateTitle();
        await BuildModelAsync();
        if (!IsDisposed) viewport.Focus();
    }

    private void UpdateTitle()
    {
        var name = currentPath == null ? "ฉากใหม่ · Untitled" : Path.GetFileName(currentPath);
        fileLabel.Text = name + (dirty ? "  •" : "");
        Text = $"{(dirty ? "• " : "")}{name} — มิติ สตูดิโอ";
    }

    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "บันทึกโปรเจกต์ก่อนหรือไม่?\nSave the project before continuing?", "ยังไม่ได้บันทึก · Unsaved",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && SaveProject();
    }

    private void NewScene()
    {
        if (Busy || !ConfirmDiscard()) return;
        SetCode(DefaultCode, null); dirty = false; UpdateTitle();
        status.Text = "ฉากใหม่พร้อมใช้งาน · New scene";
    }

    private async void OpenProject()
    {
        if (Busy || !ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog { Title = "เปิดโปรเจกต์ · Open project", Filter = "Miti Studio Project (*.miti)|*.miti|ไฟล์ทั้งหมด (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var project = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(dialog.FileName, Encoding.UTF8));
            if (project?.Source == null) throw new InvalidDataException("ไฟล์โปรเจกต์ไม่ถูกต้อง · Invalid project file");
            SetCode(project.Source, dialog.FileName);
            if (blenderPath != null && await BuildModelAsync()) status.Text = "เปิดโปรเจกต์แล้ว · Project opened";
        }
        catch (Exception ex) { ShowError("เปิดโปรเจกต์ไม่สำเร็จ", ex); }
    }

    private bool SaveProject(bool saveAs = false)
    {
        var path = currentPath;
        if (saveAs || path == null || !path.EndsWith(".miti", StringComparison.OrdinalIgnoreCase))
        {
            using var dialog = new SaveFileDialog { Title = "บันทึกโปรเจกต์ · Save project", Filter = "Miti Studio Project (*.miti)|*.miti",
                FileName = path == null ? "โมเดลของฉัน.miti" : Path.GetFileNameWithoutExtension(path) + ".miti", DefaultExt = "miti", AddExtension = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new ProjectFile(1, editor.Code), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(false));
            currentPath = path; dirty = false; UpdateTitle();
            status.Text = "บันทึกโปรเจกต์แล้ว · Project saved";
            return true;
        }
        catch (Exception ex) { ShowError("บันทึกโปรเจกต์ไม่สำเร็จ", ex); return false; }
    }

    private void OpenCode()
    {
        if (Busy || !ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog { Title = "เปิดโค้ด Python · Open Python", Filter = "โค้ด Python (*.py)|*.py|ไฟล์ทั้งหมด (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { SetCode(File.ReadAllText(dialog.FileName, Encoding.UTF8), dialog.FileName); }
        catch (Exception ex) { ShowError("เปิดไฟล์ไม่สำเร็จ", ex); }
    }

    private bool SaveCode(bool saveAs = false)
    {
        SetScriptMode(true);
        var path = currentPath != null && currentPath.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? currentPath : null;
        if (saveAs || path == null)
        {
            using var dialog = new SaveFileDialog { Title = "บันทึกโค้ด Python · Save Python", Filter = "โค้ด Python (*.py)|*.py",
                FileName = path == null ? "โมเดลของฉัน.py" : Path.GetFileName(path), DefaultExt = "py", AddExtension = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try { File.WriteAllText(path, editor.Code, new UTF8Encoding(false)); currentPath = path; dirty = false; UpdateTitle(); status.Text = "บันทึกโค้ดแล้ว · Python saved"; return true; }
        catch (Exception ex) { ShowError("บันทึกไฟล์ไม่สำเร็จ", ex); return false; }
    }

    private async void LoadExample(string name)
    {
        if (Busy || !ConfirmDiscard()) return;
        var path = Path.Combine(AppContext.BaseDirectory, "examples", name);
        try { SetCode(File.ReadAllText(path, Encoding.UTF8), null); await BuildModelAsync(); }
        catch (Exception ex) { ShowError("เปิดตัวอย่างไม่สำเร็จ", ex); }
    }

    private async Task<string?> PromptAsync(string title, string label, string initial)
    {
        using var dialog = new Form { Text = title, Size = new Size(440, 200), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Leelawadee UI", 10) };
        var input = new TextBox { Text = initial, Dock = DockStyle.Fill, BackColor = Theme.Raised, ForeColor = Theme.Text };
        Theme.UseDarkControls(input); Theme.UseDarkWindow(dialog);
        var ok = Theme.Button("OK · ตกลง", (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); }, true);
        var cancel = Theme.Button("Cancel · ยกเลิก", (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16, 14, 16, 4) };
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.Controls.Add(Theme.Label(label, 9.5f, Theme.Text), 0, 0); body.Controls.Add(input, 0, 1);
        dialog.Controls.Add(body); dialog.Controls.Add(buttons);
        await Task.Yield();
        return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
    }

    private async Task DuplicateSelectedAsync()
    {
        var name = viewport.SelectedObject ?? objectActions.TargetObject;
        if (name == null) { status.Text = "เลือกวัตถุก่อนทำสำเนา · Select a subtool first"; return; }
        var newName = await PromptAsync("ทำสำเนา · Duplicate", "ชื่อวัตถุใหม่ · New subtool name", name + "_copy");
        if (string.IsNullOrWhiteSpace(newName)) return;
        var code = new StringBuilder()
            .AppendLine().AppendLine("# ทำสำเนา · Duplicate subtool")
            .Append("_miti_src = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(name)).AppendLine(")")
            .AppendLine("if _miti_src is not None:")
            .AppendLine("    _miti_copy = _miti_src.copy()")
            .AppendLine("    _miti_copy.data = _miti_src.data.copy()")
            .AppendLine("    bpy.context.collection.objects.link(_miti_copy)")
            .Append("    _miti_copy.name = ").AppendLine(ApiCatalogService.PythonString(newName))
            .ToString();
        if (await ApplySnippetAsync(code, "กำลังทำสำเนาวัตถุ…")) SelectObject(newName);
    }

    private async Task RenameSelectedAsync()
    {
        var name = viewport.SelectedObject ?? objectActions.TargetObject;
        if (name == null) { status.Text = "เลือกวัตถุก่อนเปลี่ยนชื่อ · Select a subtool first"; return; }
        var newName = await PromptAsync("เปลี่ยนชื่อ · Rename", "ชื่อวัตถุใหม่ · New subtool name", name);
        if (string.IsNullOrWhiteSpace(newName) || newName == name) return;
        var code = new StringBuilder()
            .AppendLine().AppendLine("# เปลี่ยนชื่อวัตถุ · Rename subtool")
            .Append("_miti_obj = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(name)).AppendLine(")")
            .AppendLine("if _miti_obj is None: raise ValueError('ไม่พบวัตถุ · Object not found')")
            .Append("_miti_obj.name = ").AppendLine(ApiCatalogService.PythonString(newName)).ToString();
        if (await ApplySnippetAsync(code, "กำลังเปลี่ยนชื่อวัตถุ…")) SelectObject(newName);
    }

    private async Task DeleteSelectedAsync()
    {
        var name = viewport.SelectedObject ?? objectActions.TargetObject;
        if (name == null) { status.Text = "เลือกวัตถุก่อนลบ · Select a subtool first"; return; }
        if (MessageBox.Show(this, $"ลบวัตถุ “{name}” หรือไม่?\nDelete the subtool “{name}”?", "ยืนยันการลบ · Confirm delete",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        var code = new StringBuilder()
            .AppendLine().AppendLine("# ลบวัตถุ · Delete subtool")
            .Append("_miti_obj = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(name)).AppendLine(")")
            .AppendLine("if _miti_obj is not None: bpy.data.objects.remove(_miti_obj, do_unlink=True)")
            .ToString();
        await ApplySnippetAsync(code, "กำลังลบวัตถุ…");
    }

    private async Task<bool> BuildModelAsync()
    {
        if (Busy) return false;
        CancelLivePreview();
        if (!EnsureBlender()) return false;
        var source = editor.Code;
        var cts = BeginOperation("กำลังสร้างโมเดล… · Building…"); output.Clear();
        var watch = Stopwatch.StartNew();
        try
        {
            Log("เริ่มสร้างโมเดลด้วย Blender… · Building with Blender…");
            var jobDir = Path.Combine(dataDir, "Builds", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
            var result = await blender.BuildAsync(blenderPath!, source, jobDir, new Progress<string>(Log), cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            var selectedBefore = viewport.SelectedObject ?? objectActions.TargetObject;
            viewport.LoadScene(result.PreviewPath);
            lastBuild = result; lastBuiltSource = source;
            objects.Items.Clear(); foreach (var item in viewport.Objects) objects.Items.Add(item.Name);
            var selectedIndex = selectedBefore == null ? -1 : objects.Items.IndexOf(selectedBefore);
            if (selectedIndex >= 0) objects.SelectedIndex = selectedIndex;
            else { objectDetails.Text = "เลือกวัตถุเพื่อดูรายละเอียด\nSelect an object"; SelectPanels(null); }
            modelLabel.Text = $"{viewport.Objects.Count:N0} objects · วัตถุ  ·  {viewport.VertexCount:N0} จุดยอด  ·  {viewport.TriangleCount:N0} สามเหลี่ยม";
            using (var json = JsonDocument.Parse(File.ReadAllText(result.PreviewPath)))
                if (json.RootElement.TryGetProperty("simplified", out var simplified) && simplified.ValueKind == JsonValueKind.True)
                {
                    modelLabel.Text += "  ·  ย่อเฉพาะภาพตัวอย่าง";
                    Log("ภาพตัวอย่างลดจำนวนสามเหลี่ยมเพื่อความลื่นไหล ไฟล์โมเดลยังมีรายละเอียดครบ");
                }
            status.Text = $"สร้างสำเร็จ · Done  ·  {watch.Elapsed.TotalSeconds:N1}s";
            Log($"สำเร็จ • {viewport.Objects.Count} วัตถุ • {watch.Elapsed.TotalSeconds:N1} วินาที");
            return true;
        }
        catch (OperationCanceledException) { status.Text = "หยุดสร้างโมเดลแล้ว · Cancelled"; Log("ผู้ใช้หยุดการทำงาน"); return false; }
        catch (Exception ex)
        {
            status.Text = "สร้างไม่สำเร็จ — ดูข้อผิดพลาดด้านล่าง · Build failed";
            Log("ข้อผิดพลาด: " + ex.Message);
            lastBuild = null; lastBuiltSource = null; viewport.ClearScene(); objects.Items.Clear();
            modelLabel.Text = "การสร้างล้มเหลว — ไม่มีโมเดลค้างจากครั้งก่อน · Build failed";
            return false;
        }
        finally { EndOperation(cts); }
    }

    private void CancelLivePreview()
    {
        previewVersion++;
        previewOperation?.Cancel();
        previewOperation = null;
        if (livePreviewShowing && lastBuild != null && File.Exists(lastBuild.PreviewPath))
        {
            try { viewport.LoadScene(lastBuild.PreviewPath, preserveView: true); UpdateSceneMetrics(false); }
            catch (Exception) { }
        }
        livePreviewShowing = false;
        if (!Busy) export.Enabled = lastBuild != null;
    }

    private async Task ShowLivePreviewAsync(string snippet)
    {
        if (Busy || blenderPath == null) return;
        CancelLivePreview();
        var current = ++previewVersion;
        var cts = new CancellationTokenSource(); previewOperation = cts;
        status.Text = "กำลังอัปเดตภาพตัวอย่างตามพารามิเตอร์…";
        objectActions.SetPreviewStatus("Blender กำลังคำนวณ…");
        shapeTools.SetPreviewStatus("Blender กำลังคำนวณ…");
        export.Enabled = false;
        try
        {
            var path = await blender.PreviewAsync(blenderPath, editor.Code + snippet, Path.Combine(dataDir, "Previews"), cts.Token);
            if (cts.IsCancellationRequested || current != previewVersion || IsDisposed || Busy) return;
            viewport.LoadScene(path, preserveView: true);
            viewport.PreviewCuts = 0;
            livePreviewShowing = true;
            UpdateSceneMetrics(true);
            status.Text = "ภาพตัวอย่างสด — กด Build เพื่อบันทึกผล · Live preview, press Build to keep it";
            objectActions.SetPreviewStatus("ภาพตัวอย่างใหม่ ✓ กดใช้เพื่อบันทึก");
            shapeTools.SetPreviewStatus("ภาพตัวอย่างใหม่ ✓ กดใช้เพื่อบันทึก");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (current == previewVersion)
            {
                status.Text = "แสดงภาพตัวอย่างไม่ได้: " + ex.Message.Split('\n')[0];
                objectActions.SetPreviewStatus("ภาพตัวอย่างไม่สำเร็จ — ดูข้อความด้านล่าง");
                shapeTools.SetPreviewStatus("ภาพตัวอย่างไม่สำเร็จ — ดูข้อความด้านล่าง");
            }
        }
        finally
        {
            if (ReferenceEquals(previewOperation, cts)) { previewOperation = null; if (!livePreviewShowing && !Busy) export.Enabled = lastBuild != null; }
            cts.Dispose();
        }
    }

    private void UpdateSceneMetrics(bool temporary)
    {
        modelLabel.Text = $"{(temporary ? "ตัวอย่าง · Preview  ·  " : "")}{viewport.Objects.Count:N0} objects · วัตถุ  ·  {viewport.VertexCount:N0} จุดยอด  ·  {viewport.TriangleCount:N0} สามเหลี่ยม";
        var selected = viewport.Objects.FirstOrDefault(o => o.Name == viewport.SelectedObject);
        if (selected != null) objectDetails.Text = $"{selected.VertexCount:N0} จุดยอด · vertices\n{selected.TriangleCount:N0} สามเหลี่ยม · triangles";
    }

    private async Task ExportModelAsync()
    {
        if (Busy) return;
        if (livePreviewShowing) { status.Text = "กด Build ก่อนส่งออกภาพตัวอย่างที่ปรับค่า · Build the live preview first"; return; }
        if (lastBuild == null || lastBuiltSource != editor.Code) { if (!await BuildModelAsync()) return; }
        using var dialog = new SaveFileDialog { Title = "ส่งออกโมเดล · Export model", FileName = "โมเดลของฉัน",
            Filter = "โมเดล GLB — Godot / เว็บ (*.glb)|*.glb|โปรเจกต์ Blender (*.blend)|*.blend|โมเดล OBJ (*.obj)|*.obj|งานพิมพ์ 3 มิติ STL (*.stl)|*.stl", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var format = new[] { "glb", "blend", "obj", "stl" }[dialog.FilterIndex - 1];
        var cts = BeginOperation("กำลังส่งออกโมเดล… · Exporting…");
        try
        {
            await blender.ExportAsync(blenderPath!, lastBuild!.ScenePath, dialog.FileName, format, new Progress<string>(Log), cts.Token);
            status.Text = "ส่งออกสำเร็จ · Exported: " + Path.GetFileName(dialog.FileName); Log("บันทึกโมเดล: " + dialog.FileName);
            MessageBox.Show(this, "ส่งออกโมเดลสำเร็จ\nExport complete\n" + dialog.FileName, "บันทึกแล้ว · Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) { status.Text = "หยุดส่งออกแล้ว · Export cancelled"; }
        catch (Exception ex) { ShowError("ส่งออกไม่สำเร็จ", ex); }
        finally { EndOperation(cts); }
    }

    private CancellationTokenSource BeginOperation(string message)
    {
        operation = new CancellationTokenSource(); status.Text = message;
        run.Enabled = false; export.Enabled = false; stop.Enabled = true; editor.IsReadOnly = true;
        viewport.SculptEnabled = false; sculptTools.Enabled = false; modelToolTabs.Enabled = false;
        foreach (var item in idleItems) item.Enabled = false;
        UseWaitCursor = false;
        return operation;
    }

    private void EndOperation(CancellationTokenSource cts)
    {
        if (operation == cts) operation = null;
        cts.Dispose(); if (closing || IsDisposed) return;
        run.Enabled = true; export.Enabled = lastBuild != null && !livePreviewShowing; stop.Enabled = false; editor.IsReadOnly = !scriptMode;
        sculptTools.Enabled = true; viewport.SculptEnabled = sculptTools.Active; modelToolTabs.Enabled = true;
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
        engineLabel.Text = blenderPath == null ? "ยังไม่พบ Blender · Blender not found" : "BLENDER  ·  พร้อมใช้งาน · Ready";
    }
    private bool EnsureBlender()
    {
        if (blenderPath != null && File.Exists(blenderPath)) return true;
        MessageBox.Show(this, "เลือกไฟล์ blender.exe เพื่อใช้สร้างโมเดล\nPick blender.exe to build models.", "เชื่อมต่อ Blender · Connect Blender", MessageBoxButtons.OK, MessageBoxIcon.Information);
        SelectBlender(); return blenderPath != null && File.Exists(blenderPath);
    }
    private void SelectBlender()
    {
        if (Busy) return;
        using var dialog = new OpenFileDialog { Title = "เลือก blender.exe · Select Blender", Filter = "Blender (blender.exe)|blender.exe", FileName = "blender.exe" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        blenderPath = dialog.FileName;
        try { Directory.CreateDirectory(dataDir); File.WriteAllText(Path.Combine(dataDir, "settings.json"), JsonSerializer.Serialize(new Settings(blenderPath))); }
        catch (Exception ex) { Log("จำตำแหน่ง Blender ไม่สำเร็จ: " + ex.Message); }
        UpdateEngineLabel(); Log("ใช้ Blender: " + blenderPath);
    }
    private void OpenInBlender()
    {
        if (lastBuild == null) { status.Text = "สร้างโมเดลก่อนเปิดใน Blender · Build a model first"; return; }
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
        if (lastBuild == null) { status.Text = "สร้างโมเดลก่อนบันทึกภาพ · Build a model first"; return; }
        using var dialog = new SaveFileDialog { Title = "บันทึกภาพมุมมอง · Save view", Filter = "ภาพ PNG (*.png)|*.png", FileName = "โมเดลของฉัน.png" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { viewport.SaveScreenshot(dialog.FileName); status.Text = "บันทึกภาพแล้ว · View saved"; }
        catch (Exception ex) { ShowError("บันทึกภาพไม่สำเร็จ", ex); }
    }
    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && Busy) { operation?.Cancel(); e.SuppressKeyPress = true; }
        if (!viewport.Focused || !sculptTools.Active || Busy) return;
        if (e.Control && e.KeyCode == Keys.Z) { _ = UndoSculptAsync(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.X) { sculptTools.ToggleSymmetryX(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.B) { sculptTools.FocusBrush(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.OemOpenBrackets) { sculptTools.AdjustRadius(-1); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.OemCloseBrackets) { sculptTools.AdjustRadius(1); e.SuppressKeyPress = true; }
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        closing = true; CancelLivePreview(); operation?.Cancel();
    }
    private void ShowHelp()
    {
        using var dialog = new Form { Text = "คู่มือ · Guide — มิติ สตูดิโอ", Size = new Size(860, 760), StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Panel, ForeColor = Theme.Text, MinimizeBox = false, MaximizeBox = false };
        var text = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.Panel,
            ForeColor = Theme.Text, Font = new Font("Leelawadee UI", 11), BorderStyle = BorderStyle.None,
            DetectUrls = true, Padding = new Padding(20), Text = HelpText };
        dialog.Padding = new Padding(22); dialog.Controls.Add(text); dialog.ShowDialog(this);
    }
    private void ShowZBrushMap()
    {
        MessageBox.Show(this,
            "ZBrush-style tools → Blender engine\n\n" +
            "SubTool · วัตถุย่อย → Scene object + Duplicate/Rename/Delete\n" +
            "Brushes · หัวแปรง → 16 brushes (Standard … Fill)\n" +
            "DynaMesh → Voxel Remesh\n" +
            "ZRemesher → Quad Remesh (QuadriFlow)\n" +
            "Divide / SDiv → Subdivide\n" +
            "Decimation Master → Decimate\n" +
            "Masking → MitiMask vertex group + Feather/Region\n" +
            "Deformation → Taper/Twist/Bend/Stretch\n" +
            "Polypaint → Material colour (per object)\n" +
            "GoZ → Export GLB/OBJ/BLEND/STL\n\n" +
            "ZBrush ไม่ได้ติดตั้งในเครื่องนี้ จึงไม่มี REA ของ ZBrush\n" +
            "ZBrush is not installed here, so no ZBrush REA evidence exists.\n" +
            "ฟังก์ชันเหล่านี้เป็นการทำงานแบบ ZBrush-style บน Blender ไม่ใช่การเลียนแบบทุกฟังก์ชัน",
            "ZBrush-style tools · ตารางเทียบ", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private sealed record Settings(string? BlenderPath);
    private sealed record ProjectFile(int Version, string Source);

    private const string DefaultCode = """
        # ฉากเริ่มต้น · Default scene
        ล้างฉาก()
        _miti_mat = วัสดุ("วัสดุเริ่มต้น", "#5BE2BA", roughness=0.45, metallic=0.0)
        กล่อง("กล่องเริ่มต้น", size=(2, 2, 2), location=(0, 0, 1), material=_miti_mat)
        """;

    private const string HelpText = """
มิติ สตูดิโอ 0.9 — สตูดิโอโมเดล 3 มิติแบบ GUI
Miti Studio 0.9 — bilingual GUI 3D studio

เริ่มใช้งาน
1. เปิดโปรแกรม จะสร้างฉากเริ่มต้นให้อัตโนมัติถ้าพบ Blender
2. เลือกวัตถุจาก SubTools ด้านซ้าย หรือคลิกบนโมเดลใน viewport
3. ใช้แผงเครื่องมือด้านขวา: Create, Functions, Sculpt, Topology, Mask, Material
4. กด Build (F5) เมื่อต้องการบันทึกการเปลี่ยนแปลง
5. Export เพื่อบันทึก GLB, BLEND, OBJ หรือ STL

Create · สร้าง
สร้างกล่อง ทรงกลม ทรงกระบอก กรวย โดนัท ระนาบ หรือหัวลิง
กำหนดชื่อ ตำแหน่ง และสเกล พร้อมภาพตัวอย่างก่อนกดใช้

Sculpt · ปั้น
หัวแปรงแบบ ZBrush 16 แบบ: Standard, Clay, Clay Tubes, Clay Strips, Move,
Smooth, Flatten, Pinch, Inflate, Crease, Dam Standard, Trim Dynamic,
Snake Hook, Polish, Layer, Fill
ZAdd/ZSub, Symmetry X/Y/Z, Draw Size, Z Intensity, Focal Shift, Detail
ลากซ้ายบนผิวโมเดล ปล่อยเพื่อบันทึก • Alt+ลาก = หมุน • Shift+ลาก = Smooth
X = สลับ Symmetry X • [ ] = ปรับขนาดแปรง • Undo Stroke = ย้อนรอยล่าสุด

Topology · โครงสร้าง
Voxel Remesh (แบบ DynaMesh), Quad Remesh (QuadriFlow), Subdivide,
Decimate, Mirror, Merge by Distance, Shade Smooth/Flat,
Deform (Taper/Twist/Bend/Stretch), Bevel, Solidify, Apply All Modifiers

Mask · มาสก์
มาสก์ทั้งชิ้น เหนือ/ใต้ระดับตามแกน กลับด้าน เบลอขอบ
เมื่อเปิด ใช้มาสก์ตอนปั้น แปรงจะไม่แตะบริเวณที่ถูกมาสก์

Material · วัสดุ
ชื่อวัสดุ สีหลัก Metallic Roughness Emission และวัสดุสำเร็จรูป 5 แบบ

Functions · ฟังก์ชัน
ค้นหาคำสั่ง Blender กว่า 2,400 รายการ ปรับพารามิเตอร์แล้วใช้กับวัตถุ
คลิกขวาที่โมเดลเพื่อเปิดรายการคำสั่งแบบเร็ว

SubTools · วัตถุย่อย
ทำสำเนา เปลี่ยนชื่อ หรือลบวัตถุ พร้อมดูจำนวนจุดยอดและสามเหลี่ยม

โหมดสคริปต์ (ตัวเลือกเสริม)
View > Script Mode เปิดตัวแก้ไขโค้ด Python และคลัง Blender API
คำสั่ง GUI ทั้งหมดถูกบันทึกเป็นคำสั่ง Blender ภายในโปรเจกต์อัตโนมัติ

ไฟล์โปรเจกต์
File > Save Project บันทึกเป็น .miti และเปิดกลับได้ภายหลัง
การส่งออกยังใช้ Blender จริงเพื่อสร้างไฟล์โมเดล

คีย์ลัด
F5 สร้าง • Escape หยุด • F1 คู่มือ
Ctrl+N ฉากใหม่ • Ctrl+O เปิดโปรเจกต์ • Ctrl+S บันทึกโปรเจกต์ • Ctrl+E ส่งออก

REA
ตรวจ Blender executable/resources และดึง API catalog ด้วย runtime introspection
ZBrush ไม่ได้ติดตั้งในเครื่องนี้ จึงยังไม่มี REA ของ ZBrush และแอปไม่ อ้างว่ามี
ฟังก์ชัน ZBrush ครบทั้งโปรแกรม — เครื่องมือที่เห็นคือ ZBrush-style บน Blender
""";
}
