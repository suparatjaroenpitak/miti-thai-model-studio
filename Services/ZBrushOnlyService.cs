using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ThaiModelStudio;

public sealed record ZBrushJobFiles(string ScriptPath, string JobPath, string ResultPath);

public sealed class ZBrushOnlyService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static string? FindZBrush()
    {
        var configured = Environment.GetEnvironmentVariable("MITI_ZBRUSH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return Path.GetFullPath(configured);
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!Directory.Exists(root)) return null;
        return Directory.GetDirectories(root, "Maxon ZBrush*")
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.Combine(path, "ZBrush.exe"))
            .FirstOrDefault(File.Exists);
    }

    public ZBrushJobFiles CreateJob(string directory, IReadOnlyList<ZBrushAction> actions)
    {
        if (actions.Count == 0) throw new ArgumentException("ยังไม่มีคำสั่ง · Add an action first", nameof(actions));
        var runner = Path.Combine(AppContext.BaseDirectory, "zbrush", "runner.py");
        if (!File.Exists(runner)) throw new FileNotFoundException("ไม่พบ ZBrush runner.py", runner);
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var jobPath = Path.Combine(directory, "job.json");
        var resultPath = Path.Combine(directory, "result.json");
        var scriptPath = Path.Combine(directory, "run_zbrush.py");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(new { actions, result = resultPath }, JsonOptions), new UTF8Encoding(false));
        // JSON string literals are valid Python string literals, including escaped Unicode and backslashes.
        var script = "import os, runpy\n" +
                     "os.environ['MITI_ZBRUSH_JOB'] = " + JsonSerializer.Serialize(jobPath) + "\n" +
                     "runpy.run_path(" + JsonSerializer.Serialize(runner) + ", run_name='__main__')\n";
        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        return new ZBrushJobFiles(scriptPath, jobPath, resultPath);
    }

    public void ExportStandaloneScript(string destination, IReadOnlyList<ZBrushAction> actions)
    {
        if (actions.Count == 0) throw new ArgumentException("ยังไม่มีคำสั่ง", nameof(actions));
        var runner = Path.Combine(AppContext.BaseDirectory, "zbrush", "runner.py");
        var source = File.ReadAllText(runner, Encoding.UTF8);
        var actionJson = JsonSerializer.Serialize(actions, JsonOptions);
        var script = "# มิติ ZBrush ไทย · โหลดผ่าน ZBrush > ZScript > Python Scripting > Load\n" +
                     "import json\n" +
                     "_miti_runner = {'__name__': 'miti_embedded'}\n" +
                     "exec(compile(" + JsonSerializer.Serialize(source) + ", '<miti-runner>', 'exec'), _miti_runner)\n" +
                     "from zbrush import commands as zbc\n" +
                     "for _miti_action in json.loads(" + JsonSerializer.Serialize(actionJson) + "):\n" +
                     "    _miti_runner['execute_action'](zbc, _miti_action)\n";
        File.WriteAllText(Path.GetFullPath(destination), script, new UTF8Encoding(false));
    }

    public async Task<string> RunAsync(string executable, ZBrushJobFiles files, string? projectPath,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("ไม่พบ ZBrush.exe", executable);
        if (!string.IsNullOrWhiteSpace(projectPath) && !File.Exists(projectPath))
            throw new FileNotFoundException("ไม่พบไฟล์โปรเจกต์ ZBrush", projectPath);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        start.ArgumentList.Add("-script");
        start.ArgumentList.Add(files.ScriptPath);
        // Maxon's CLI treats a trailing ZPR argument as the project to load.
        if (!string.IsNullOrWhiteSpace(projectPath)) start.ArgumentList.Add(Path.GetFullPath(projectPath));
        using var process = new Process { StartInfo = start };
        var lines = new Queue<string>();
        object gate = new();
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (gate) { lines.Enqueue(line); while (lines.Count > 40) lines.Dequeue(); }
            progress?.Report(line);
        }
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("เปิด ZBrush ไม่สำเร็จ");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!File.Exists(files.ResultPath) && !process.HasExited && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool critical;
            lock (gate) critical = lines.Any(line => line.Contains("CRITICAL:", StringComparison.OrdinalIgnoreCase));
            if (critical) break;
            await Task.Delay(350, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(files.ResultPath))
        {
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(files.ResultPath, cancellationToken));
            var root = result.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException("คำสั่ง ZBrush ไม่สำเร็จ: " + root.GetProperty("error").GetString());
            return "รันสำเร็จ " + root.GetProperty("completed").GetInt32() + " คำสั่ง" +
                (root.TryGetProperty("activeTool", out var tool) ? " · " + tool.GetString() : "");
        }
        string details;
        lock (gate) details = string.Join(Environment.NewLine, lines);
        if (details.Contains("DigiCert", StringComparison.OrdinalIgnoreCase) ||
            details.Contains("libraries seem to be broken", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ZBrush ตรวจลายเซ็นโปรแกรมไม่ผ่าน กรุณาตรวจการติดตั้งและใบรับรอง Windows\n" + details);
        process.Refresh();
        string windowTitle = "";
        try { if (!process.HasExited) windowTitle = process.MainWindowTitle; }
        catch (InvalidOperationException) { }
        if (windowTitle.Contains("SplashScreen", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ZBrush ยังอยู่ที่ SplashScreen และไม่เริ่ม Python API กรุณาเปิด ZBrush ด้วยตนเองและตรวจสิทธิ์ใช้งานใน Maxon App\nสคริปต์ที่เตรียมไว้: " + files.ScriptPath);
        throw new InvalidOperationException("ZBrush ไม่ส่งผลกลับมา" + (details.Length == 0 ? "" : "\n" + details) +
            "\nสคริปต์ที่เตรียมไว้: " + files.ScriptPath);
    }
}
