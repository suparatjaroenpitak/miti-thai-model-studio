using System.Diagnostics;
using System.Text;

namespace ThaiModelStudio;

public record BuildResult(string ScenePath, string PreviewPath, string GlbPath);

public sealed class BlenderService
{
    public bool UseWindowContext { get; set; }
    public static string? FindBlender()
    {
        string? configured = Environment.GetEnvironmentVariable("THAI_MODEL_BLENDER");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var bases = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        foreach (var root in bases.Where(Directory.Exists))
        {
            string folder = Path.Combine(root, "Blender Foundation");
            if (!Directory.Exists(folder)) continue;
            foreach (string directory in Directory.GetDirectories(folder).OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(directory, "blender.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                string candidate = Path.Combine(directory.Trim('"'), "blender.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    public async Task<BuildResult> BuildAsync(string blenderPath, string source, string outputDirectory,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        // Every invocation owns a new directory; a failed script cannot invalidate a good scene.
        string job = Path.Combine(Path.GetFullPath(outputDirectory), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(job);
        string sourcePath = Path.Combine(job, "source.py");
        await File.WriteAllTextAsync(sourcePath, source, new UTF8Encoding(false), cancellationToken);
        var arguments = new List<string>();
        if (!UseWindowContext) arguments.Add("--background");
        arguments.AddRange(["--factory-startup", "--python-exit-code", "1", "--python", RunnerPath(), "--", "--source", sourcePath, "--output", job]);
        if (UseWindowContext) arguments.Add("--window-context");
        await RunAsync(blenderPath, arguments, progress, cancellationToken);
        var result = new BuildResult(Path.Combine(job, "scene.blend"), Path.Combine(job, "preview.json"), Path.Combine(job, "scene.glb"));
        if (!File.Exists(Path.Combine(job, "build-success.json")) ||
            new[] { result.ScenePath, result.PreviewPath, result.GlbPath }.Any(p => !File.Exists(p) || new FileInfo(p).Length == 0))
            throw new InvalidOperationException("Blender ทำงานไม่สำเร็จ: ไม่พบไฟล์ผลลัพธ์ครบถ้วน ดูรายละเอียดในบันทึกด้านล่าง");
        return result;
    }

    public async Task ExportAsync(string blenderPath, string blendPath, string destination, string format,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        format = format.TrimStart('.').ToLowerInvariant();
        if (format is not ("blend" or "glb" or "obj" or "stl")) throw new ArgumentException("รูปแบบไฟล์ไม่รองรับ", nameof(format));
        if (!File.Exists(blendPath)) throw new FileNotFoundException("ไม่พบฉากที่สร้างไว้ กรุณากดสร้างโมเดลอีกครั้ง", blendPath);
        string fullDestination = Path.GetFullPath(destination);
        string? parent = Path.GetDirectoryName(fullDestination);
        if (parent is not null) Directory.CreateDirectory(parent);
        cancellationToken.ThrowIfCancellationRequested();
        // Export beside the destination on the same volume, then publish only
        // after Blender and cancellation checks have succeeded. Keep OBJ's .mtl
        // basename intact by using a private sibling directory.
        string staging = Path.Combine(parent!, ".miti-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string stagedDestination = Path.Combine(staging, Path.GetFileName(fullDestination));
        try
        {
            if (format == "blend")
            {
                await using var input = new FileStream(blendPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var output = new FileStream(stagedDestination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
            }
            else
            {
                await RunAsync(blenderPath, ["--background", Path.GetFullPath(blendPath), "--python-exit-code", "1", "--python", RunnerPath(), "--", "--export", stagedDestination, "--format", format], progress, cancellationToken);
            }
            if (!File.Exists(stagedDestination) || new FileInfo(stagedDestination).Length == 0)
                throw new InvalidOperationException("ส่งออกไม่สำเร็จ: ไม่พบไฟล์ผลลัพธ์");
            cancellationToken.ThrowIfCancellationRequested();
            CommitExport(staging, parent!, stagedDestination);
            progress?.Report("บันทึกไฟล์แล้ว: " + fullDestination);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void CommitExport(string staging, string parent, string primary)
    {
        string backups = Path.Combine(staging, "_backup");
        var moves = Directory.GetFiles(staging, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => string.Equals(path, primary, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .Select(path => (Source: path, Target: Path.Combine(parent, Path.GetFileName(path))))
            .ToArray();
        var committed = new List<(string Target, string? Backup)>();
        Directory.CreateDirectory(backups);
        try
        {
            foreach (var move in moves)
            {
                string? backup = null;
                if (File.Exists(move.Target))
                {
                    backup = Path.Combine(backups, Path.GetFileName(move.Target));
                    File.Move(move.Target, backup);
                }
                try { File.Move(move.Source, move.Target); }
                catch
                {
                    if (backup is not null) File.Move(backup, move.Target);
                    throw;
                }
                committed.Add((move.Target, backup));
            }
        }
        catch
        {
            foreach (var item in committed.AsEnumerable().Reverse())
            {
                File.Delete(item.Target);
                if (item.Backup is not null) File.Move(item.Backup, item.Target);
            }
            throw;
        }
    }

    private static string RunnerPath()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "engine", "runner.py");
        if (!File.Exists(path)) throw new FileNotFoundException("ไม่พบ engine/runner.py กรุณาติดตั้งแอปให้ครบโฟลเดอร์", path);
        return path;
    }

    private static async Task RunAsync(string blenderPath, IEnumerable<string> arguments,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(blenderPath)) throw new FileNotFoundException("ไม่พบ Blender กรุณาเลือก blender.exe ในเมนูตั้งค่า", blenderPath);
        var start = new ProcessStartInfo(blenderPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUTF8"] = "1";
        var customCatalog = Path.Combine(AppContext.BaseDirectory, "UserData", "ApiCatalog", "blender_api.json");
        var apiCatalog = Environment.GetEnvironmentVariable("MITI_API_CATALOG");
        if (string.IsNullOrWhiteSpace(apiCatalog)) apiCatalog = File.Exists(customCatalog) ? customCatalog : Path.Combine(AppContext.BaseDirectory, "engine", "blender_api.json");
        start.Environment["MITI_API_CATALOG"] = apiCatalog;
        using var process = new Process { StartInfo = start };
        var tail = new Queue<string>();
        object gate = new();
        void OnLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (gate)
            {
                tail.Enqueue(line);
                while (tail.Count > 80) tail.Dequeue();
            }
            progress?.Report(line);
        }
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException("เปิด Blender ไม่สำเร็จ");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit(); // Drain async redirected stdout/stderr before reading the status.
        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            string details;
            lock (gate) details = string.Join(Environment.NewLine, tail);
            throw new InvalidOperationException("Blender สร้างโมเดลไม่สำเร็จ (รหัส " + process.ExitCode + ")\n" + details);
        }
    }
}
