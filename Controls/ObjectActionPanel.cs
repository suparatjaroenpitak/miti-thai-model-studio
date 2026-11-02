using System.Globalization;
using System.Text;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>Searches Blender's operator inventory and edits typed parameters for a picked object.</summary>
public sealed class ObjectActionPanel : UserControl
{
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "ค้นหา เช่น แบ่งจุด / subdivide", BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly ListBox results = new() { Dock = DockStyle.Top, Height = 160, BackColor = Theme.Background, ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly FlowLayoutPanel fields = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Label target = Theme.Label("คลิกขวาที่โมเดลเพื่อเลือกวัตถุ", 9, Theme.Accent);
    private readonly Label details = Theme.Label("เลือกคำสั่งจากผลค้นหา", 8.5f, Theme.Muted);
    private readonly Label previewStatus = Theme.Label("ปรับค่าเพื่อดูผลในมุมมอง", 8.5f, Theme.Accent);
    private readonly Button apply;
    private readonly Button reset;
    private readonly ToolTip tips = new() { AutoPopDelay = 15000, InitialDelay = 300 };
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 650 };
    private readonly List<(ApiParameter parameter, Control control)> controls = [];
    private List<ApiEntry> inventory = [];
    private List<ApiEntry> matches = [];
    private ApiEntry? selected;
    private string? objectName;
    public event EventHandler<string>? ApplyRequested;
    public event EventHandler<string>? PreviewRequested;
    public event EventHandler? PreviewInvalidated;
    public event EventHandler<string?>? ActionSelected;
    public event EventHandler<int>? QuickSubdivisionChanged;
    public string? TargetObject => objectName;
    public void SetPreviewStatus(string message) => previewStatus.Text = message;

