using System.Globalization;
using System.Numerics;
using System.Text;

namespace ThaiModelStudio.Controls;

/// <summary>A compact sculpt palette with English names followed by Thai names.</summary>
public sealed class SculptBrushPanel : UserControl
{
    private readonly Dictionary<string, Button> brushButtons = [];
    private readonly NumericUpDown radius = Number(0.8m, 0.01m, 100m);
    private readonly NumericUpDown strength = Number(0.5m, 0.01m, 1m);
    private readonly NumericUpDown focal = Number(0, -100, 100, 0);
    private readonly NumericUpDown detail = Number(3, 0, 8, 0);
    private readonly CheckBox symmetryX = Axis("X"), symmetryY = Axis("Y"), symmetryZ = Axis("Z");
    private readonly Button zAdd, zSub, toggle;
    private string mode = "STANDARD";
    private bool subtract;
    private bool active;
    public event EventHandler? SettingsChanged;
    public event EventHandler? UndoRequested;
    public bool Active => active;
    public float Radius => (float)radius.Value;
    public float Strength => (float)strength.Value;
    public int Detail => (int)detail.Value;
    public int FocalShift => (int)focal.Value;
    public string Mode => mode;
    public string BrushLabel => brushButtons[mode].Text.Replace("\n", " · ");
    public bool Subtract => subtract;
    public string Symmetry => (symmetryX.Checked ? "X" : "") + (symmetryY.Checked ? "Y" : "") + (symmetryZ.Checked ? "Z" : "");

