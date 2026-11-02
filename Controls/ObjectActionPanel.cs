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
    private readonly Button apply;
    private readonly List<(ApiParameter parameter, Control control)> controls = [];
    private List<ApiEntry> inventory = [];
    private List<ApiEntry> matches = [];
    private ApiEntry? selected;
    private string? objectName;
    public event EventHandler<string>? ApplyRequested;

    public ObjectActionPanel()
    {
        BackColor = Theme.Panel; ForeColor = Theme.Text; Padding = new Padding(10);
        target.Dock = DockStyle.Top; target.Height = 32; target.AutoSize = false;
        details.Dock = DockStyle.Top; details.Height = 48; details.AutoSize = false;
        apply = Theme.Button("ใช้กับวัตถุและสร้างใหม่", (_, _) => Apply(), true);
        apply.Dock = DockStyle.Bottom; apply.Height = 36; apply.Enabled = false;
        Controls.Add(fields); Controls.Add(apply); Controls.Add(details); Controls.Add(results); Controls.Add(search); Controls.Add(target);
        search.TextChanged += (_, _) => Filter(); results.SelectedIndexChanged += (_, _) => SelectAction();
        Load += async (_, _) => await LoadInventoryAsync();
    }

    public void PickObject(string name)
    {
        objectName = name; target.Text = "วัตถุ: " + name;
        search.Focus(); search.SelectAll(); apply.Enabled = selected != null;
    }
    public IReadOnlyList<ApiEntry> Matches => matches;
    public void SetQuery(string query) => search.Text = query;
    public void Choose(ApiEntry entry)
    {
        search.Text = entry.Id;
        for (var i = 0; i < results.Items.Count; i++)
            if ((results.Items[i] as Row)?.Entry.Id == entry.Id) { results.SelectedIndex = i; break; }
        fields.Controls.OfType<TextBox>().FirstOrDefault()?.Focus();
    }

    private async Task LoadInventoryAsync()
    {
        try
        {
            details.Text = "กำลังอ่านคำสั่ง Blender…";
            var catalog = await ApiCatalogService.LoadAsync();
            if (IsDisposed) return;
            inventory = catalog.Entries.Where(e => e.Kind == "operator").ToList();
            Filter();
        }
        catch (Exception ex) { details.Text = "อ่านคลังคำสั่งไม่ได้: " + ex.Message; }
    }

    private void Filter()
    {
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
        else { selected = null; fields.Controls.Clear(); apply.Enabled = false; details.Text = q.Length == 0 ? "พิมพ์ชื่อคำสั่งเพื่อค้นหา" : "ไม่พบคำสั่ง"; }
    }

    private void SelectAction()
    {
        selected = (results.SelectedItem as Row)?.Entry;
        controls.Clear(); fields.Controls.Clear();
        if (selected == null) { apply.Enabled = false; return; }
        details.Text = selected.DisplayName + "\n" + selected.Id;
        foreach (var p in selected.Parameters.Where(p => p.Id != "rna_type" && !p.ReadOnly))
        {
            var label = Theme.Label(p.DisplayName + (p.Required ? " *" : ""), 8.5f, Theme.Text);
            label.AutoSize = false; label.Width = 190; label.Height = 22; fields.Controls.Add(label);
            Control input;
            if (p.Type == "BOOLEAN") input = new CheckBox { Checked = p.Default.ValueKind == System.Text.Json.JsonValueKind.True, ForeColor = Theme.Text, Width = 190, Height = 26 };
            else if (p.Type == "ENUM" && p.EnumItems.Count > 0)
            {
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, BackColor = Theme.Raised, ForeColor = Theme.Text };
                foreach (var option in p.EnumItems) combo.Items.Add(new Choice(option.Id, option.ThaiName ?? option.Name));
                var initial = p.Default.ValueKind == System.Text.Json.JsonValueKind.String ? p.Default.GetString() : null;
                combo.SelectedIndex = Math.Max(0, combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Id == initial)); input = combo;
            }
            else input = new TextBox { Text = ApiCatalogService.PythonValue(p.Default, p.Type), Width = 190, BackColor = Theme.Raised, ForeColor = Theme.Text };
            input.Margin = new Padding(0, 0, 0, 7); fields.Controls.Add(input); controls.Add((p, input));
        }
        apply.Enabled = objectName != null;
    }

    private void Apply()
    {
        if (selected == null || objectName == null) return;
        try
        {
            var args = new List<string>();
            foreach (var (p, control) in controls)
            {
                string value;
                if (control is CheckBox checkbox) value = checkbox.Checked ? "True" : "False";
                else if (control is ComboBox combo) value = ApiCatalogService.PythonString(((Choice)combo.SelectedItem!).Id);
                else
                {
                    var raw = ((TextBox)control).Text.Trim();
                    if (p.Type == "INT") value = int.Parse(raw, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    else if (p.Type == "FLOAT") value = double.Parse(raw, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
                    else if (p.Type == "STRING") value = ApiCatalogService.PythonString(raw.Trim('"', '\''));
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
            ApplyRequested?.Invoke(this, code.ToString());
        }
        catch (Exception ex) { MessageBox.Show(this, "ค่าพารามิเตอร์ไม่ถูกต้อง: " + ex.Message, "ตรวจค่า", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private sealed record Row(ApiEntry Entry) { public override string ToString() => Entry.DisplayName + " · " + Entry.Id; }
    private sealed record Choice(string Id, string Label) { public override string ToString() => Label; }
}