    public ObjectActionPanel()
    {
        BackColor = Theme.Panel; ForeColor = Theme.Text; Padding = new Padding(10);
        target.Dock = DockStyle.Top; target.Height = 32; target.AutoSize = false;
        details.Dock = DockStyle.Top; details.Height = 48; details.AutoSize = false;
        apply = Theme.Button("ใช้กับวัตถุและสร้างใหม่", (_, _) => Apply(), true);
        apply.Dock = DockStyle.Bottom; apply.Height = 36; apply.Enabled = false;
        reset = Theme.Button("คืนค่าพารามิเตอร์เริ่มต้น", (_, _) => SelectAction());
        reset.Dock = DockStyle.Bottom; reset.Height = 30;
        previewStatus.Dock = DockStyle.Bottom; previewStatus.Height = 34; previewStatus.AutoSize = false;
        Controls.Add(fields); Controls.Add(previewStatus); Controls.Add(reset); Controls.Add(apply); Controls.Add(details); Controls.Add(results); Controls.Add(search); Controls.Add(target);
        search.TextChanged += (_, _) => Filter(); results.SelectedIndexChanged += (_, _) => SelectAction();
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            try { if (selected != null && objectName != null) PreviewRequested?.Invoke(this, GenerateSnippet()); }
            catch (Exception) { /* Wait for a valid value; Apply will show the exact input error. */ }
        };
        Load += async (_, _) => await LoadInventoryAsync();
    }

    public void PickObject(string name)
    {
        SetTargetObject(name);
        search.Focus(); search.SelectAll(); apply.Enabled = selected != null;
    }
    public void SetTargetObject(string name)
    {
        if (objectName == name) return;
        objectName = name; target.Text = "วัตถุ: " + name;
        apply.Enabled = selected != null;
        SchedulePreview();
    }
    public IReadOnlyList<ApiEntry> Matches => matches;
    public void SetQuery(string query) => search.Text = query;
    public void Choose(ApiEntry entry)
    {
        search.Text = entry.Id;
        for (var i = 0; i < results.Items.Count; i++)
            if ((results.Items[i] as Row)?.Entry.Id == entry.Id) { results.SelectedIndex = i; break; }
        controls.FirstOrDefault().control?.Focus();
    }

    private async Task LoadInventoryAsync()
    {
        try
        {
            details.Text = "กำลังอ่านคำสั่ง Blender…";
            var catalog = await ApiCatalogService.LoadAsync();
            if (IsDisposed) return;
            inventory = catalog.Entries.Where(e => e.Kind == "operator" && IsModelingOperator(e.Id)).ToList();
            Filter();
        }
        catch (Exception ex) { details.Text = "อ่านคลังคำสั่งไม่ได้: " + ex.Message; }
    }

    private void Filter()
    {
        previewTimer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty);
        var q = search.Text.Trim();
        var normalized = q is "แบ่งจุด" or "การแบ่งจุด" or "แบ่งหน้า" ? "subdivide" : q;
        results.BeginUpdate(); results.Items.Clear(); matches = [];
        if (q.Length != 0)
        {
            matches = inventory.Where(e => e.DisplayName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                 || e.Id.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                 || e.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                 || (e.ThaiDescription?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false)).Take(80).ToList();
            foreach (var e in matches) results.Items.Add(new Row(e));
        }
        results.EndUpdate();
        if (results.Items.Count > 0) results.SelectedIndex = 0;
        else { selected = null; ActionSelected?.Invoke(this, null); fields.Controls.Clear(); apply.Enabled = false; details.Text = q.Length == 0 ? "พิมพ์ชื่อคำสั่งเพื่อค้นหา" : "ไม่พบคำสั่ง"; }
    }

    private void SelectAction()
    {
        previewTimer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty);
        selected = (results.SelectedItem as Row)?.Entry;
        ActionSelected?.Invoke(this, selected?.Id);
        controls.Clear(); fields.Controls.Clear();
        if (selected == null) { apply.Enabled = false; return; }
        details.Text = selected.DisplayName + "\n" + selected.Id;
        foreach (var p in selected.Parameters.Where(p => p.Id != "rna_type" && !p.ReadOnly))
        {
            var label = Theme.Label(FriendlyName(p) + (p.Required ? " *" : ""), 8.5f, Theme.Text);
            label.AutoSize = false; label.AutoEllipsis = true; label.Width = 190; label.Height = 22; fields.Controls.Add(label);
            Control input;
            if (p.Type == "BOOLEAN") input = new CheckBox { Text = "เปิดใช้งาน", Checked = p.Default.ValueKind == System.Text.Json.JsonValueKind.True, ForeColor = Theme.Text, Width = 190, Height = 26 };
            else if (p.Type == "ENUM" && p.EnumItems.Count > 0)
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, BackColor = Theme.Raised, ForeColor = Theme.Text };
                foreach (var option in p.EnumItems) combo.Items.Add(new Choice(option.Id, option.ThaiName ?? option.Name));
                var initial = p.Default.ValueKind == System.Text.Json.JsonValueKind.String ? p.Default.GetString() : null;
                combo.SelectedIndex = Math.Max(0, combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Id == initial)); input = combo;
            }
            else if (p.Type is "INT" or "FLOAT" && p.ArrayLength is >= 2 and <= 4 && p.Default.ValueKind == System.Text.Json.JsonValueKind.Array)
                input = new VectorEditor(p);
            else if (p.Type is "INT" or "FLOAT" && p.ArrayLength == 0)
            {
                var number = CreateNumber(p);
                var initial = p.Default.ValueKind == System.Text.Json.JsonValueKind.Number && p.Default.TryGetDecimal(out var val) ? val : 0;
                number.Value = Math.Clamp(initial, number.Minimum, number.Maximum);
                input = number;
            }
            else input = new TextBox { Text = p.Type == "STRING" && p.Default.ValueKind == System.Text.Json.JsonValueKind.String ? p.Default.GetString() ?? "" : ApiCatalogService.PythonValue(p.Default, p.Type), Width = 190, BackColor = Theme.Raised, ForeColor = Theme.Text };
            var help = p.ThaiDescription ?? p.Description;
            if (!string.IsNullOrWhiteSpace(help)) { tips.SetToolTip(label, help); tips.SetToolTip(input, help); }
            input.Margin = new Padding(0, 0, 0, 7); fields.Controls.Add(input); controls.Add((p, input));
            if (input is NumericUpDown numericInput) numericInput.ValueChanged += (_, _) =>
            {
                if (selected?.Id == "bpy.ops.mesh.subdivide" && p.Id == "number_cuts")
                    QuickSubdivisionChanged?.Invoke(this, (int)numericInput.Value);
                SchedulePreview();
            };
            else if (input is CheckBox checkbox) checkbox.CheckedChanged += (_, _) => SchedulePreview();
            else if (input is ComboBox combo) combo.SelectedIndexChanged += (_, _) => SchedulePreview();
            else if (input is TextBox text) text.TextChanged += (_, _) => SchedulePreview();
            else if (input is VectorEditor vector) vector.Changed += (_, _) => SchedulePreview();
        }
        apply.Enabled = objectName != null;
        if (selected.Id == "bpy.ops.mesh.subdivide")
        {
            var cuts = controls.FirstOrDefault(c => c.parameter.Id == "number_cuts").control as NumericUpDown;
            if (cuts != null) QuickSubdivisionChanged?.Invoke(this, (int)cuts.Value);
        }
        SchedulePreview();
    }

    private void Apply()
    {
        if (selected == null || objectName == null) return;
        try
        {
            previewTimer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty);
            ApplyRequested?.Invoke(this, GenerateSnippet());
        }
        catch (Exception ex) { MessageBox.Show(this, "ค่าพารามิเตอร์ไม่ถูกต้อง: " + ex.Message, "ตรวจค่า", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void SchedulePreview()
    {
        previewTimer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty);
        if (selected != null && objectName != null)
        {
            previewStatus.Text = selected.Id == "bpy.ops.mesh.subdivide" ? "เส้นร่างทันที · รอ Blender คำนวณจริง…" : "รอ Blender อัปเดตโมเดล…";
            previewTimer.Start();
        }
    }

    public static bool IsModelingOperator(string id)
    {
        var path = id.StartsWith("bpy.ops.") ? id[8..] : id;
        return path.StartsWith("mesh.", StringComparison.Ordinal) || path.StartsWith("object.", StringComparison.Ordinal)
            || path.StartsWith("transform.", StringComparison.Ordinal) || path.StartsWith("uv.", StringComparison.Ordinal);
    }

    private string GenerateSnippet()
    {
        if (selected == null || objectName == null) throw new InvalidOperationException("ยังไม่ได้เลือกวัตถุหรือคำสั่ง");
            var args = new List<string>();
            foreach (var (p, control) in controls)
            {
                string value;
                if (control is CheckBox checkbox) value = checkbox.Checked ? "True" : "False";
                else if (control is ComboBox combo) value = ApiCatalogService.PythonString(((Choice)combo.SelectedItem!).Id);
                else if (control is NumericUpDown number) value = number.Value.ToString(CultureInfo.InvariantCulture);
                else if (control is VectorEditor vector) value = vector.PythonValue;
                else
                {
                    var raw = ((TextBox)control).Text.Trim();
                    if (p.Type == "INT") value = int.Parse(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    else if (p.Type == "FLOAT") value = double.Parse(raw, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
                    else if (p.Type == "STRING") value = ApiCatalogService.PythonString(raw);
                    else value = raw;
                }
                if (p.Required || !string.Equals(value, ApiCatalogService.PythonValue(p.Default, p.Type), StringComparison.Ordinal))
                    args.Add(p.Id + "=" + value);
            }
            var op = selected.Id.StartsWith("bpy.ops.") ? selected.Id[8..] : selected.Id;
            var category = op.Split('.')[0];
            var mode = category is "mesh" or "uv" ? "EDIT" : "OBJECT";
            var code = new StringBuilder().Append("\n# ปรับวัตถุ: ").Append(selected.DisplayName).AppendLine()
                .Append("_miti_obj = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(objectName)).AppendLine(")")
                .AppendLine("if _miti_obj is None: raise ValueError('ไม่พบวัตถุที่เลือก กรุณาสร้างโมเดลใหม่ก่อน')")
                .AppendLine("if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')")
                .AppendLine("bpy.ops.object.select_all(action='DESELECT')")
                .AppendLine("_miti_obj.select_set(True)")
                .AppendLine("bpy.context.view_layer.objects.active = _miti_obj");
            if (mode == "EDIT") code.AppendLine("bpy.ops.object.mode_set(mode='EDIT')").AppendLine("bpy.ops.mesh.select_all(action='SELECT')");
            code.Append("คำสั่ง(").Append(ApiCatalogService.PythonString(op));
            foreach (var arg in args) code.Append(", ").Append(arg);
            code.AppendLine(")");
            if (mode == "EDIT") code.AppendLine("bpy.ops.object.mode_set(mode='OBJECT')");
            return code.ToString();
    }
    private sealed record Row(ApiEntry Entry) { public override string ToString() => Entry.DisplayName + " · " + Entry.Id; }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }

    private static string FriendlyName(ApiParameter p) => p.Id switch
    {
        "number_cuts" => "จำนวนครั้งที่แบ่ง",
        "smoothness" => "ความเรียบ",
        "offset" => "ระยะ",
        "segments" => "จำนวนขั้น",
        "width" => "ความกว้าง",
        "factor" => "สัดส่วน",
        _ => p.DisplayName
    };

    private static NumericUpDown CreateNumber(ApiParameter p)
    {
        var number = new NumericUpDown { Width = 190, BackColor = Theme.Raised, ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle, DecimalPlaces = p.Type == "INT" ? 0 : 3,
            Increment = p.Type == "INT" ? 1 : 0.1m, Minimum = -1_000_000_000, Maximum = 1_000_000_000 };
        if (p.Minimum.ValueKind == System.Text.Json.JsonValueKind.Number && p.Minimum.TryGetDouble(out var min) && double.IsFinite(min))
            number.Minimum = (decimal)Math.Clamp(min, -1_000_000_000, 1_000_000_000);
        if (p.Maximum.ValueKind == System.Text.Json.JsonValueKind.Number && p.Maximum.TryGetDouble(out var max) && double.IsFinite(max))
            number.Maximum = (decimal)Math.Clamp(max, (double)number.Minimum, 1_000_000_000);
        return number;
    }

    private sealed class VectorEditor : FlowLayoutPanel
    {
        private readonly List<NumericUpDown> values = [];
        public event EventHandler? Changed;
        public VectorEditor(ApiParameter p)
        {
            Width = 190; Height = 32; WrapContents = false; BackColor = Theme.Panel;
            var defaults = p.Default.EnumerateArray().ToArray();
            for (int i = 0; i < p.ArrayLength; i++)
            {
                Controls.Add(new Label { Text = "XYZW"[i].ToString(), Width = 14, Height = 24, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 0) });
                var n = CreateNumber(p); n.Width = p.ArrayLength == 4 ? 31 : 44; n.Margin = new Padding(0, 0, 2, 0);
                var initial = i < defaults.Length && defaults[i].TryGetDecimal(out var val) ? val : 0;
                n.Value = Math.Clamp(initial, n.Minimum, n.Maximum);
                n.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
                Controls.Add(n); values.Add(n);
            }
        }
        public string PythonValue => "(" + string.Join(", ", values.Select(v => v.Value.ToString(CultureInfo.InvariantCulture))) + ")";
    }
}
