using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>Surface material editing for the selected object, executed in Blender.</summary>
public sealed class MaterialPanel : UserControl
{
    private readonly Label target = Theme.Label("เลือกวัตถุก่อนใส่วัสดุ", 9, Theme.Accent);
    private readonly TextBox name = new() { Width = 236, Text = "วัสดุใหม่", BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly TextBox hex = new() { Width = 150, Text = "#5BE2BA", BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly NumericUpDown metallic = Number(0m, 0m, 1m, 2, 0.05m);
    private readonly NumericUpDown roughness = Number(0.45m, 0m, 1m, 2, 0.05m);
    private readonly CheckBox emission = new() { Text = "Emission · เรืองแสง", Width = 236, ForeColor = Theme.Text, BackColor = Theme.Panel };
    private readonly NumericUpDown emissionStrength = Number(1.5m, 0m, 100m, 1, 0.1m);
    private readonly Button color;
    private string? objectName;

    public MaterialPanel()
    {
        BackColor = Theme.Panel;
        color = Theme.Button("Pick Colour · เลือกสี", (_, _) => PickColor());
        color.Width = 80; color.Height = 28; color.Padding = new Padding(4, 0, 4, 0);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(6, 10, 0, 0), BackColor = Theme.Panel };
        Controls.Add(flow);
        target.AutoSize = false; target.Width = 236; target.Height = 34; flow.Controls.Add(target);

        Group(flow, "Material · วัสดุ");
        Label(flow, "Name · ชื่อวัสดุ"); flow.Controls.Add(name);
        Label(flow, "Base Colour · สีหลัก");
        var colorRow = new FlowLayoutPanel { Width = 236, Height = 32, WrapContents = false, Margin = Padding.Empty };
        Theme.UseDarkControls(hex);
        colorRow.Controls.Add(hex); colorRow.Controls.Add(color); flow.Controls.Add(colorRow);
        Label(flow, "Metallic · ความเป็นโลหะ"); flow.Controls.Add(metallic);
        Label(flow, "Roughness · ความหยาบ"); flow.Controls.Add(roughness);

        Group(flow, "Presets · วัสดุสำเร็จรูป");
        Preset(flow, "Clay · ดินปั้น", "#B08968", 0m, 0.85m);
        Preset(flow, "Skin · ผิวหนัง", "#E8B4A0", 0m, 0.6m);
        Preset(flow, "Metal · โลหะ", "#C8CDD4", 1m, 0.25m);
        Preset(flow, "Gold · ทอง", "#D4AF37", 1m, 0.3m);
        Preset(flow, "Plastic · พลาสติก", "#5BE2BA", 0m, 0.45m);

        Group(flow, "Glow · เรืองแสง");
        flow.Controls.Add(emission);
        Label(flow, "Strength · ความแรง"); flow.Controls.Add(emissionStrength);

        var apply = Theme.Button("Apply Material · ใช้วัสดุ", (_, _) => Apply(), true);
        apply.Width = 236; apply.Height = 34; apply.Margin = new Padding(0, 12, 0, 4); flow.Controls.Add(apply);
        var note = Theme.Label("วัสดุจะแทนที่ช่องวัสดุเดิมของวัตถุที่เลือก\nReplaces the selected object's material slots", 8.5f, Theme.Muted);
        note.AutoSize = false; note.Width = 236; note.Height = 44; flow.Controls.Add(note);
    }

    public event EventHandler<string>? ApplyRequested;
    public string? TargetObject => objectName;

    public void SetTargetObject(string? value)
    {
        objectName = value;
        target.Text = value == null ? "เลือกวัตถุก่อนใส่วัสดุ" : "วัตถุ · Object: " + value;
    }

    private void PickColor()
    {
        var current = ParseColor(hex.Text) ?? Color.FromArgb(91, 226, 186);
        using var dialog = new ColorDialog { FullOpen = true, Color = current, AnyColor = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        hex.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
    }

    private void Preset(FlowLayoutPanel flow, string label, string value, decimal metallicValue, decimal roughnessValue)
    {
        var button = Theme.Button(label, (_, _) =>
        {
            hex.Text = value; metallic.Value = metallicValue; roughness.Value = roughnessValue;
            name.Text = label.Split('·')[0].Trim();
        });
        button.Width = 236; button.Height = 28; button.TextAlign = ContentAlignment.MiddleLeft;
        flow.Controls.Add(button);
    }

    private void Apply()
    {
        if (objectName == null)
        {
            MessageBox.Show(this, "เลือกวัตถุก่อนใส่วัสดุ\nSelect an object first.", "ยังไม่ได้เลือกวัตถุ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Regex.IsMatch(hex.Text.Trim(), "^#?[0-9A-Fa-f]{6}$"))
        {
            MessageBox.Show(this, "สีต้องอยู่ในรูป #RRGGBB\nColour must be #RRGGBB.", "ตรวจสี", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var materialName = name.Text.Trim();
        if (materialName.Length == 0) materialName = "วัสดุ";
        var rgb = ParseColor(hex.Text)!.Value;
        var linear = new[] { Channel(rgb.R), Channel(rgb.G), Channel(rgb.B) };
        var code = new StringBuilder()
            .AppendLine().Append("# วัสดุ · Material: ").AppendLine(materialName)
            .Append("_miti_obj = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(objectName)).AppendLine(")")
            .AppendLine("if _miti_obj is None: raise ValueError('ไม่พบวัตถุที่เลือก · Selected object is missing')")
            .Append("_miti_mat = วัสดุ(").Append(ApiCatalogService.PythonString(materialName)).Append(", ")
            .Append(ApiCatalogService.PythonString(hex.Text.Trim())).Append(", roughness=").Append(Decimal(roughness))
            .Append(", metallic=").Append(Decimal(metallic)).AppendLine(")")
            .AppendLine("_miti_obj.data.materials.clear()")
            .AppendLine("_miti_obj.data.materials.append(_miti_mat)");
        if (emission.Checked)
        {
            code.AppendLine("_miti_node = next((n for n in _miti_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)")
                .AppendLine("if _miti_node is not None:")
                .AppendLine($"    _miti_node.inputs['Emission Color'].default_value = ({Number(linear[0])}, {Number(linear[1])}, {Number(linear[2])}, 1.0)")
                .Append("    _miti_node.inputs['Emission Strength'].default_value = ").AppendLine(Decimal(emissionStrength));
        }
        code.AppendLine("bpy.context.view_layer.update()");
        ApplyRequested?.Invoke(this, code.ToString());
    }

    private static float Channel(byte value)
    {
        var channel = value / 255f;
        return channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);
    }
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Decimal(NumericUpDown input) => input.Value.ToString(CultureInfo.InvariantCulture);
    private static Color? ParseColor(string text)
    {
        var value = text.Trim().TrimStart('#');
        if (value.Length != 6 || !int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return null;
        return Color.FromArgb((packed >> 16) & 0xFF, (packed >> 8) & 0xFF, packed & 0xFF);
    }
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals, decimal increment)
    {
        var input = new NumericUpDown { Width = 236, Value = value, Minimum = minimum, Maximum = maximum,
            DecimalPlaces = decimals, Increment = increment, BackColor = Theme.Raised, ForeColor = Theme.Text };
        Theme.UseDarkControls(input); return input;
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
