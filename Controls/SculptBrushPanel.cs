using System.Globalization;
using System.Numerics;
using System.Text;

namespace ThaiModelStudio.Controls;

/// <summary>Brush controls for strokes drawn directly on the model viewport.</summary>
public sealed class SculptBrushPanel : UserControl
{
    private sealed record BrushChoice(string Id, string Label) { public override string ToString() => Label; }
    private readonly ComboBox brush = new StudioComboBox { Width = 220 };
    private readonly NumericUpDown radius = Number(0.8m, 0.01m, 100m);
    private readonly NumericUpDown strength = Number(0.5m, 0.01m, 1m);
    private readonly NumericUpDown detail = Number(3, 0, 8, 0);
    private readonly Button toggle;
    private readonly Label hint = Theme.Label("เลือกหัวแปรง แล้วเปิดโหมดปั้น", 9, Theme.Muted);
    private bool active;
    public event EventHandler? SettingsChanged;
    public bool Active => active;
    public float Radius => (float)radius.Value;
    public float Strength => (float)strength.Value;
    public int Detail => (int)detail.Value;
    public string Mode => ((BrushChoice?)brush.SelectedItem)?.Id ?? "DRAW";

    public SculptBrushPanel()
    {
        BackColor = Theme.Panel;
        Theme.UseDarkControls(brush);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(5, 12, 0, 0), BackColor = Theme.Panel };
        brush.Items.AddRange([new BrushChoice("DRAW", "Draw · ยกผิว"), new BrushChoice("INFLATE", "Inflate · พองผิว"),
            new BrushChoice("SMOOTH", "Smooth · เกลี่ยผิว")]);
        brush.SelectedIndex = 0;
        AddLabel(flow, "Brush · หัวแปรง"); flow.Controls.Add(brush);
        AddLabel(flow, "Radius · รัศมีแปรง"); flow.Controls.Add(radius);
        AddLabel(flow, "Strength · ความแรง"); flow.Controls.Add(strength);
        AddLabel(flow, "Detail · ระดับแบ่งผิวครั้งแรก"); flow.Controls.Add(detail);
        toggle = Theme.Button("เปิดโหมดปั้นใน viewport", (_, _) => SetActive(!active), true);
        toggle.Width = 220; toggle.Height = 36; toggle.Margin = new Padding(0, 12, 0, 8); flow.Controls.Add(toggle);
        hint.AutoSize = false; hint.Width = 220; hint.Height = 100;
        hint.Text = "เลือกวัตถุแล้วลากเมาส์ซ้ายบนผิวโมเดล\nAlt + ลากซ้าย: หมุนมุมมอง\nลากขวา: เลื่อน · ลูกล้อ: ซูม\nปล่อยเมาส์เพื่อบันทึกรอยแปรง";
        flow.Controls.Add(hint); Controls.Add(flow);
        brush.SelectedIndexChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        radius.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        strength.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        detail.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        toggle.Text = active ? "ปิดโหมดปั้นใน viewport" : "เปิดโหมดปั้นใน viewport";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GenerateSnippet(SculptStroke stroke)
    {
        if (stroke.Samples.Count == 0) throw new ArgumentException("ไม่มีรอยแปรง", nameof(stroke));
        var code = new StringBuilder("\n# Sculpt Brush · ปั้นด้วยหัวแปรง\n_miti_sculpt_stroke(")
            .Append(ApiCatalogService.PythonString(stroke.ObjectName)).Append(", [\n");
        foreach (var sample in stroke.Samples)
            code.Append("    (").Append(Vector(sample.Position)).Append(", ").Append(Vector(sample.Normal)).AppendLine("),");
        code.Append("], radius=").Append(Number(Radius)).Append(", strength=").Append(Number(Strength))
            .Append(", mode=").Append(ApiCatalogService.PythonString(Mode)).Append(", detail=").Append(Detail).AppendLine(")");
        return code.ToString();
    }

    private static string Vector(Vector3 v) => "(" + Number(v.X) + ", " + Number(v.Y) + ", " + Number(v.Z) + ")";
    private static string Number(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals = 2)
    {
        var input = new NumericUpDown { Width = 220, Value = value, Minimum = minimum, Maximum = maximum,
            DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.05m, BackColor = Theme.Raised, ForeColor = Theme.Text };
        Theme.UseDarkControls(input);
        return input;
    }
    private static void AddLabel(FlowLayoutPanel flow, string text)
    {
        var label = Theme.Label(text, 9f, Theme.Text); label.AutoSize = false; label.Width = 220; label.Height = 25;
        flow.Controls.Add(label);
    }
}
