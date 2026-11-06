using System.Globalization;
using System.Numerics;
using System.Text;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

public sealed record ShapeToolAction(string Code, string? CreatedName);

/// <summary>Forms for creating primitives and setting an object's absolute transform.</summary>
public sealed class ShapeTransformPanel : UserControl
{
    private sealed record Shape(string Label, string Operator, string DefaultName)
    {
        public override string ToString() => Label;
    }

    private readonly StudioTabs tabs = new() { Dock = DockStyle.Fill };
    private readonly ComboBox shapes = new StudioComboBox { Width = 220 };
    private readonly TextBox shapeName = new() { Width = 220, BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly VectorInput createPosition = new(Vector3.Zero, -10000, 10000);
    private readonly VectorInput createScale = new(Vector3.One, 0, 1000);
    private readonly VectorInput position = new(Vector3.Zero, -10000, 10000);
    private readonly VectorInput rotation = new(Vector3.Zero, -3600, 3600);
    private readonly VectorInput scale = new(Vector3.One, 0, 1000);
    private readonly Label selectedLabel = Theme.Label("เลือกวัตถุจากมุมมองหรือรายการ", 9, Theme.Accent);
    private readonly Label liveLabel = Theme.Label("ปรับค่าเพื่อดูภาพตัวอย่าง", 8.5f, Theme.Accent);
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 650 };
    private string? selectedName;
    private bool loading;
    public event EventHandler<string>? PreviewRequested;
    public event EventHandler? PreviewInvalidated;
    public event EventHandler<ShapeToolAction>? ApplyRequested;

    public ShapeTransformPanel()
    {
        BackColor = Theme.Panel;
        var createPage = new StudioTabPage("Create · สร้าง");
        var editPage = new StudioTabPage("Edit · แก้ไข");
        tabs.TabPages.AddRange([createPage, editPage]); Controls.Add(tabs);
        Theme.UseDarkControls(shapes, shapeName);
        var create = MakeFlow(); createPage.Controls.Add(create);
        AddLabel(create, "Shape · รูปทรง"); create.Controls.Add(shapes);
        foreach (var shape in new[] {
            new Shape("Cube · กล่อง", "primitive_cube_add", "กล่องใหม่"),
            new Shape("UV Sphere · ทรงกลม UV", "primitive_uv_sphere_add", "ทรงกลมใหม่"),
            new Shape("Icosphere · ทรงกลมเหลี่ยม", "primitive_ico_sphere_add", "ทรงกลมเหลี่ยม"),
            new Shape("Cylinder · ทรงกระบอก", "primitive_cylinder_add", "ทรงกระบอกใหม่"),
            new Shape("Cone · ทรงกรวย", "primitive_cone_add", "ทรงกรวยใหม่"),
            new Shape("Torus · โดนัท", "primitive_torus_add", "โดนัทใหม่"),
            new Shape("Plane · ระนาบ", "primitive_plane_add", "ระนาบใหม่"),
            new Shape("Monkey · หัวลิง", "primitive_monkey_add", "หัวลิงใหม่") }) shapes.Items.Add(shape);
        shapes.SelectedIndex = 0;
        AddLabel(create, "Name · ชื่อวัตถุ"); create.Controls.Add(shapeName); shapeName.Text = "กล่องใหม่";
        AddLabel(create, "Location X/Y/Z · ตำแหน่ง"); create.Controls.Add(createPosition);
        AddLabel(create, "Scale X/Y/Z · สเกล"); create.Controls.Add(createScale);
        var createButton = Theme.Button("Create · เพิ่มรูปทรง", (_, _) => ApplyCreate(), true);
        createButton.Width = 220; create.Controls.Add(createButton);

        var edit = MakeFlow(); editPage.Controls.Add(edit);
        selectedLabel.AutoSize = false; selectedLabel.Width = 220; selectedLabel.Height = 40; edit.Controls.Add(selectedLabel);
        AddLabel(edit, "Location X/Y/Z · ตำแหน่ง"); edit.Controls.Add(position);
        AddLabel(edit, "Rotation X/Y/Z (°) · มุมหมุน"); edit.Controls.Add(rotation);
        AddLabel(edit, "Scale X/Y/Z · สเกล"); edit.Controls.Add(scale);
        var applyButton = Theme.Button("Apply · ใช้กับวัตถุ", (_, _) => ApplyTransform(), true);
        applyButton.Width = 220; edit.Controls.Add(applyButton);

        liveLabel.AutoSize = false; liveLabel.Dock = DockStyle.Bottom; liveLabel.Height = 34; Controls.Add(liveLabel);
        shapes.SelectedIndexChanged += (_, _) => { shapeName.Text = ((Shape)shapes.SelectedItem!).DefaultName; Schedule(); };
        shapeName.TextChanged += (_, _) => Schedule();
        foreach (var vector in new[] { createPosition, createScale, position, rotation, scale }) vector.Changed += (_, _) => Schedule();
        tabs.SelectedIndexChanged += (_, _) => Schedule();
        timer.Tick += (_, _) => { timer.Stop(); try { PreviewRequested?.Invoke(this, CurrentSnippet()); } catch (Exception) { } };
    }

