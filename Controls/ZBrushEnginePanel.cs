using System.Globalization;
using System.Text;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>ZBrush-style engine tools implemented on Blender, grouped like a SubTool palette.</summary>
public sealed class ZBrushEnginePanel : UserControl
{
    private sealed record ChoiceItem(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly Label target = Theme.Label("เลือกวัตถุก่อนใช้เครื่องมือ", 9, Theme.Accent);
    private readonly ComboBox primitive = Choice(("SPHERE", "Sphere · ทรงกลม"), ("ICOSPHERE", "Icosphere · ทรงกลมเหลี่ยม"),
        ("CUBE", "Cube · กล่อง"), ("CYLINDER", "Cylinder · ทรงกระบอก"), ("CONE", "Cone · กรวย"),
        ("TORUS", "Torus · โดนัท"), ("MONKEY", "Monkey · หัวลิง"));
    private readonly NumericUpDown insertSize = Number(0.6m, 0.01m, 100m, 2, 0.1m);
    private readonly NumericUpDown insertX = Number(1m, -1000m, 1000m, 2, 0.1m);
    private readonly NumericUpDown insertY = Number(0m, -1000m, 1000m, 2, 0.1m);
    private readonly NumericUpDown insertZ = Number(0m, -1000m, 1000m, 2, 0.1m);

    private readonly ComboBox clipAxis = Choice(("X", "X"), ("Y", "Y"), ("Z", "Z"));
    private readonly NumericUpDown clipPosition = Number(0m, -1000m, 1000m, 2, 0.1m);
    private readonly ComboBox clipKeep = Choice(("ABOVE", "Above · เก็บด้านบน"), ("BELOW", "Below · เก็บด้านล่าง"));

    private readonly ComboBox pivotAxis = Choice(("Z", "Z"), ("Y", "Y"), ("X", "X"));
    private readonly NumericUpDown pivotCoordinate = Number(0m, -1000m, 1000m, 2, 0.1m);
    private readonly NumericUpDown transposeMove = Number(0.5m, -100m, 100m, 2, 0.1m);
    private readonly NumericUpDown transposeRadius = Number(1.5m, 0.01m, 1000m, 2, 0.1m);

    private readonly ComboBox source = Choice(("", "— เลือกวัตถุ —"));
    private readonly NumericUpDown projectOffset = Number(0m, -10m, 10m, 3, 0.01m);

    private readonly NumericUpDown noiseStrength = Number(0.05m, 0m, 10m, 3, 0.01m);
    private readonly NumericUpDown noiseSize = Number(0.5m, 0.001m, 100m, 2, 0.1m);

    private readonly TextBox layerName = new() { Width = 236, Text = "Sculpt Layer", BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly ComboBox layerAxis = Choice(("Z", "Z"), ("Y", "Y"), ("X", "X"), ("NORMAL", "Normal · ตามแนวผิว"));
    private readonly NumericUpDown layerAmount = Number(0.1m, -10m, 10m, 3, 0.01m);
    private readonly NumericUpDown layerIntensity = Number(1m, -10m, 10m, 2, 0.1m);

    private readonly NumericUpDown sphereSegments = Number(4, 2, 32, 0, 1);
    private readonly NumericUpDown sphereRadius = Number(0.35m, 0.01m, 100m, 2, 0.05m);
    private readonly NumericUpDown sphereHeight = Number(2m, 0.1m, 100m, 2, 0.1m);

    private readonly NumericUpDown fibreCount = Number(80, 1, 2000, 0, 10);
    private readonly NumericUpDown fibreLength = Number(0.25m, 0.001m, 10m, 3, 0.01m);
    private readonly NumericUpDown fibreRadius = Number(0.008m, 0.0001m, 1m, 3, 0.001m);

    private readonly NumericUpDown nanoCount = Number(40, 1, 500, 0, 5);
    private readonly NumericUpDown nanoScale = Number(0.12m, 0.001m, 10m, 3, 0.01m);
    private readonly ComboBox nanoInstance = Choice(("", "— สร้างทรงกลมใหม่ —"));

    private readonly NumericUpDown arrayCount = Number(4, 1, 200, 0, 1);
    private readonly NumericUpDown arrayOffset = Number(0.6m, -100m, 100m, 2, 0.1m);
    private readonly NumericUpDown arrayTurn = Number(0m, -360m, 360m, 1, 5m);
    private readonly CheckBox arrayRadial = new() { Text = "Radial · กระจายเป็นวง", Width = 236, ForeColor = Theme.Text, BackColor = Theme.Panel };

    private readonly TextBox paintHex = new() { Width = 150, Text = "#FF7A29", BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly NumericUpDown paintHeight = Number(0m, -1000m, 1000m, 2, 0.1m);

    private readonly ComboBox boolTool = Choice(("", "— เลือกวัตถุ —"));
    private readonly ComboBox boolOperation = Choice(("UNION", "Union · รวม"), ("DIFFERENCE", "Subtract · ตัดออก"),
        ("INTERSECT", "Intersect · เก็บส่วนซ้อน"));

    private string? objectName;

    public ZBrushEnginePanel()
    {
        BackColor = Theme.Panel;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(6, 10, 0, 0), BackColor = Theme.Panel };
        Controls.Add(flow);
        target.AutoSize = false; target.Width = 236; target.Height = 34; flow.Controls.Add(target);
        var note = Theme.Label("เครื่องมือ ZBrush-style ที่ทำงานบน engine ของ Blender\nZBrush-style tools executed by Blender", 8.5f, Theme.Muted);
        note.AutoSize = false; note.Width = 236; note.Height = 34; flow.Controls.Add(note);

        Group(flow, "Insert Mesh · แทรกชิ้นงาน");
        Row(flow, "Shape · รูปทรง"); flow.Controls.Add(primitive);
        Label(flow, "Size · ขนาด"); flow.Controls.Add(insertSize);
        VectorRow(flow, "Location X/Y/Z · ตำแหน่ง", insertX, insertY, insertZ);
        ActionButton(flow, "Insert into selected · แทรกในวัตถุที่เลือก", InsertSnippet, "แทรกชิ้นงานใหม่ · Insert Mesh");
        ActionButton(flow, "Insert as new SubTool · แทรกเป็นวัตถุใหม่", InsertNewSnippet, "สร้างวัตถุใหม่ · New SubTool");

        Group(flow, "Clip Curve · ตัดระนาบ");
        Row(flow, "Axis · แกน"); flow.Controls.Add(clipAxis);
        Label(flow, "Plane height · ระดับระนาบ"); flow.Controls.Add(clipPosition);
        Row(flow, "Keep · เก็บด้าน"); flow.Controls.Add(clipKeep);
        ActionButton(flow, "Clip and cap · ตัดและปิดผิว", () => Code(
            $"zbrush_engine.clip_plane({TargetPreamble()}, axis={ChoiceValue(clipAxis)}, position={Decimal(clipPosition)}, " +
            $"keep={ChoiceValue(clipKeep)}, fill=True)"), "ตัดเมชด้วยระนาบ · Clip Curve");

        Group(flow, "Transpose · แปลงตามสัดส่วน");
        Row(flow, "Axis · แกน"); flow.Controls.Add(pivotAxis);
        Label(flow, "Pivot height · ระดับจุดหมุน"); flow.Controls.Add(pivotCoordinate);
        Label(flow, "Move · ระยะย้าย"); flow.Controls.Add(transposeMove);
        Label(flow, "Falloff radius · รัศมีผล"); flow.Controls.Add(transposeRadius);
        ActionButton(flow, "Transpose move · ย้ายตามสัดส่วน", TransposeSnippet, "ย้ายผิวตามสัดส่วน · Transpose");

        Group(flow, "Project All · ฉายลงผิวอื่น");
        Row(flow, "Source · วัตถุต้นแบบ"); flow.Controls.Add(source);
        Label(flow, "Offset · ระยะเผื่อ"); flow.Controls.Add(projectOffset);
        ActionButton(flow, "Project All · ฉายทั้งชิ้น", ProjectSnippet, "ฉายเมชลงผิววัตถุอื่น · Project All");

        Group(flow, "Surface Noise · ผิว-noise");
        Label(flow, "Strength · ความแรง"); flow.Controls.Add(noiseStrength);
        Label(flow, "Size · ขนาดลาย"); flow.Controls.Add(noiseSize);
        ActionButton(flow, "Apply noise · ใส่ลายผิว", NoiseSnippet, "เติมลายผิว · Surface Noise");

        Group(flow, "Sculpt Layer · เลเยอร์ปั้น");
        Label(flow, "Layer name · ชื่อเลเยอร์"); flow.Controls.Add(layerName);
        Row(flow, "Direction · ทิศทาง"); flow.Controls.Add(layerAxis);
        Label(flow, "Amount · ระยะดัน"); flow.Controls.Add(layerAmount);
        Label(flow, "Intensity · ความแรงเลเยอร์"); flow.Controls.Add(layerIntensity);
        ActionButton(flow, "Add sculpt layer · เพิ่มเลเยอร์", LayerSnippet, "เพิ่มเลเยอร์ปั้น · Sculpt Layer");

        Group(flow, "ZSpheres · โครงกระดูกทรงกลม");
        Label(flow, "Segments · จำนวนช่วง"); flow.Controls.Add(sphereSegments);
        Label(flow, "Radius · รัศมี"); flow.Controls.Add(sphereRadius);
        Label(flow, "Height · ความสูงรวม"); flow.Controls.Add(sphereHeight);
        ActionButton(flow, "Create ZSphere base · สร้างฐาน ZSphere", ZSphereSnippet, "สร้างฐานจาก ZSpheres");

        Group(flow, "FiberMesh · เส้นใย");
        Label(flow, "Strands · จำนวนเส้น"); flow.Controls.Add(fibreCount);
        Label(flow, "Length · ความยาว"); flow.Controls.Add(fibreLength);
        Label(flow, "Thickness · ความหนา"); flow.Controls.Add(fibreRadius);
        ActionButton(flow, "Grow fibers · ขึ้นเส้นใย", FibreSnippet, "สร้าง FiberMesh");

        Group(flow, "NanoMesh · กระจายชิ้นงาน");
        Label(flow, "Instances · จำนวนชิ้น"); flow.Controls.Add(nanoCount);
        Label(flow, "Scale · ขนาดชิ้น"); flow.Controls.Add(nanoScale);
        Row(flow, "Instance · วัตถุต้นแบบ"); flow.Controls.Add(nanoInstance);
        ActionButton(flow, "Scatter NanoMesh · กระจายชิ้นงาน", NanoSnippet, "กระจายชิ้นงาน · NanoMesh");

        Group(flow, "ArrayMesh · เรียงชิ้นงาน");
        Label(flow, "Count · จำนวน"); flow.Controls.Add(arrayCount);
        Label(flow, "Spacing · ระยะห่าง"); flow.Controls.Add(arrayOffset);
        Label(flow, "Rotate Z (°) · หมุนต่อชิ้น"); flow.Controls.Add(arrayTurn);
        flow.Controls.Add(arrayRadial);
        ActionButton(flow, "ArrayMesh · เรียงชิ้นงาน", ArraySnippet, "เรียงชิ้นงาน · ArrayMesh");

        Group(flow, "PolyPaint · ระบายสีจุด");
        ColorRow(flow, "Colour · สี", paintHex);
        Label(flow, "Paint above height · ระบายเหนือระดับ"); flow.Controls.Add(paintHeight);
        ActionButton(flow, "Paint all · ระบายทั้งชิ้น", () => Code(
            $"{TargetPreamble()}\nzbrush_engine.polypaint(_miti_target, color={Python(paintHex.Text.Trim())}, use_mask=True)"), "ระบายสีทั้งชิ้น · PolyPaint");
        ActionButton(flow, "Paint above · ระบายเหนือระดับ", () => Code(
            $"{TargetPreamble()}\nzbrush_engine.polypaint(_miti_target, color={Python(paintHex.Text.Trim())}, axis='Z', minimum={Decimal(paintHeight)})"),
            "ระบายสีเหนือระดับ · Paint above");
        ActionButton(flow, "Paint below · ระบายใต้ระดับ", () => Code(
            $"{TargetPreamble()}\nzbrush_engine.polypaint(_miti_target, color={Python(paintHex.Text.Trim())}, axis='Z', maximum={Decimal(paintHeight)})"),
            "ระบายสีใต้ระดับ · Paint below");

        Group(flow, "SubTool Boolean · รวมวัตถุ");
        Row(flow, "Tool · วัตถุที่ใช้ตัด"); flow.Controls.Add(boolTool);
        Row(flow, "Operation · วิธีรวม"); flow.Controls.Add(boolOperation);
        ActionButton(flow, "Combine SubTools · รวมวัตถุ", BooleanSnippet, "รวมวัตถุย่อย · SubTool Boolean");

        Group(flow, "OBJ / Mesh Exchange · รับส่งโมเดล");
        ActionButton(flow, "Import mesh · นำเข้าเมช…", ImportSnippet, "นำเข้า OBJ/GLB/STL/FBX · Import mesh");
        ActionButton(flow, "Import ZBrush OBJ · นำโมเดลกลับ…", ImportZBrushObjSnippet, "นำ OBJ จาก ZBrush กลับเข้า Blender");
        var nativeButton = Theme.Button("Open in ZBrush · เปิดวัตถุใน ZBrush", (_, _) => NativeOpenRequested?.Invoke(this, EventArgs.Empty));
        nativeButton.Width = 236; nativeButton.Height = 32; nativeButton.TextAlign = ContentAlignment.MiddleLeft;
        flow.Controls.Add(nativeButton);
        Label(flow, "Native API · ใช้ ZBrush ที่ติดตั้งในเครื่อง (OBJ)");
    }

    public event EventHandler<string>? ApplyRequested;
    public event EventHandler? NativeOpenRequested;

    public void SetTargetObject(string? value)
    {
        objectName = value;
        target.Text = value == null ? "เลือกวัตถุก่อนใช้เครื่องมือ" : "วัตถุ · Object: " + value;
    }

    public void SetObjects(IEnumerable<string> names)
    {
        var all = names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
        var selectedSource = RawValue(source);
        var selectedTool = RawValue(boolTool);
        var selectedNano = RawValue(nanoInstance);
        Fill(source, "— เลือกวัตถุ —", all, selectedSource);
        Fill(boolTool, "— เลือกวัตถุ —", all, selectedTool);
        Fill(nanoInstance, "— สร้างทรงกลมใหม่ —", all, selectedNano);
    }

    private static void Fill(ComboBox combo, string empty, IEnumerable<string> names, string previous)
    {
        combo.Items.Clear();
        combo.Items.Add(new ChoiceItem("", empty));
        foreach (var name in names) combo.Items.Add(new ChoiceItem(name, name));
        var index = Enumerable.Range(0, combo.Items.Count)
            .FirstOrDefault(i => ((ChoiceItem)combo.Items[i]!).Value == previous, 0);
        combo.SelectedIndex = Math.Min(index, combo.Items.Count - 1);
    }

    private void ActionButton(FlowLayoutPanel flow, string text, Func<string> body, string description)
    {
        var button = Theme.Button(text, (_, _) => Apply(body(), description));
        button.Width = 236; button.Height = 30; button.TextAlign = ContentAlignment.MiddleLeft;
        button.Margin = new Padding(0, 2, 0, 3);
        flow.Controls.Add(button);
    }

    private void Apply(string body, string description)
    {
        if (string.IsNullOrWhiteSpace(body)) return;
        if (objectName == null && RequiresTarget(body))
        {
            MessageBox.Show(this, "เลือกวัตถุก่อนใช้เครื่องมือนี้\nSelect an object first.", "ยังไม่ได้เลือกวัตถุ",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var code = new StringBuilder()
            .AppendLine().Append("# ZBrush engine · เอนจิน ZBrush: ").AppendLine(description)
            .AppendLine(body)
            .AppendLine("bpy.context.view_layer.update()")
            .ToString();
        ApplyRequested?.Invoke(this, code);
    }

    private static bool RequiresTarget(string body) => body.Contains("_miti_target", StringComparison.Ordinal);

    private string TargetPreamble() => "_miti_target = bpy.data.objects.get(" + Python(objectName ?? "") +
        ")\nif _miti_target is None: raise ValueError('ไม่พบวัตถุที่เลือก · Selected object is missing')";

    private string InsertSnippet() => Code(
        $"{TargetPreamble()}\nzbrush_engine.insert_mesh(_miti_target, {ChoiceValue(primitive)}, " +
        $"location=({Decimal(insertX)}, {Decimal(insertY)}, {Decimal(insertZ)}), size={Decimal(insertSize)}, join=True)");

    private string InsertNewSnippet() => Code(
        $"zbrush_engine.insert_mesh(None, {ChoiceValue(primitive)}, location=({Decimal(insertX)}, {Decimal(insertY)}, {Decimal(insertZ)}), size={Decimal(insertSize)}, join=False)");

    private string TransposeSnippet() => Code(
        $"{TargetPreamble()}\nzbrush_engine.transpose(_miti_target, pivot={Pivot()}, move=({AxisVector()}), " +
        $"radius={Decimal(transposeRadius)}, falloff='SMOOTH')");

    private string ProjectSnippet()
    {
        var value = RawValue(source);
        if (string.IsNullOrEmpty(value))
        {
            MessageBox.Show(this, "เลือกวัตถุต้นแบบก่อนฉาย\nChoose a source object first.", "Project All",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return "";
        }
        return Code($"{TargetPreamble()}\nzbrush_engine.project_all(_miti_target, {Python(value)}, offset={Decimal(projectOffset)})");
    }

    private string NoiseSnippet() => Code(
        $"{TargetPreamble()}\nzbrush_engine.surface_noise(_miti_target, strength={Decimal(noiseStrength)}, size={Decimal(noiseSize)})");

    private string LayerSnippet()
    {
        var direction = RawValue(layerAxis);
        return Code($"{TargetPreamble()}\nzbrush_engine.sculpt_layer(_miti_target, name={Python(layerName.Text.Trim())}, " +
            $"axis={Python(direction)}, amount={Decimal(layerAmount)}, intensity={Decimal(layerIntensity)})");
    }

    private string ZSphereSnippet()
    {
        var segments = (int)sphereSegments.Value;
        var height = sphereHeight.Value;
        var radius = sphereRadius.Value;
        var points = new StringBuilder("[");
        var radii = new StringBuilder("[");
        for (var index = 0; index < segments; index++)
        {
            var t = segments == 1 ? 0 : index / (decimal)(segments - 1);
            var taper = 1m - 0.55m * t;
            points.Append(index == 0 ? "" : ", ").Append($"({Number(radius * 0.6m)}, 0, {Number(height * t)})");
            radii.Append(index == 0 ? "" : ", ").Append(Number(radius * taper));
        }
        points.Append(']'); radii.Append(']');
        return Code($"zbrush_engine.zsphere('ZSphere', points={points}, radii={radii}, resolution=0.06)");
    }

    private string FibreSnippet() => Code(
        $"{TargetPreamble()}\nzbrush_engine.fibermesh(_miti_target, count={Decimal(fibreCount)}, " +
        $"length={Decimal(fibreLength)}, radius={Decimal(fibreRadius)})");

    private string NanoSnippet()
    {
        var instance = RawValue(nanoInstance);
        var argument = string.IsNullOrEmpty(instance) ? "None" : Python(instance);
        return Code($"{TargetPreamble()}\nzbrush_engine.nanomesh(_miti_target, instance_name={argument}, " +
            $"count={Decimal(nanoCount)}, scale={Decimal(nanoScale)})");
    }

    private string ArraySnippet() => Code(
        $"{TargetPreamble()}\nzbrush_engine.array_mesh(_miti_target, count={Decimal(arrayCount)}, " +
        $"offset=({Decimal(arrayOffset)}, 0, 0), rotate=(0, 0, {Decimal(arrayTurn)}), radial={(arrayRadial.Checked ? "True" : "False")})");

    private string BooleanSnippet()
    {
        var tool = RawValue(boolTool);
        if (string.IsNullOrEmpty(tool))
        {
            MessageBox.Show(this, "เลือกวัตถุที่ใช้ตัดก่อนรวม\nChoose a tool object first.", "SubTool Boolean",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return "";
        }
        return Code($"{TargetPreamble()}\nzbrush_engine.subtool_boolean(_miti_target, {Python(tool)}, {ChoiceValue(boolOperation)})");
    }

    private string ImportSnippet()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import mesh · นำเข้าเมช",
            Filter = "3D files · ไฟล์ 3 มิติ|*.obj;*.glb;*.gltf;*.stl;*.fbx|All files|*.*"
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? Code($"zbrush_engine.import_mesh({Python(dialog.FileName)})")
            : "";
    }

    private string ImportZBrushObjSnippet()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import ZBrush OBJ · นำโมเดลจาก ZBrush กลับ",
            Filter = "ZBrush OBJ (*.obj)|*.obj"
        };
        return dialog.ShowDialog(this) == DialogResult.OK
            ? Code($"zbrush_engine.import_mesh({Python(dialog.FileName)})")
            : "";
    }

    private string Pivot()
    {
        var axis = RawValue(pivotAxis);
        return axis switch
        {
            "X" => $"({Decimal(pivotCoordinate)}, 0, 0)",
            "Y" => $"(0, {Decimal(pivotCoordinate)}, 0)",
            _ => $"(0, 0, {Decimal(pivotCoordinate)})"
        };
    }

    private string AxisVector()
    {
        var axis = RawValue(pivotAxis);
        var amount = Decimal(transposeMove);
        return axis switch { "X" => $"{amount}, 0, 0", "Y" => $"0, {amount}, 0", _ => $"0, 0, {amount}" };
    }

    private void ColorRow(FlowLayoutPanel flow, string text, TextBox input)
    {
        Label(flow, text);
        var row = new FlowLayoutPanel { Width = 236, Height = 32, WrapContents = false, Margin = Padding.Empty };
        Theme.UseDarkControls(input);
        var pick = Theme.Button("Pick · เลือก", (_, _) =>
        {
            using var dialog = new ColorDialog { FullOpen = true, Color = Color.Orange };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            input.Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        });
        pick.Width = 80; pick.Height = 28; pick.Padding = new Padding(4, 0, 4, 0);
        row.Controls.Add(input); row.Controls.Add(pick); flow.Controls.Add(row);
    }

    private void VectorRow(FlowLayoutPanel flow, string text, NumericUpDown x, NumericUpDown y, NumericUpDown z)
    {
        Label(flow, text);
        var row = new FlowLayoutPanel { Width = 236, Height = 32, WrapContents = false, Margin = Padding.Empty };
        foreach (var input in new[] { x, y, z }) { input.Width = 74; input.Margin = new Padding(0, 0, 4, 0); row.Controls.Add(input); }
        flow.Controls.Add(row);
    }

    private static string Code(string body) => body;
    private static string Python(string value) => ApiCatalogService.PythonString(value);
    private static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Decimal(NumericUpDown input) => input.Value.ToString(CultureInfo.InvariantCulture);
    private static string ChoiceValue(ComboBox combo) =>
        combo.SelectedItem is ChoiceItem item ? Python(item.Value) : "''";

    private static string RawValue(ComboBox combo) =>
        combo.SelectedItem is ChoiceItem item ? item.Value : "";

    private static ComboBox Choice(params (string Value, string Label)[] options)
    {
        var combo = new StudioComboBox { Width = 236 };
        foreach (var (value, label) in options) combo.Items.Add(new ChoiceItem(value, label));
        combo.SelectedIndex = 0;
        return combo;
    }

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals, decimal increment)
    {
        var input = new NumericUpDown { Width = 236, Minimum = minimum, Maximum = maximum, Value = value,
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

    private static void Row(FlowLayoutPanel flow, string text) => Label(flow, text);
}
