using System.Globalization;
using System.Text;
using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>ZBrush-style topology, deformation and modifier workflows executed by Blender.</summary>
public sealed class TopologyPanel : UserControl
{
    private readonly Label target = Theme.Label("เลือกวัตถุก่อนใช้เครื่องมือ", 9, Theme.Accent);
    private readonly NumericUpDown voxel = Number(0.06m, 0.005m, 2m, 3);
    private readonly NumericUpDown quadFaces = Number(4000, 100, 200000, 0);
    private readonly NumericUpDown levels = Number(2, 1, 4, 0);
    private readonly NumericUpDown ratio = Number(0.5m, 0.05m, 1m, 2);
    private readonly ComboBox mirrorAxis = Choice("X", "Y", "Z");
    private readonly NumericUpDown merge = Number(0.001m, 0.0001m, 1m, 4);
    private readonly ComboBox deformMethod = Choice("TAPER", "TWIST", "BEND", "STRETCH");
    private readonly ComboBox deformAxis = Choice("X", "Y", "Z");
    private readonly NumericUpDown deformFactor = Number(0.5m, -10m, 10m, 2);
    private readonly NumericUpDown bevelWidth = Number(0.1m, 0.001m, 10m, 3);
    private readonly NumericUpDown bevelSegments = Number(3, 1, 20, 0);
    private readonly NumericUpDown thickness = Number(0.1m, 0.001m, 10m, 3);
    private string? objectName;

    public TopologyPanel()
    {
        BackColor = Theme.Panel;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(6, 10, 0, 0), BackColor = Theme.Panel };
        target.AutoSize = false; target.Width = 236; target.Height = 34; flow.Controls.Add(target);

        Group(flow, "Remesh · รีเมช");
        Label(flow, "Voxel Size · ขนาด voxel"); flow.Controls.Add(voxel);
        ActionButton(flow, "Voxel Remesh · รีเมช voxel", () => $"""
            _miti_obj.data.remesh_voxel_size = {Decimal(voxel)}
            bpy.ops.object.voxel_remesh()
            """, "รีเมช voxel (DynaMesh style)");
        Label(flow, "Target Faces · จำนวนหน้าเป้าหมาย"); flow.Controls.Add(quadFaces);
        ActionButton(flow, "Quad Remesh · รีเมชสี่เหลี่ยม", () => $"""
            bpy.ops.object.quadriflow_remesh(target_faces={Decimal(quadFaces)})
            """, "รีเมชสี่เหลี่ยม (อาจใช้เวลาหลายวินาที)");

        Group(flow, "Subdivide · แบ่งผิว");
        Label(flow, "Levels · จำนวนระดับ"); flow.Controls.Add(levels);
        ActionButton(flow, "Subdivide · แบ่งผิว", () => $"""
            _mod = _miti_obj.modifiers.new("MitiSubdivision", 'SUBSURF')
            _mod.levels = {Decimal(levels)}
            _mod.render_levels = {Decimal(levels)}
            bpy.ops.object.modifier_apply(modifier=_mod.name)
            """, "แบ่งผิวด้วย Subdivision");

        Group(flow, "Decimate · ลดรายละเอียด");
        Label(flow, "Ratio · สัดส่วนที่เก็บ"); flow.Controls.Add(ratio);
        ActionButton(flow, "Decimate · ลดรายละเอียด", () => $"""
            _mod = _miti_obj.modifiers.new("MitiDecimate", 'DECIMATE')
            _mod.ratio = {Decimal(ratio)}
            bpy.ops.object.modifier_apply(modifier=_mod.name)
            """, "ลดจำนวนหน้าด้วย Decimate");

        Group(flow, "Mirror · สมมาตร");
        Label(flow, "Axis · แกน"); flow.Controls.Add(mirrorAxis);
        ActionButton(flow, "Mirror · ทำสมมาตร", () =>
        {
            var axis = mirrorAxis.SelectedIndex;
            return $"""
                _mod = _miti_obj.modifiers.new("MitiMirror", 'MIRROR')
                _mod.use_axis[0] = {Bool(axis == 0)}
                _mod.use_axis[1] = {Bool(axis == 1)}
                _mod.use_axis[2] = {Bool(axis == 2)}
                _mod.use_mirror_merge = True
                _mod.merge_threshold = {Decimal(merge)}
                bpy.ops.object.modifier_apply(modifier=_mod.name)
                """;
        }, "ทำสมมาตรด้วย Mirror");

        Group(flow, "Merge · เชื่อมจุด");
        Label(flow, "Distance · ระยะเชื่อม"); flow.Controls.Add(merge);
        ActionButton(flow, "Merge by Distance · เชื่อมจุดซ้ำ", () => $"""
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.mesh.remove_doubles(threshold={Decimal(merge)})
            bpy.ops.object.mode_set(mode='OBJECT')
            """, "เชื่อมจุดซ้ำ");

        Group(flow, "Shading · ผิว");
        ActionButton(flow, "Shade Smooth · ผิวเรียบ", () => "bpy.ops.object.shade_smooth()", "ผิวเรียบ");
        ActionButton(flow, "Shade Flat · ผิวเหลี่ยม", () => "bpy.ops.object.shade_flat()", "ผิวเหลี่ยม");