    public void ShowTransformTab() => tabs.SelectedIndex = 1;
    public void ShowCreateTab() => tabs.SelectedIndex = 0;
    public void SetPreviewStatus(string text) => liveLabel.Text = text;
    public void SelectObject(SceneObjectInfo info)
    {
        if (selectedName == info.Name) return;
        selectedName = info.Name; selectedLabel.Text = "วัตถุ: " + info.Name;
        loading = true;
        position.Set(info.Location); rotation.Set(info.RotationDegrees); scale.Set(info.Scale);
        loading = false;
        if (tabs.SelectedIndex == 1) Schedule();
    }

    private static FlowLayoutPanel MakeFlow() => new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, Padding = new Padding(5, 10, 0, 0), BackColor = Theme.Panel };
    private static void AddLabel(FlowLayoutPanel flow, string text)
    {
        var label = Theme.Label(text, 9f, Theme.Text); label.Width = 220; label.Height = 24; label.AutoSize = false;
        flow.Controls.Add(label);
    }
    private void Schedule()
    {
        if (loading) return;
        timer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty);
        if (tabs.SelectedIndex == 1 && selectedName == null) { liveLabel.Text = "เลือกวัตถุก่อนปรับค่า"; return; }
        liveLabel.Text = "รอ Blender อัปเดตโมเดล…"; timer.Start();
    }
    private string CurrentSnippet() => tabs.SelectedIndex == 0 ? CreateSnippet() : TransformSnippet();
    private string CreateSnippet()
    {
        var shape = (Shape?)shapes.SelectedItem ?? throw new InvalidOperationException("เลือกรูปทรงก่อน");
        var name = shapeName.Text.Trim(); if (name.Length == 0) throw new InvalidOperationException("ใส่ชื่อวัตถุ");
        return new StringBuilder("\n# เพิ่มรูปทรง: ").Append(shape.Label).AppendLine()
            .AppendLine("if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')")
            .Append("bpy.ops.mesh.").Append(shape.Operator).Append("(location=").Append(createPosition.PythonTuple).AppendLine(")")
            .Append("bpy.context.object.name = ").AppendLine(ApiCatalogService.PythonString(name))
            .Append("bpy.context.object.scale = ").AppendLine(createScale.PythonTuple).ToString();
    }
    private string TransformSnippet()
    {
        if (selectedName == null) throw new InvalidOperationException("เลือกวัตถุก่อน");
        return new StringBuilder("\n# ย้าย หมุน และสเกล: ").Append(selectedName).AppendLine()
            .Append("_miti_obj = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(selectedName)).AppendLine(")")
            .AppendLine("if _miti_obj is None: raise ValueError('ไม่พบวัตถุที่เลือก')")
            .Append("_miti_obj.location = ").AppendLine(position.PythonTuple)
            .Append("_miti_obj.rotation_euler = tuple(math.radians(v) for v in ").Append(rotation.PythonTuple).AppendLine(")")
            .Append("_miti_obj.scale = ").AppendLine(scale.PythonTuple).ToString();
    }
    private void ApplyCreate()
    {
        try { timer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty); ApplyRequested?.Invoke(this, new ShapeToolAction(CreateSnippet(), shapeName.Text.Trim())); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ตรวจค่า", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    private void ApplyTransform()
    {
        try { timer.Stop(); PreviewInvalidated?.Invoke(this, EventArgs.Empty); ApplyRequested?.Invoke(this, new ShapeToolAction(TransformSnippet(), null)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "ตรวจค่า", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private sealed class VectorInput : FlowLayoutPanel
    {
        private readonly NumericUpDown[] axes = new NumericUpDown[3];
        public event EventHandler? Changed;
        public VectorInput(Vector3 initial, decimal min, decimal max)
        {
            Width = 220; Height = 36; WrapContents = false; BackColor = Theme.Panel;
            for (int i = 0; i < 3; i++)
            {
                Controls.Add(new Label { Text = "XYZ"[i].ToString(), Width = 13, Height = 26,
                    ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty });
                var axis = new NumericUpDown { Width = 60, Minimum = min, Maximum = max, DecimalPlaces = 2,
                    Increment = 0.1m, BackColor = Theme.Raised, ForeColor = Theme.Text, Margin = Padding.Empty };
                Theme.UseDarkControls(axis);
                axis.ValueChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
                axes[i] = axis; Controls.Add(axis);
            }
            Set(initial);
        }
        public void Set(Vector3 value)
        {
            float[] parts = [value.X, value.Y, value.Z];
            for (int i = 0; i < 3; i++) axes[i].Value = Math.Clamp((decimal)parts[i], axes[i].Minimum, axes[i].Maximum);
        }
        public string PythonTuple => "(" + string.Join(", ", axes.Select(a => a.Value.ToString(CultureInfo.InvariantCulture))) + ")";
    }
}
