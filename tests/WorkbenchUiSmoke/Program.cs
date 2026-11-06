using System.Reflection;
using ThaiModelStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new BlenderWorkbenchForm { ShowInTaskbar = false };
        form.Show(); Application.DoEvents();
        var type = typeof(BlenderWorkbenchForm);
        var tree = (TreeView)type.GetField("tree", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var actions = (ListBox)type.GetField("actions", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var editor = (ComboBox)type.GetField("editor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var mode = (ComboBox)type.GetField("mode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        var selection = (ComboBox)type.GetField("selection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        if (tree.Nodes.Count == 0 || !tree.Nodes[0].Text.Contains(" · ") ||
            !editor.Items[0]!.ToString()!.StartsWith("View 3D · ", StringComparison.Ordinal))
            throw new Exception("Blender menu labels are not English-first bilingual");
        bool found = false;
        foreach (TreeNode node in tree.Nodes)
        {
            tree.SelectedNode = node; Application.DoEvents();
            foreach (var item in actions.Items)
            {
                var action = (BlenderMenuAction)item!.GetType().GetProperty("Action")!.GetValue(item)!;
                if (!action.Resolved || action.Kind != "operator") continue;
                actions.SelectedItem = item; found = true; break;
            }
            if (found) break;
        }
        if (!found) throw new Exception("No resolved Blender menu action found");
        mode.SelectedIndex = 3; selection.SelectedIndex = 3;
        using (var image = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "ThaiModelStudio.csproj"))) root = root.Parent;
            image.Save(Path.Combine(root!.FullName, "artifacts", "blender-menus-bilingual.png"));
        }
        type.GetMethod("AcceptSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
        if (form.SelectedCode is null || !form.SelectedCode.Contains("with บริบท(\"มุมมอง3มิติ\", mode=\"ปั้น\", selection=\"หน้า\")"))
            throw new Exception("Bilingual choices did not preserve Blender context values");
        Console.WriteLine("WORKBENCH_UI_SUCCESS: bilingual menus and preserved context code");
    }
}
