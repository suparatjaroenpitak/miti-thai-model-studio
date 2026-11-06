using System.Text;

namespace ThaiModelStudio.Controls;

/// <summary>One bilingual, searchable library for Blender API entries and ZBrush-style tools.</summary>
public sealed class FunctionCatalogForm : Form
{
    private sealed record Filter(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private sealed record Row(string Name, string Thai, string Source, string Kind, string Id, string Status,
        string Mapping, string Description, string ThaiDescription, ApiEntry? Entry, ZBrushFunction? Tool)
    {
        public string DisplayName => ApiCatalogService.BilingualName(Name, Thai);
    }

    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search · ค้นหาชื่อ รหัส หรือคำอธิบาย…",
        BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly ComboBox source = new StudioComboBox { Width = 210 };
    private readonly Label count = Theme.Label("กำลังอ่านคลังฟังก์ชัน…", 9, Theme.Muted);
    private readonly RichTextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Leelawadee UI", 10) };
    private readonly DataGridView list = new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None,
        ForeColor = Theme.Text, ReadOnly = true, RowHeadersVisible = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
        EnableHeadersVisualStyles = false, GridColor = Theme.Border, RowTemplate = { Height = 30 }
    };
    private readonly Button openTool;
    private readonly Button useOperator;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 240 };
    private List<Row> rows = [];
    private List<Row> visible = [];

    public FunctionCatalogForm()
    {
        Text = "Function Library · คลังฟังก์ชัน Blender + ZBrush-style — มิติ สตูดิโอ";
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Size = new Size(1380, 880); MinimumSize = new Size(1040, 680);
        StartPosition = FormStartPosition.CenterParent; Font = new Font("Leelawadee UI", 10); Padding = new Padding(14);

        openTool = Theme.Button("Open tool · เปิดเครื่องมือ", (_, _) => Accept(), true);
        openTool.Enabled = false;
        useOperator = Theme.Button("Use with object · ใช้กับวัตถุ", (_, _) => Accept());
        useOperator.Enabled = false;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
        footer.Controls.Add(openTool); footer.Controls.Add(useOperator); footer.Controls.Add(Theme.Button("Close · ปิด", (_, _) => Close()));

        var info = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(0, 8, 0, 0) };
        count.Dock = DockStyle.Fill; info.Controls.Add(count);

        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 3, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        source.Items.AddRange([
            new Filter("All sources · ทุกแหล่ง", ""),
            new Filter("Blender API · คลัง Blender", "blender"),
            new Filter("ZBrush-style · เครื่องมือ ZBrush", "zbrush")
        ]);
        source.SelectedIndex = 0;
        Theme.UseDarkControls(search, source);
        search.Margin = new Padding(0, 2, 10, 4); source.Margin = new Padding(0, 2, 10, 4);
        var hint = Theme.Label("กด Enter เพื่อใช้งานรายการที่เลือก", 8.5f, Theme.Muted);
        hint.Dock = DockStyle.Fill; hint.Padding = new Padding(0, 12, 0, 0);
        top.Controls.Add(search, 0, 0); top.Controls.Add(source, 1, 0); top.Controls.Add(hint, 2, 0);

        list.Columns.Add("name", "Name · ชื่อ");
        list.Columns.Add("source", "Source · แหล่ง");
        list.Columns.Add("kind", "Kind · ประเภท");
        list.Columns.Add("mapping", "Implementation · การทำงาน");
        list.Columns[0].FillWeight = 36; list.Columns[1].FillWeight = 12; list.Columns[2].FillWeight = 16; list.Columns[3].FillWeight = 36;
        list.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, ForeColor = Theme.Text,
            SelectionBackColor = Theme.Raised, SelectionForeColor = Theme.Accent, Padding = new Padding(3) };
        list.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Raised, ForeColor = Theme.Muted, Padding = new Padding(3) };
        list.VirtualMode = true;
        list.CellValueNeeded += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= visible.Count) return;
            var row = visible[e.RowIndex];
            e.Value = e.ColumnIndex switch
            {
                0 => row.DisplayName,
                1 => row.Source,
                2 => row.Kind,
                _ => row.Mapping
            };
        };
        list.CurrentCellChanged += (_, _) => SelectRow();
        list.CellDoubleClick += (_, _) => Accept();

        var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = Theme.Border, SplitterWidth = 5,
            Size = new Size(1300, 640), SplitterDistance = 780 };
        split.Panel1.Controls.Add(list);
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(14) };
        right.Controls.Add(details);
        split.Panel2.Controls.Add(right);
        Controls.Add(split); Controls.Add(info); Controls.Add(footer); Controls.Add(top);

        search.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        debounce.Tick += (_, _) => { debounce.Stop(); ApplyFilter(); };
        source.SelectedIndexChanged += (_, _) => ApplyFilter();
        Shown += async (_, _) => await LoadCatalogAsync();
        FormClosed += (_, _) => debounce.Dispose();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !search.Focused) { Accept(); e.SuppressKeyPress = true; }
        };
        Theme.UseDarkWindow(this); Theme.UseDarkControls(list);
    }

    public ApiEntry? BlenderOperator { get; private set; }
    public int? ToolPanel { get; private set; }

    private async Task LoadCatalogAsync()
    {
        try
        {
            var blender = await ApiCatalogService.LoadAsync();
            var zbrush = ZBrushCatalogService.Load();
            rows = blender.Entries.Select(entry => new Row(entry.Name, entry.ThaiName ?? "", "Blender API",
                    entry.KindThai, entry.Id, entry.Kind == "operator" ? "คำสั่ง · operator" : entry.KindThai,
                    entry.Id, entry.Description, entry.ThaiDescription ?? "", entry, null))
                .Concat(zbrush.Select(tool => new Row(tool.Name, tool.ThaiName, "ZBrush-style", tool.CategoryName,
                    tool.Id, tool.StatusThai, tool.Blender, tool.Description, tool.ThaiDescription, null, tool)))
                .ToList();
            ApplyFilter();
            count.Text = $"รวม {rows.Count:N0} รายการ  ·  Blender {blender.Entries.Count:N0} (REA + runtime introspection)  ·  " +
                $"ZBrush-style {zbrush.Count:N0} (แมปกับ Blender; ไม่มี REA ของ ZBrush เพราะไม่ได้ติดตั้ง)";
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            count.Text = "อ่านคลังฟังก์ชันไม่ได้ · Could not load the function library";
            details.Text = ex.Message;
        }
    }

    private void ApplyFilter()
    {
        if (rows.Count == 0 || IsDisposed) return;
        var query = search.Text.Trim();
        var selectedSource = (source.SelectedItem as Filter)?.Value ?? "";
        visible = rows.Where(row =>
                (selectedSource.Length == 0 || (selectedSource == "blender" ? row.Entry != null : row.Tool != null))
                && (query.Length == 0
                    || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Thai.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Mapping.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.ThaiDescription.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        list.RowCount = visible.Count;
        list.Invalidate();
        if (visible.Count > 0) { list.CurrentCell = list.Rows[0].Cells[0]; SelectRow(); }
        else ClearDetails();
    }

    private void SelectRow()
    {
        if (list.CurrentCell == null || list.CurrentCell.RowIndex < 0 || list.CurrentCell.RowIndex >= visible.Count)
        { ClearDetails(); return; }
        var row = visible[list.CurrentCell.RowIndex];
        var heading = row.DisplayName;
        var text = new StringBuilder().AppendLine(heading).AppendLine();
        text.Append("Source · แหล่ง: ").AppendLine(row.Source);
        text.Append("Kind · ประเภท: ").AppendLine(row.Kind);
        text.Append("Status · สถานะ: ").AppendLine(row.Status);
        text.AppendLine().Append("ID: ").AppendLine(row.Id);
        text.Append("Blender: ").AppendLine(row.Mapping).AppendLine();
        if (!string.IsNullOrWhiteSpace(row.ThaiDescription)) text.AppendLine(row.ThaiDescription);
        if (!string.IsNullOrWhiteSpace(row.Description)) text.AppendLine().AppendLine("Original description:").AppendLine(row.Description);
        if (row.Tool?.Status == "planned")
            text.AppendLine().AppendLine("รายการนี้ยังไม่รองรับในรุ่นนี้ · This entry is not implemented yet.");
        details.Text = text.ToString();
        details.Select(0, heading.Length);
        details.SelectionFont = new Font(Font, FontStyle.Bold);
        openTool.Enabled = row.Tool?.Panel != null;
        useOperator.Enabled = row.Entry?.Kind == "operator";
    }

    private void ClearDetails()
    {
        details.Clear();
        openTool.Enabled = false;
        useOperator.Enabled = false;
    }

    private void Accept()
    {
        if (list.CurrentCell == null || list.CurrentCell.RowIndex < 0 || list.CurrentCell.RowIndex >= visible.Count) return;
        var row = visible[list.CurrentCell.RowIndex];
        if (row.Tool?.Panel is int panel) { ToolPanel = panel; DialogResult = DialogResult.OK; Close(); return; }
        if (row.Tool != null)
        {
            count.Text = $"{row.DisplayName}: ใช้เมนู File → Export Model หรือปุ่มส่งออกบนแถบด้านบน · Use File → Export Model";
            return;
        }
        if (row.Entry?.Kind == "operator") { BlenderOperator = row.Entry; DialogResult = DialogResult.OK; Close(); return; }
        if (row.Entry != null)
            count.Text = $"{row.DisplayName}: รายการข้อมูล API ไม่ใช่คำสั่ง — ใช้ได้ผ่านแท็บ Functions หรือคลัง Blender API · API data, not an operator";
    }
}
