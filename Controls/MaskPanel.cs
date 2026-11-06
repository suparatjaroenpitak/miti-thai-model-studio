using System.Globalization;
using System.Text;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>ZBrush-style masking for the selected mesh, stored as a vertex group.</summary>
public sealed class MaskPanel : UserControl
{
    private readonly Label target = Theme.Label("เลือกวัตถุก่อนใช้มาสก์", 9, Theme.Accent);
    private readonly CheckBox useMask = new() { Text = "Use mask while sculpting · ใช้มาสก์ตอนปั้น", Width = 236, Height = 28,
        ForeColor = Theme.Text, BackColor = Theme.Panel, Checked = true };
    private readonly NumericUpDown threshold = Number(0m, -100000m, 100000m, 2, 0.1m);
    private readonly ComboBox axis = Choice("Z", "Y", "X");
    private readonly NumericUpDown iterations = Number(3, 1, 40, 0, 1);
    private string? objectName;

    public MaskPanel()
    {
        BackColor = Theme.Panel;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(6, 10, 0, 0), BackColor = Theme.Panel };
        Controls.Add(flow);
        target.AutoSize = false; target.Width = 236; target.Height = 34; flow.Controls.Add(target);
        flow.Controls.Add(useMask);

        Group(flow, "Mask · มาสก์");
        ActionButton(flow, "Mask All · มาสก์ทั้งชิ้น", "value=1.0");
        ActionButton(flow, "Clear Mask · ล้างมาสก์", "clear");
        ActionButton(flow, "Invert Mask · กลับด้านมาสก์", "invert");

        Group(flow, "Feather · เบลอขอบ");
        Label(flow, "Iterations · จำนวนครั้ง"); flow.Controls.Add(iterations);
        ActionButton(flow, "Feather Mask · เบลอมาสก์", "feather");

        Group(flow, "Region · มาสก์ตามพื้นที่");
        Label(flow, "Axis · แกน"); flow.Controls.Add(axis);
        Label(flow, "Height · ระดับ (world)"); flow.Controls.Add(threshold);
        ActionButton(flow, "Mask Above · มาสก์เหนือระดับ", "above");
        ActionButton(flow, "Mask Below · มาสก์ใต้ระดับ", "below");

        var note = Theme.Label("มาสก์เก็บเป็น vertex group ชื่อ MitiMask\nเมื่อเปิด ใช้มาสก์ แปรงจะปั้นเฉพาะจุดที่ยังไม่ถูกมาสก์\nMasked areas resist every brush", 8.5f, Theme.Muted);
        note.AutoSize = false; note.Width = 236; note.Height = 64; flow.Controls.Add(note);

        useMask.CheckedChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<string>? ApplyRequested;
    public event EventHandler? SettingsChanged;
    public string? TargetObject => objectName;
    public bool UseMask => useMask.Checked;

    public void SetTargetObject(string? name)
    {
        objectName = name;
        target.Text = name == null ? "เลือกวัตถุก่อนใช้มาสก์" : "วัตถุ · Object: " + name;
    }

    private void ActionButton(FlowLayoutPanel flow, string text, string operation)
    {
        var button = Theme.Button(text, (_, _) => Apply(operation));
        button.Width = 236; button.Height = 30; button.TextAlign = ContentAlignment.MiddleLeft;
        button.Margin = new Padding(0, 2, 0, 3);
        flow.Controls.Add(button);
    }

    private void Apply(string operation)
    {
        if (objectName == null)
        {
            MessageBox.Show(this, "เลือกวัตถุก่อนใช้มาสก์\nSelect an object first.", "ยังไม่ได้เลือกวัตถุ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var name = ApiCatalogService.PythonString(objectName);
        var body = operation switch
        {
            "clear" => $"_miti_mask_clear({name})",
            "invert" => $"_miti_mask_invert({name})",
            "feather" => $"_miti_mask_feather({name}, iterations={Decimal(iterations)})",
            "above" => $"_miti_mask_set({name}, value=1.0, axis='{axis.SelectedItem}', minimum={Decimal(threshold)})",
            "below" => $"_miti_mask_set({name}, value=1.0, axis='{axis.SelectedItem}', maximum={Decimal(threshold)})",
            _ => $"_miti_mask_set({name}, value=1.0)"
        };
        var code = new StringBuilder()
            .AppendLine().Append("# มาสก์ · Mask: ").AppendLine(operation)
            .AppendLine(body)
            .AppendLine("bpy.context.view_layer.update()")
            .ToString();
        ApplyRequested?.Invoke(this, code);
    }

    private static string Decimal(NumericUpDown input) => input.Value.ToString(CultureInfo.InvariantCulture);
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals, decimal increment)
    {
        var input = new NumericUpDown { Width = 236, Value = value, Minimum = minimum, Maximum = maximum,
            DecimalPlaces = decimals, Increment = increment, BackColor = Theme.Raised, ForeColor = Theme.Text };
        Theme.UseDarkControls(input); return input;
    }
    private static ComboBox Choice(params string[] items)
    {
        var combo = new StudioComboBox { Width = 236 };
        combo.Items.AddRange(items); combo.SelectedIndex = 0; return combo;
    }
    private static void Group(FlowLayoutPanel flow, string text)
    {
        var label = Theme.Label(text, 9.5f, Theme.Accent, true);
        label.AutoSize = false; label.Width = 236; label.Height = 28;
        label.Margin = new Padding(0, 8, 0, 2);
        flow.Controls.Add(label);
    }
    private static void Label(FlowLayoutPanel flow, string text)
    {
        var label = Theme.Label(text, 8.7f, Theme.Text);
        label.AutoSize = false; label.Width = 236; label.Height = 21;
        flow.Controls.Add(label);
    }
}
