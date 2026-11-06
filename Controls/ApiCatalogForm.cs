using System.Text;

namespace ThaiModelStudio.Controls;

public sealed class ApiCatalogForm : Form
{
    private readonly Func<string?> blenderPath;
    private readonly TextBox search = new() { PlaceholderText = "Search · ค้นหาภาษาไทย อังกฤษ หรือรหัส API…", Dock = DockStyle.Fill };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly DataGridView list = Grid(true);
    private readonly DataGridView parameters = Grid(false);
    private readonly RichTextBox details = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = new Font("Leelawadee UI", 10) };
    private readonly TextBox target = new() { Text = "เป้าหมาย", Dock = DockStyle.Fill };
    private readonly TextBox positional = new() { Dock = DockStyle.Fill, PlaceholderText = "อาร์กิวเมนต์ตามตำแหน่ง เช่น (0, 0, 0)" };
    private readonly TextBox code = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Theme.Background, ForeColor = Theme.Accent };
    private readonly Label count = Theme.Label("กำลังอ่านคลังฟังก์ชัน…", 9, Theme.Muted);
    private readonly Button insert;
    private readonly Button scan;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 220 };
    private ApiCatalog? catalog;
    private List<ApiEntry> visible = [];
    private ApiEntry? selected;
    private CancellationTokenSource? scanning;
    private bool filling;
    public string? SelectedCode { get; private set; }
    public ApiCatalogForm(Func<string?> getBlenderPath)
    {
        blenderPath = getBlenderPath;
        Text = "Blender API · คลังฟังก์ชัน Blender"; BackColor = Theme.Background; ForeColor = Theme.Text;
        Size = new Size(1370, 900); MinimumSize = new Size(1020, 700); StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Leelawadee UI", 10); Padding = new Padding(14);
        insert = Theme.Button("Insert Code · แทรกโค้ด", (_, _) => { if (selected == null) return; SelectedCode = code.Text + "\n"; DialogResult = DialogResult.OK; Close(); }, true);
        insert.Enabled = false;
        scan = Theme.Button("Rescan · สแกน Blender", async (_, _) => await ScanAsync());
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 46, ColumnCount = 4, RowCount = 1 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        foreach (var c in new Control[] { search, kind, category }) { c.BackColor = Theme.Raised; c.ForeColor = Theme.Text; c.Margin = new Padding(0, 2, 10, 4); }
        kind.FlatStyle = FlatStyle.Flat; category.FlatStyle = FlatStyle.Flat;
        top.Controls.Add(search, 0, 0); top.Controls.Add(kind, 1, 0); top.Controls.Add(category, 2, 0); top.Controls.Add(scan, 3, 0);
        kind.Items.AddRange([new Filter("ทุกประเภท", ""), new Filter("คำสั่ง", "operator"), new Filter("เมธอด", "function"), new Filter("คุณสมบัติ", "property"), new Filter("ชนิดข้อมูล", "type"), new Filter("ฟังก์ชันโมดูล", "moduleFunction")]); kind.SelectedIndex = 0;
        category.Items.Add(new Filter("ทุกหมวด", "")); category.SelectedIndex = 0;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 6, 0, 0) };
        footer.Controls.Add(insert); footer.Controls.Add(Theme.Button("Close · ปิด", (_, _) => Close()));
        var info = new Panel { Dock = DockStyle.Bottom, Height = 43, Padding = new Padding(0, 10, 0, 0) }; count.Dock = DockStyle.Fill; info.Controls.Add(count);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1300, 650), SplitterDistance = 570, BackColor = Theme.Border, SplitterWidth = 5 };
        list.Columns.Add("name", "Name · ชื่อ"); list.Columns.Add("kind", "Kind · ประเภท"); list.Columns.Add("identity", "API ID · รหัสอ้างอิง");
        list.Columns[0].FillWeight = 48; list.Columns[1].FillWeight = 17; list.Columns[2].FillWeight = 35;
        list.VirtualMode = true;
        list.CellValueNeeded += (_, e) => { if (e.RowIndex >= visible.Count) return; var item = visible[e.RowIndex]; e.Value = e.ColumnIndex switch { 0 => ApiCatalogService.BilingualName(item.Name, item.ThaiName), 1 => item.KindThai, _ => item.Id }; };
        list.CurrentCellChanged += (_, _) => SelectEntry();
        split.Panel1.Controls.Add(list);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Panel, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); right.RowStyles.Add(new RowStyle(SizeType.Absolute, 23)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        right.Controls.Add(details, 0, 0);
        right.Controls.Add(Theme.Label("เป้าหมายสำหรับเมธอด/คุณสมบัติ — ระบุตัวแปรจากโค้ด", 8.5F, Theme.Muted), 0, 1);
        target.BackColor = Theme.Raised; target.ForeColor = Theme.Text; right.Controls.Add(target, 0, 2);
        positional.BackColor = Theme.Raised; positional.ForeColor = Theme.Text; right.Controls.Add(positional, 0, 3);
        parameters.Columns.Add(new DataGridViewCheckBoxColumn { Name = "include", HeaderText = "Use · ใช้", FillWeight = 12 });
        parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Parameter · พารามิเตอร์", ReadOnly = true, FillWeight = 40 });
        parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Value · ค่าในโค้ด Python", FillWeight = 32 });
        parameters.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = "Type · ชนิด", ReadOnly = true, FillWeight = 16 });
        parameters.CurrentCellDirtyStateChanged += (_, _) => { if (parameters.IsCurrentCellDirty) parameters.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        parameters.CellValueChanged += (_, _) => GenerateCode();
        parameters.DataError += (_, e) => e.ThrowException = false;
        right.Controls.Add(parameters, 0, 4); right.Controls.Add(Theme.Label("โค้ดที่จะเพิ่ม — การเรียกใช้ขึ้นกับวัตถุ โหมด และบริบทของ Blender", 8.5F, Theme.Muted), 0, 5); right.Controls.Add(code, 0, 6);
        split.Panel2.Controls.Add(right);
        Controls.Add(split); Controls.Add(info); Controls.Add(footer); Controls.Add(top);
        search.TextChanged += (_, _) => { debounce.Stop(); debounce.Start(); };
        debounce.Tick += (_, _) => { debounce.Stop(); ApplyFilter(); };
        kind.SelectedIndexChanged += (_, _) => ApplyFilter(); category.SelectedIndexChanged += (_, _) => ApplyFilter();
        target.TextChanged += (_, _) => GenerateCode(); positional.TextChanged += (_, _) => GenerateCode();
        Shown += async (_, _) => await LoadCatalogAsync();
        FormClosed += (_, _) => { scanning?.Cancel(); debounce.Dispose(); };
    }

    private static DataGridView Grid(bool readOnly)
    {
        var g = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Theme.Panel, BorderStyle = BorderStyle.None,
            ForeColor = Theme.Text, ReadOnly = readOnly, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = readOnly ? DataGridViewSelectionMode.FullRowSelect : DataGridViewSelectionMode.CellSelect,
            MultiSelect = false, EnableHeadersVisualStyles = false, GridColor = Theme.Border, RowTemplate = { Height = 31 } };
        g.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Theme.Raised, SelectionForeColor = Theme.Accent, Padding = new Padding(3) };
        g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Raised, ForeColor = Theme.Muted, Padding = new Padding(3) };
        return g;
    }
    private async Task LoadCatalogAsync()
    {
        try
        {
            catalog = await ApiCatalogService.LoadAsync();
            category.Items.Clear(); category.Items.Add(new Filter("ทุกหมวด", ""));
            category.Items.Add(new Filter("ชนิดข้อมูลและคุณสมบัติ RNA", "@rna"));
            foreach (var group in catalog.Entries.Where(e => e.Kind is "operator" or "moduleFunction").GroupBy(e => e.Category).OrderBy(g => g.Key))
                category.Items.Add(new Filter(group.First().ThaiCategory ?? CategoryThai(group.Key), group.Key));
            category.SelectedIndex = 0; ApplyFilter();
        }
        catch (Exception ex) { count.Text = "ยังไม่มีคลัง API — กดสแกน Blender ใหม่"; details.Text = ex.Message; }
    }
    private void ApplyFilter()
    {
        if (catalog == null || IsDisposed) return;
        var query = search.Text.Trim(); var filter = (kind.SelectedItem as Filter)?.Value ?? ""; var group = (category.SelectedItem as Filter)?.Value ?? "";
        visible = catalog.Entries.Where(e => (filter.Length == 0 || e.Kind == filter) && (group.Length == 0 || (group == "@rna" ? e.Id.StartsWith("bpy.types.") : e.Category == group))
            && (query.Length == 0 || e.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (e.ThaiDescription?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderBy(e => e.Kind == "operator" ? 0 : e.Kind == "function" ? 1 : 2).ThenBy(e => e.DisplayName).ToList();
        list.RowCount = visible.Count;
        count.Text = $"แสดง {visible.Count:N0} จาก {catalog.Entries.Count:N0} รายการ  ·  {catalog.Entries.Count(e => e.Kind == "operator"):N0} คำสั่ง  ·  ชื่อภาษาไทยอ้างอิง API เดิม";
        list.Invalidate();
        if (visible.Count > 0) { list.CurrentCell = list.Rows[0].Cells[0]; SelectEntry(); }
        else { selected = null; details.Clear(); parameters.Rows.Clear(); code.Clear(); insert.Enabled = false; }
    }
    private void SelectEntry()
    {
        if (list.CurrentCell == null || list.CurrentCell.RowIndex >= visible.Count) return;
        selected = visible[list.CurrentCell.RowIndex]; filling = true;
        try
        {
            parameters.Rows.Clear(); positional.Clear();
            var heading = ApiCatalogService.BilingualName(selected.Name, selected.ThaiName);
            var text = new StringBuilder().AppendLine(heading).AppendLine(selected.KindThai + "  ·  " + ApiCatalogService.TranslationThai(selected.NameTranslationSource)).AppendLine(selected.Id).AppendLine();
            if (!string.IsNullOrWhiteSpace(selected.ThaiDescription)) text.AppendLine(selected.ThaiDescription);
            if (selected.DescriptionTranslationSource == "generated-summary") text.AppendLine("(สรุปภาษาไทยจากชื่อและชนิดข้อมูล)");
            if (!string.IsNullOrWhiteSpace(selected.Description)) text.AppendLine().AppendLine("คำอธิบายต้นฉบับ:").AppendLine(selected.Description);
            if (selected.PollInFactoryContext.HasValue) text.AppendLine().AppendLine(selected.PollInFactoryContext.Value ? "ผ่านการตรวจบริบทเริ่มต้นของ Blender" : "ต้องเตรียมวัตถุ/โหมด/พื้นที่ทำงานให้ตรงกับคำสั่ง");
            if (selected.ReadOnly) text.AppendLine("คุณสมบัตินี้อ่านอย่างเดียว");
            details.Text = text.ToString(); details.Select(0, heading.Length); details.SelectionFont = new Font(Font, FontStyle.Bold);
            var typeName = selected.OwnerType?.Split('.').Last() ?? (selected.Id.StartsWith("bpy.types.") ? selected.Id.Split('.')[2] : "");
            target.Text = typeName == "Object" ? "เบลนเดอร์.บริบท.วัตถุ" : typeName == "Mesh" ? "เบลนเดอร์.บริบท.วัตถุ.ข้อมูล" : "เป้าหมาย";
            target.Enabled = selected.Kind is "function" or "property";
            positional.Enabled = selected.Kind is "function" or "moduleFunction" or "type";
            if (selected.Kind == "property" && !selected.ReadOnly)
            {
                var value = ApiCatalogService.PythonValue(selected.Default, selected.Type ?? "");
                if (selected.Type == "ENUM" && selected.Default.ValueKind == System.Text.Json.JsonValueKind.Array) value = SetLiteral(selected.Default);
                parameters.Rows.Add(true, ApiCatalogService.BilingualName(selected.Name, selected.ThaiName), value, ApiCatalogService.DataTypeThai(selected.Type ?? ""));
            }
            else foreach (var p in selected.Parameters.Where(p => p.Id != "rna_type" && !p.ReadOnly))
            {
                var value = ApiCatalogService.PythonValue(p.Default, p.Type);
                if (p.Type == "ENUM" && p.Default.ValueKind == System.Text.Json.JsonValueKind.Array) value = SetLiteral(p.Default);
                var englishName = (string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name) + (p.Required ? " *" : "");
                var row = parameters.Rows[parameters.Rows.Add(p.Required, ApiCatalogService.BilingualName(englishName, p.ThaiName), value, ApiCatalogService.DataTypeThai(p.Type))];
                row.Tag = p;
                row.Cells[1].ToolTipText = p.Id + "\n" + ApiCatalogService.BilingualName(p.Description, p.ThaiDescription, "\n");
                if (p.Type == "ENUM" && p.EnumItems.Count > 0 && p.Default.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    var choices = p.EnumItems.Select(e => new EnumChoice(ApiCatalogService.PythonString(e.Id), ApiCatalogService.BilingualName(e.Name, e.ThaiName))).ToArray();
                    var combo = new DataGridViewComboBoxCell { DataSource = choices, DisplayMember = "Label", ValueMember = "Value", DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton, FlatStyle = FlatStyle.Flat };
                    var existing = row.Cells[2].Value?.ToString(); row.Cells[2] = combo;
                    combo.Value = choices.Any(c => c.Value == existing) ? existing : choices[0].Value;
                }
            }
        }
        finally { filling = false; }
        GenerateCode(); insert.Enabled = true;
    }
    private void GenerateCode()
    {
        if (filling || selected == null) return;
        var args = new List<string>();
        if (positional.Enabled && positional.Text.Trim().Length > 0) args.Add(positional.Text.Trim());
        foreach (DataGridViewRow row in parameters.Rows)
        {
            if (row.Cells[0].Value is not true) continue;
            if (row.Tag is ApiParameter p)
            {
                var value = row.Cells[2].Value?.ToString() ?? "None";
                args.Add(p.ParameterKind == "POSITIONAL_ONLY" ? value : ApiCatalogService.ParameterKey(p) + "=" + value);
            }
        }
        var argumentText = args.Count == 0 ? "" : ", " + string.Join(", ", args);
        var q = ApiCatalogService.PythonString;
        var destination = string.IsNullOrWhiteSpace(target.Text) ? "เป้าหมาย" : target.Text.Trim();
        var method = (selected.ApiPath ?? selected.Id).Split('.').Last();
        var preamble = selected.Kind is "function" or "property" && destination == "เป้าหมาย" ? "# กำหนดตัวแปร เป้าหมาย เป็นวัตถุชนิด " + (selected.OwnerType ?? selected.Id.Split('.').ElementAtOrDefault(2)) + " ก่อนเรียก\n" : "";
        code.Text = preamble + (selected.Kind switch
        {
            "operator" => "คำสั่ง(" + q(string.IsNullOrWhiteSpace(selected.ThaiPath) ? selected.Id : selected.ThaiPath) + argumentText + ")",
            "function" => "ผลลัพธ์ = เรียกฟังก์ชัน(" + destination + ", " + q(selected.ThaiIdentifier ?? method) + argumentText + ")",
            "property" when selected.ReadOnly => "ค่า = อ่านค่า(" + destination + ", " + q(selected.ThaiIdentifier ?? method) + ")",
            "property" => "ตั้งค่า(" + destination + ", **{" + q(selected.ThaiIdentifier ?? method) + ": " + (parameters.Rows.Count > 0 ? parameters.Rows[0].Cells[2].Value?.ToString() ?? "None" : "None") + "})",
            "type" when args.Count == 0 => "ชนิดข้อมูล = อ่านค่า(เบลนเดอร์.ชนิด, " + q(selected.ThaiIdentifier ?? selected.Id.Split('.').Last()) + ")",
            _ => "ผลลัพธ์ = เรียกAPI(" + q(selected.ThaiPath ?? selected.Id) + argumentText + ")"
        });
    }
    private static string SetLiteral(System.Text.Json.JsonElement value) => value.GetArrayLength() == 0 ? "set()"
        : "{" + string.Join(", ", value.EnumerateArray().Select(v => ApiCatalogService.PythonValue(v))) + "}";
    private async Task ScanAsync()
    {
        if (scanning != null) { scanning.Cancel(); return; }
        var path = blenderPath(); if (path == null) { details.Text = "เลือกตำแหน่ง Blender ในเมนูตั้งค่าของหน้าต่างหลักก่อน"; return; }
        using var cts = new CancellationTokenSource(); scanning = cts;
        scan.Text = "หยุดสแกน"; insert.Enabled = false;
        try
        {
            count.Text = "กำลังสำรวจ API จาก Blender…";
            await ApiCatalogService.ScanAsync(path, new Progress<string>(line => { if (!IsDisposed) count.Text = line; }), cts.Token);
            await LoadCatalogAsync();
        }
        catch (OperationCanceledException) { if (!IsDisposed) count.Text = "หยุดสแกนแล้ว — คลังเดิมยังใช้งานได้"; }
        catch (Exception ex) { if (!IsDisposed) details.Text = ex.Message; }
        finally { scanning = null; if (!IsDisposed) { scan.Text = "สแกน Blender ใหม่"; insert.Enabled = selected != null; } }
    }
    private static string CategoryThai(string key) => key switch
    {
        "mesh" => "ตาข่าย", "object" => "วัตถุ", "transform" => "แปลงรูป", "sculpt" => "ปั้น", "paint" => "ลงสี",
        "anim" or "action" => "แอนิเมชัน", "armature" or "pose" => "กระดูกและท่าทาง", "uv" => "ยูวี", "node" => "โหนด",
        "render" => "เรนเดอร์", "material" => "วัสดุ", "scene" => "ฉาก", "wm" => "หน้าต่างและไฟล์", "curve" => "เส้นโค้ง",
        "geometry" => "เรขาคณิต", "image" => "ภาพ", "collection" => "กลุ่มวัตถุ", _ => key
    };
    private sealed record Filter(string Label, string Value) { public override string ToString() => Label; }
    private sealed record EnumChoice(string Value, string Label);
}
