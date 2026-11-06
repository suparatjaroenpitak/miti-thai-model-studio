using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ThaiModelStudio;

/// <summary>Optional OBJ handoff to Maxon's installed ZBrush via its documented Python API.</summary>
public sealed class ZBrushService
{
    public static string? FindZBrush()
    {
        var configured = Environment.GetEnvironmentVariable("MITI_ZBRUSH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (Directory.Exists(root))
            foreach (var directory in Directory.GetDirectories(root, "Maxon ZBrush*"))
            {
                var executable = Path.Combine(directory, "ZBrush.exe");
                if (File.Exists(executable)) return executable;
            }
        return null;
    }

    public async Task OpenObjAsync(string executable, string source, string jobDirectory,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("ไม่พบ ZBrush.exe", executable);
        if (!File.Exists(source) || new FileInfo(source).Length == 0) throw new FileNotFoundException("ไม่พบไฟล์ OBJ", source);
        var script = Path.Combine(AppContext.BaseDirectory, "engine", "zbrush_open.py");
        if (!File.Exists(script)) throw new FileNotFoundException("ไม่พบสคริปต์เชื่อม ZBrush", script);
        Directory.CreateDirectory(jobDirectory);
        var result = Path.Combine(jobDirectory, "zbrush-result.json");
        var job = Path.Combine(jobDirectory, "zbrush-job.json");
        await File.WriteAllTextAsync(job, JsonSerializer.Serialize(new { source, result }), Encoding.UTF8, cancellationToken);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("-script");
        start.ArgumentList.Add(script);
        start.Environment["MITI_ZBRUSH_JOB"] = job;
        using var process = new Process { StartInfo = start };
        var lines = new Queue<string>();
        object gate = new();
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (gate) { lines.Enqueue(line); while (lines.Count > 25) lines.Dequeue(); }
            progress?.Report(line);
        }
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        if (!process.Start()) throw new InvalidOperationException("เปิด ZBrush ไม่สำเร็จ");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var deadline = DateTime.UtcNow.AddSeconds(75);
        while (!File.Exists(result) && !process.HasExited && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool failed;
            lock (gate) failed = lines.Any(line => line.Contains("CRITICAL:", StringComparison.OrdinalIgnoreCase));
            if (failed) break;
            await Task.Delay(400, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(result))
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result, cancellationToken));
            if (document.RootElement.GetProperty("ok").GetBoolean()) return;
            throw new InvalidOperationException("ZBrush import ไม่สำเร็จ: " + document.RootElement.GetProperty("error").GetString());
        }
        string details;
        lock (gate) details = string.Join(Environment.NewLine, lines);
        if (details.Contains("DigiCert", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("libraries seem to be broken", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ZBrush เริ่มไม่สำเร็จ: ระบบตรวจใบรับรอง DigiCert ของไฟล์โปรแกรมไม่ผ่าน กรุณาซ่อมการติดตั้ง ZBrush/ใบรับรอง Windows แล้วลองใหม่\n" + details);
        process.Refresh();
        if (!process.HasExited && process.MainWindowTitle.Contains("SplashScreen", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ZBrush ยังอยู่ที่หน้า SplashScreen และยังไม่รัน Python API ตรวจสถานะสิทธิ์ใช้งานใน Maxon App แล้วลองเปิด ZBrush ด้วยตนเองก่อน\nไฟล์ OBJ ที่เตรียมไว้: " + source);
        throw new InvalidOperationException("ZBrush ไม่ส่งผลการนำเข้ากลับมา" + (details.Length == 0 ? "" : "\n" + details));
    }
}
