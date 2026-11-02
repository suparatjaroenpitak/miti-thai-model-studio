using System.Reflection;
using ThaiModelStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        using var main = new MainForm();
        main.CreateControl();
        var method = typeof(MainForm).GetMethod("OpenObjectActions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var menu = (ContextMenuStrip)typeof(MainForm).GetField("objectMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        var search = (ToolStripTextBox)typeof(MainForm).GetField("objectMenuSearch", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        for (var round = 0; round < 2; round++)
        {
            method.Invoke(main, ["CubeActionSmoke"]);
            search.Text = "subdivide";
            Application.DoEvents();
            if (menu.IsDisposed) throw new Exception("Context menu was disposed while in use");
            menu.Close();
            Application.DoEvents();
        }
        Console.WriteLine("ACTION_UI_SUCCESS: context menu reused after close");
    }
}