        Group(flow, "Deform · บิดรูป");
        Label(flow, "Method · วิธี"); flow.Controls.Add(deformMethod);
        Label(flow, "Axis · แกน"); flow.Controls.Add(deformAxis);
        Label(flow, "Factor · ความแรง"); flow.Controls.Add(deformFactor);
        ActionButton(flow, "Apply Deform · ใช้การบิดรูป", () => $"""
            _mod = _miti_obj.modifiers.new("MitiDeform", 'SIMPLE_DEFORM')
            _mod.deform_method = '{deformMethod.SelectedItem}'
            _mod.deform_axis = '{deformAxis.SelectedItem}'
            _mod.factor = {Decimal(deformFactor)}
            bpy.ops.object.modifier_apply(modifier=_mod.name)
            """, "บิดรูปวัตถุ");

        Group(flow, "Modifiers · ตัวปรับแต่ง");
        Label(flow, "Bevel Width · ความกว้างขอบ"); flow.Controls.Add(bevelWidth);
        Label(flow, "Bevel Segments · จำนวนขั้น"); flow.Controls.Add(bevelSegments);
        ActionButton(flow, "Bevel · ขอบมน", () => $"""
            _mod = _miti_obj.modifiers.new("MitiBevel", 'BEVEL')
            _mod.width = {Decimal(bevelWidth)}
            _mod.segments = {Decimal(bevelSegments)}
            bpy.ops.object.modifier_apply(modifier=_mod.name)
            """, "ทำขอบมน");
        Label(flow, "Thickness · ความหนา"); flow.Controls.Add(thickness);
        ActionButton(flow, "Solidify · เพิ่มความหนา", () => $"""
            _mod = _miti_obj.modifiers.new("MitiSolidify", 'SOLIDIFY')
            _mod.thickness = {Decimal(thickness)}
            bpy.ops.object.modifier_apply(modifier=_mod.name)
            """, "เพิ่มความหนา");
        ActionButton(flow, "Apply All Modifiers · ใช้ตัวปรับแต่งทั้งหมด", () => """
            for _mod in list(_miti_obj.modifiers):
                bpy.ops.object.modifier_apply(modifier=_mod.name)
            """, "ใช้ตัวปรับแต่งทั้งหมด");

        var note = Theme.Label("ทุกคำสั่งทำงานบนวัตถุที่เลือกผ่าน Blender จริง\nEvery operation runs on the selected object in Blender", 8.5f, Theme.Muted);
        note.AutoSize = false; note.Width = 236; note.Height = 52; flow.Controls.Add(note);
        Controls.Add(flow);
    }

    public event EventHandler<string>? ApplyRequested;
    public string? TargetObject => objectName;

    public void SetTargetObject(string? name)
    {
        objectName = name;
        target.Text = name == null ? "เลือกวัตถุก่อนใช้เครื่องมือ" : "วัตถุ · Object: " + name;
    }

    private void Apply(string body, string description)
    {
        if (objectName == null)
        {
            MessageBox.Show(this, "เลือกวัตถุก่อนใช้เครื่องมือ\nSelect an object first.", "ยังไม่ได้เลือกวัตถุ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var code = new StringBuilder()
            .AppendLine().Append("# โครงสร้าง · Topology: ").AppendLine(description)
            .Append("_miti_obj = bpy.data.objects.get(").Append(ApiCatalogService.PythonString(objectName)).AppendLine(")")
            .AppendLine("if _miti_obj is None: raise ValueError('ไม่พบวัตถุที่เลือก · Selected object is missing')")
            .AppendLine("if bpy.context.object and bpy.context.object.mode != 'OBJECT': bpy.ops.object.mode_set(mode='OBJECT')")
            .AppendLine("bpy.ops.object.select_all(action='DESELECT')")
            .AppendLine("_miti_obj.select_set(True)")
            .AppendLine("bpy.context.view_layer.objects.active = _miti_obj")
            .AppendLine(body.Trim())
            .AppendLine("bpy.context.view_layer.update()")
            .ToString();
        ApplyRequested?.Invoke(this, code);
    }

    private static string Decimal(NumericUpDown input) => input.Value.ToString(CultureInfo.InvariantCulture);
    private static string Bool(bool value) => value ? "True" : "False";
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
    private void ActionButton(FlowLayoutPanel flow, string text, Func<string> body, string description)
    {
        var button = Theme.Button(text, (_, _) => Apply(body(), description));
        button.Width = 236; button.Height = 30; button.TextAlign = ContentAlignment.MiddleLeft;
        button.Margin = new Padding(0, 2, 0, 3);
        flow.Controls.Add(button);
    }
    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, int decimals)
    {
        var input = new NumericUpDown { Width = 236, Minimum = minimum, Maximum = maximum, Value = value,
            DecimalPlaces = decimals, Increment = decimals == 0 ? 1 : 0.01m,
            BackColor = Theme.Raised, ForeColor = Theme.Text };
        Theme.UseDarkControls(input); return input;
    }
    private static ComboBox Choice(params string[] items)
    {
        var combo = new StudioComboBox { Width = 236 };
        combo.Items.AddRange(items); combo.SelectedIndex = 0; return combo;
    }
}
