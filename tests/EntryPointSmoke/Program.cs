using ThaiModelStudio;

var entry = typeof(MainForm).Assembly.EntryPoint ?? throw new Exception("Application entry point was not found");
if (!Attribute.IsDefined(entry, typeof(STAThreadAttribute)))
    throw new Exception("The app entry point is missing [STAThread]");
Console.WriteLine("STA_ENTRYPOINT_SUCCESS: " + entry.DeclaringType?.FullName + "." + entry.Name);
