using ThaiModelStudio.Controls;

namespace ThaiModelStudio;

/// <summary>Blender menu inventory with English labels followed by Thai labels.</summary>
public sealed class BlenderWorkbenchForm : Form
{
    private sealed record Choice(string Value, string Label) { public override string ToString() => Label; }
    private sealed record Item(BlenderMenuAction Action)
    {
        public override string ToString() => ApiCatalogService.BilingualName(Action.Label, Action.ThaiLabel) + " · " + Action.Id;
    }

    private readonly BlenderMenuService service = new();
    private readonly TreeView tree = new() { Dock = DockStyle.Left, Width = 320, BackColor = Theme.Panel,
        ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly ListBox actions = new() { Dock = DockStyle.Fill, BackColor = Theme.Background,
        ForeColor = Theme.Text, BorderStyle = BorderStyle.None };
    private readonly TextBox search = new() { Width = 245, PlaceholderText = "Search · ค้นหาเมนู Blender…",
        BackColor = Theme.Raised, ForeColor = Theme.Text };
    private readonly ComboBox editor = new StudioComboBox { Width = 180 };
    private readonly ComboBox mode = new StudioComboBox { Width = 150 };
    private readonly ComboBox selection = new StudioComboBox { Width = 125 };
    public string? SelectedCode { get; private set; }
    public bool RequiresWindowContext { get; private set; }

    public BlenderWorkbenchForm()
    {
        Text = "Blender Menus · เมนู Blender — มิติ สตูดิโอ";
        BackColor = Theme.Background; ForeColor = Theme.Text;
        Size = new Size(1080, 700); StartPosition = FormStartPosition.CenterParent;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, BackColor = Theme.Panel,
            WrapContents = false, Padding = new Padding(10, 9, 10, 0) };
        editor.Items.AddRange([
            new Choice("มุมมอง3มิติ", "View 3D · มุมมอง 3 มิติ"), new Choice("ยูวี", "UV Editor · ยูวี"),
            new Choice("ภาพ", "Image Editor · ภาพ"), new Choice("วัสดุ", "Shader · วัสดุ"),
            new Choice("โหนดเรขาคณิต", "Geometry Nodes · โหนด"), new Choice("ตัวแก้ไขข้อความ", "Text Editor · โค้ด")]);
        mode.Items.AddRange([
            new Choice("อัตโนมัติ", "Auto · อัตโนมัติ"), new Choice("วัตถุ", "Object · วัตถุ"),
            new Choice("แก้ไข", "Edit · แก้ไข"), new Choice("ปั้น", "Sculpt · ปั้น"),
            new Choice("ระบายพื้นผิว", "Texture Paint · ลงสี"), new Choice("ระบายจุดยอด", "Vertex Paint · ลงสีจุด"),
            new Choice("โพส", "Pose · โพส")]);
        selection.Items.AddRange([
            new Choice("อัตโนมัติ", "Auto · อัตโนมัติ"), new Choice("จุดยอด", "Vertex · จุดยอด"),
            new Choice("ขอบ", "Edge · ขอบ"), new Choice("หน้า", "Face · หน้า")]);
        editor.SelectedIndex = mode.SelectedIndex = selection.SelectedIndex = 0;
        var insert = Theme.Button("Insert · แทรกโค้ด", (_, _) => AcceptSelection(), true);
        insert.Width = 135; insert.Height = 30;
        top.Controls.AddRange([search, editor, mode, selection, insert]);
        Controls.Add(actions); Controls.Add(tree); Controls.Add(top);
        Theme.UseDarkControls(search, tree, actions, editor, mode, selection);
        tree.AfterSelect += (_, _) => FillActions();
        actions.DoubleClick += (_, _) => AcceptSelection();
        search.TextChanged += (_, _) => FillTree();
        FillTree();
    }

    private void FillTree()
    {
        tree.Nodes.Clear();
        var query = search.Text.Trim();
        foreach (var item in service.Menus.Where(item => query.Length == 0
            || item.ThaiName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            tree.Nodes.Add(new TreeNode(ApiCatalogService.BilingualName(item.Name, item.ThaiName)) { Tag = item });
        if (tree.Nodes.Count > 0) tree.SelectedNode = tree.Nodes[0];
    }

    private void FillActions()
    {
        actions.Items.Clear();
        if (tree.SelectedNode?.Tag is not BlenderMenu menu) return;
        foreach (var item in menu.Items) actions.Items.Add(new Item(item));
        if (actions.Items.Count > 0) actions.SelectedIndex = 0;
    }

    private void AcceptSelection()
    {
        if (tree.SelectedNode?.Tag is not BlenderMenu menu || actions.SelectedItem is not Item selected) return;
        string editorValue = ((Choice)editor.SelectedItem!).Value;
        SelectedCode = BlenderMenuService.BuildCode(menu, selected.Action, editorValue,
            ((Choice)mode.SelectedItem!).Value, ((Choice)selection.SelectedItem!).Value);
        RequiresWindowContext = editorValue != "ตัวแก้ไขข้อความ";
        DialogResult = DialogResult.OK; Close();
    }
}
