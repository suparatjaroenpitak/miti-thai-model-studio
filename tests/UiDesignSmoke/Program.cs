using System.Diagnostics;
using ThaiModelStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(new Font("Leelawadee UI", 10));
        using var form = new MainForm { Size = new Size(1560, 950), ShowInTaskbar = false, Opacity = 0 };
        form.Show();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(9)) { Application.DoEvents(); Thread.Sleep(50); }
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ThaiModelStudio.csproj"))) directory = directory.Parent;
        var path = Path.Combine(directory!.FullName, "artifacts", "ui-design-smoke.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.Save(path);
        Console.WriteLine(path);
    }
}