    public SculptBrushPanel()
    {
        BackColor = Theme.Panel;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(5, 10, 0, 0), BackColor = Theme.Panel };
        Controls.Add(flow);
        AddLabel(flow, "Brush · หัวแปรง", true);
        toggle = Theme.Button("เปิด Sculpt Mode · โหมดปั้น", (_, _) => SetActive(!active), true);
        toggle.Width = 220; toggle.Height = 36; toggle.Margin = new Padding(0, 0, 0, 5); flow.Controls.Add(toggle);
        var undo = Theme.Button("Undo Stroke · ย้อนรอยล่าสุด", (_, _) => UndoRequested?.Invoke(this, EventArgs.Empty));
        undo.Width = 220; undo.Height = 32; undo.Margin = new Padding(0, 0, 0, 7); flow.Controls.Add(undo);
        var palette = new FlowLayoutPanel { Width = 222, Height = 170, WrapContents = true, Margin = Padding.Empty };
        foreach (var (id, english, thai) in new[] {
            ("STANDARD", "Standard", "มาตรฐาน"), ("CLAY", "Clay", "เติมดิน"),
            ("MOVE", "Move", "ดึงผิว"), ("SMOOTH", "Smooth", "เกลี่ยผิว"),
            ("FLATTEN", "Flatten", "ปรับแบน"), ("PINCH", "Pinch", "บีบผิว"),
            ("INFLATE", "Inflate", "พองผิว"), ("CREASE", "Crease", "เซาะร่อง") })
        {
            var button = PaletteButton(english + "\n" + thai, (_, _) => SetMode(id));
            palette.Controls.Add(button); brushButtons.Add(id, button);
        }
        flow.Controls.Add(palette);
        AddLabel(flow, "Stroke · ทิศทางการปั้น", true);
        var direction = new FlowLayoutPanel { Width = 222, Height = 37, WrapContents = false, Margin = Padding.Empty };
        zAdd = PaletteButton("ZAdd · เพิ่มผิว", (_, _) => SetSubtract(false));
        zSub = PaletteButton("ZSub · ลดผิว", (_, _) => SetSubtract(true));
        zAdd.Height = zSub.Height = 32; direction.Controls.Add(zAdd); direction.Controls.Add(zSub); flow.Controls.Add(direction);
        AddLabel(flow, "Draw Size · รัศมีแปรง"); flow.Controls.Add(radius);
        AddLabel(flow, "Z Intensity · ความแรง"); flow.Controls.Add(strength);
        AddLabel(flow, "Focal Shift · ความคมขอบ"); flow.Controls.Add(focal);
        AddLabel(flow, "Symmetry · สมมาตร", true);
        var axes = new FlowLayoutPanel { Width = 222, Height = 30, WrapContents = false, Margin = Padding.Empty };
        axes.Controls.AddRange([symmetryX, symmetryY, symmetryZ]); flow.Controls.Add(axes);
        AddLabel(flow, "Detail · แบ่งผิวครั้งแรก"); flow.Controls.Add(detail);
        var hint = Theme.Label("ลากซ้ายบนผิวโมเดล · Shift: Smooth\nAlt + ลากซ้าย: หมุน · X: Symmetry\n[ ]: ปรับขนาดแปรง · ปล่อยเพื่อบันทึก", 8.5f, Theme.Muted);
        hint.AutoSize = false; hint.Width = 220; hint.Height = 65; flow.Controls.Add(hint);
        foreach (var input in new[] { radius, strength, focal, detail })
            input.ValueChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        foreach (var axis in new[] { symmetryX, symmetryY, symmetryZ })
            axis.CheckedChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        RefreshButtons();
    }

    public void SetActive(bool value)
    {
        if (active == value) return;
        active = value;
        toggle.Text = active ? "ปิด Sculpt Mode · โหมดปั้น" : "เปิด Sculpt Mode · โหมดปั้น";
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetMode(string value)
    {
        if (!brushButtons.ContainsKey(value)) throw new ArgumentOutOfRangeException(nameof(value));
        mode = value; RefreshButtons(); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetSubtract(bool value)
    {
        subtract = value; RefreshButtons(); SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ToggleSymmetryX() => symmetryX.Checked = !symmetryX.Checked;
    public void AdjustRadius(int steps) => radius.Value = Math.Clamp(radius.Value + steps * 0.1m, radius.Minimum, radius.Maximum);
    public void FocusBrush() => brushButtons[mode].Focus();

    public string GenerateSnippet(SculptStroke stroke)
    {
        if (stroke.Samples.Count == 0) throw new ArgumentException("ไม่มีรอยแปรง", nameof(stroke));
        var actualMode = stroke.ModeOverride ?? mode;
        var code = new StringBuilder("\n# Sculpt Brush · ปั้นด้วยหัวแปรง\n_miti_sculpt_stroke(")
            .Append(ApiCatalogService.PythonString(stroke.ObjectName)).Append(", [\n");
        foreach (var sample in stroke.Samples)
            code.Append("    (").Append(Vector(sample.Position)).Append(", ").Append(Vector(sample.Normal)).AppendLine("),");
        code.Append("], radius=").Append(Number(Radius)).Append(", strength=").Append(Number(Strength))
            .Append(", mode=").Append(ApiCatalogService.PythonString(actualMode)).Append(", detail=").Append(Detail)
            .Append(", subtract=").Append(subtract ? "True" : "False")
            .Append(", symmetry=").Append(ApiCatalogService.PythonString(Symmetry))
            .Append(", focal_shift=").Append(FocalShift).AppendLine(")");
        return code.ToString();
    }

    private void RefreshButtons()
    {
        foreach (var (id, button) in brushButtons) PaintChoice(button, id == mode);
        PaintChoice(zAdd, !subtract); PaintChoice(zSub, subtract);
    }
    private static void PaintChoice(Button button, bool selected)
    {
        button.BackColor = selected ? Theme.Accent : Theme.Raised;
        button.ForeColor = selected ? Theme.Background : Theme.Text;
        button.FlatAppearance.BorderColor = selected ? Theme.Accent : Theme.Border;
    }
    private static Button PaletteButton(string text, EventHandler action)
    {
        var button = new Button { Text = text, Width = 108, Height = 40, Margin = new Padding(0, 0, 2, 2),
            FlatStyle = FlatStyle.Flat, BackColor = Theme.Raised, ForeColor = Theme.Text,
            Font = new Font("Leelawadee UI", 8.3f), Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleCenter };
        button.FlatAppearance.BorderColor = Theme.Border; button.Click += action; return button;
    }
    private static CheckBox Axis(string axis) => new() { Text = axis, Width = 70, Height = 28, ForeColor = Theme.Text,
        BackColor = Theme.Panel, Font = new Font("Leelawadee UI", 9, FontStyle.Bold) };
    private static string Vector(Vector3 v) => "(" + Number(v.X) + ", " + Number(v.Y) + ", " + Number(v.Z) + ")";
    private static string Number(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals = 2)
    {
        var input = new NumericUpDown { Width = 220, Value = value, Minimum = minimum, Maximum = maximum,
            DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.05m, BackColor = Theme.Raised, ForeColor = Theme.Text };
        Theme.UseDarkControls(input); return input;
    }
    private static void AddLabel(FlowLayoutPanel flow, string text, bool bold = false)
    {
        var label = Theme.Label(text, 9f, bold ? Theme.Accent : Theme.Text, bold);
        label.AutoSize = false; label.Width = 220; label.Height = 24; flow.Controls.Add(label);
    }
}
