using System.Diagnostics;
using System.Reflection;
using System.Text;
using ThaiModelStudio;
using ThaiModelStudio.Controls;

internal static class Program
{
    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize(); Application.SetDefaultFont(new Font("Leelawadee UI", 10));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ThaiModelStudio.csproj"))) directory = directory.Parent;
        string root = directory?.FullName ?? throw new Exception("Workspace not found");
        Environment.SetEnvironmentVariable("MITI_API_CATALOG", Path.Combine(root, "engine", "blender_api.json"));
        using var main = new MainForm(); main.CreateControl();
        Console.WriteLine("MainForm created");
        using var dialog = new ApiCatalogForm(BlenderService.FindBlender) { Opacity = 0, ShowInTaskbar = false };
        dialog.Show();
        var grid = Field<DataGridView>(dialog, "list"); var count = Field<Label>(dialog, "count");
        Wait(() => count.Text.StartsWith("แสดง"));
        Console.WriteLine("Catalog loaded: " + grid.RowCount);
        if (grid.RowCount < 20000) throw new Exception("Full catalog did not load: " + count.Text);
        var entries = Field<List<ApiEntry>>(dialog, "visible");
        if (entries.Count(e => e.Kind == "operator") != 2499) throw new Exception("Operator coverage lost in WinForms deserialization");
        var snippets = new List<string>();
        void Select(string id)
        {
            Console.WriteLine("Select " + id);
            int index = entries.FindIndex(e => e.Id == id); if (index < 0) throw new Exception("Missing " + id);
            grid.CurrentCell = grid.Rows[index].Cells[0]; Application.DoEvents();
        }
        var parameters = Field<DataGridView>(dialog, "parameters");
        var code = Field<TextBox>(dialog, "code");
        void Parameter(string id, string value)
        {
            foreach (DataGridViewRow row in parameters.Rows)
                if (row.Tag is ApiParameter p && p.Id == id) { row.Cells[0].Value = true; row.Cells[2].Value = value; Application.DoEvents(); return; }
            throw new Exception("Missing parameter " + id);
        }
        Select("bpy.ops.mesh.primitive_cube_add");
        Parameter("size", "4"); Parameter("location", "(2, 0, 2)"); Parameter("scale", "(1, 1, 1)");
        snippets.Add(code.Text); snippets.Add("วัตถุแรก = bpy.context.object\nassert tuple(วัตถุแรก.location) == (2, 0, 2)\nassert tuple(วัตถุแรก.dimensions) == (4, 4, 4)");
        if (!code.Text.Contains("ขนาด_2=")) throw new Exception("Translated size/scale collision not disambiguated");
        Select("bpy.types.Object.location");
        Field<TextBox>(dialog, "target").Text = "วัตถุแรก";
        parameters.Rows[0].Cells[2].Value = "(3, 4, 5)"; Application.DoEvents();
        snippets.Add(code.Text); snippets.Add("assert tuple(วัตถุแรก.location) == (3, 4, 5)");
        Select("bpy.types.Mesh.from_pydata"); Field<TextBox>(dialog, "target").Text = "เมช";
        Parameter("vertices", "[(0,0,0), (1,0,0), (0,1,0)]"); Parameter("edges", "[]"); Parameter("faces", "[(0,1,2)]");
        snippets.Add("เมช = bpy.data.meshes.new('ตรวจเมธอดภาษาไทย')"); snippets.Add(code.Text); snippets.Add("assert len(เมช.polygons) == 1");
        Select("bpy.types.Object"); snippets.Add(code.Text); snippets.Add("assert ชนิดข้อมูล is bpy.types.Object");
        Select("mathutils.Vector"); Field<TextBox>(dialog, "positional").Text = "(1, 2, 3)";
        Console.WriteLine("Vector source generated");
        snippets.Add(code.Text); snippets.Add("assert tuple(ผลลัพธ์) == (1, 2, 3)");
        var search = Field<TextBox>(dialog, "search"); search.Text = "เพิ่มลูกบาศก์";
        Console.WriteLine("Thai query set");
        Wait(() => grid.RowCount < 100);
        if (grid.RowCount < 1) throw new Exception("Thai query did not find cube");
        Console.WriteLine("Thai query passed");
        using var image = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        string artifacts = Path.Combine(root, "artifacts"); Directory.CreateDirectory(artifacts);
        image.Save(Path.Combine(artifacts, "api-catalog-ui.png"));
        File.WriteAllText(Path.Combine(artifacts, "generated-thai-api.py"), "ล้างฉาก()\n" + string.Join("\n", snippets) + "\nprint('GENERATED_THAI_API_PASS')\n", new UTF8Encoding(false));
        dialog.Close();
        Console.WriteLine("PASS: MainForm constructor, 22k-entry load, 2499 operators, Thai query, 5 kinds of generated source, duplicate parameter keys, native form render.");
    }
    private static void Wait(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate()) { if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Catalog UI did not settle"); Application.DoEvents(); Thread.Sleep(10); }
    }
}
