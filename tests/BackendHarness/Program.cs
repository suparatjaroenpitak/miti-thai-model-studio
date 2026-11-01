using System.Text.Json;
using ThaiModelStudio;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string output = Path.Combine(root, "artifacts", "service-smoke-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(output);
string blender = BlenderService.FindBlender() ?? throw new Exception("Installed Blender not found");
var service = new BlenderService();
var log = new List<string>();
var progress = new ImmediateProgress(line => { lock (log) log.Add(line); });
var cases = new List<string>();
void Assert(bool value, string message) { if (!value) throw new Exception(message); }

BuildResult success = await service.BuildAsync(blender, "สี = วัสดุ('สี', '#62B6ED')\nกล่อง('กล่อง', material=สี)\n", output, progress, CancellationToken.None);
Assert(File.Exists(success.ScenePath) && File.Exists(success.GlbPath), "Build result files missing");
using (var preview = JsonDocument.Parse(await File.ReadAllTextAsync(success.PreviewPath)))
{
    var obj = preview.RootElement.GetProperty("objects")[0];
    var color = obj.GetProperty("color");
    Assert(Math.Abs(color[0].GetDouble() - 0.122138772) < 0.0001, "Hex sRGB was not converted to linear");
    Assert(obj.GetProperty("name").GetString() == "กล่อง", "Unicode object name missing");
}
cases.Add("C# build / Unicode / hex color");

try
{
    await service.BuildAsync(blender, "raise ValueError('ทดสอบ traceback')", output, progress, CancellationToken.None);
    throw new Exception("Invalid source unexpectedly succeeded");
}
catch (InvalidOperationException e)
{
    Assert(e.Message.Contains("ValueError") && e.Message.Contains("traceback"), "Build failure lost Python traceback");
}
Assert(File.Exists(success.ScenePath), "Failed build damaged previous successful scene");
cases.Add("C# invalid source preserves previous build and traceback");

var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
{
    var slowProgress = new ImmediateProgress(line => { if (line.Contains("Building model...")) started.TrySetResult(); });
    Task<BuildResult> slow = service.BuildAsync(blender, "import time\ntime.sleep(30)\nกล่อง()", output, slowProgress, cancellation.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(15));
    cancellation.Cancel();
    try { await slow; throw new Exception("Cancellation unexpectedly succeeded"); }
    catch (OperationCanceledException) { }
}
Assert(Directory.GetFiles(output, "build-success.json", SearchOption.AllDirectories).Length == 1, "Cancelled build published artifacts");
cases.Add("C# cancellation kills Blender and publishes no success");

string objPath = Path.Combine(output, "ส่งออก.obj");
await service.ExportAsync(blender, success.ScenePath, objPath, "obj", progress, CancellationToken.None);
string objText = await File.ReadAllTextAsync(objPath);
Assert(objText.Contains("mtllib ส่งออก.mtl") && File.Exists(Path.ChangeExtension(objPath, ".mtl")), "Staged OBJ lost its matching material sidecar");
cases.Add("C# staged OBJ + matching Unicode MTL");

byte[] original = [1, 2, 3, 4, 5, 6, 7, 8];
foreach (string format in new[] { "blend", "glb" })
{
    string destination = Path.Combine(output, "existing." + format);
    await File.WriteAllBytesAsync(destination, original);
    using var cancellation = new CancellationTokenSource();
    if (format == "blend") cancellation.Cancel();
    else cancellation.CancelAfter(100);
    try { await service.ExportAsync(blender, success.ScenePath, destination, format, progress, cancellation.Token); throw new Exception("Cancelled export succeeded"); }
    catch (OperationCanceledException) { }
    Assert((await File.ReadAllBytesAsync(destination)).SequenceEqual(original), "Cancelled export damaged existing " + format);
}
Assert(!Directory.GetDirectories(output, ".miti-export-*").Any(), "Export staging directory leaked");
cases.Add("C# cancelled blend/glb exports preserve existing destination");

await File.WriteAllTextAsync(Path.Combine(output, "service.log"), string.Join(Environment.NewLine, log));
await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { passed = cases.Count, output, cases }, new JsonSerializerOptions { WriteIndented = true }));

sealed class ImmediateProgress(Action<string> report) : IProgress<string>
{
    public void Report(string value) => report(value);
}
