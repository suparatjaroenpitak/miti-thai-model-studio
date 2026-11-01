using System.Diagnostics;
using System.Text.Json;
using ThaiModelStudio.Controls;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts"));
        Directory.CreateDirectory(artifacts);
        string preview = Path.Combine(artifacts, "viewport-smoke.json");
        File.WriteAllText(preview, """
        {"objects":[{"name":"กล่องทดสอบ","vertices":[[-1,-1,0],[1,-1,0],[1,1,0],[-1,1,0],[-1,-1,2],[1,-1,2],[1,1,2],[-1,1,2]],"triangles":[[0,2,1],[0,3,2],[4,5,6],[4,6,7],[0,1,5],[0,5,4],[1,2,6],[1,6,5],[2,3,7],[2,7,6],[3,0,4],[3,4,7]],"color":[0.25,0.75,0.6,1]}]}
        """);
        using var viewport = new ModelViewport { Size = new Size(960, 640) };
        viewport.CreateControl();
        viewport.LoadScene(preview);
        if (viewport.Objects.Count != 1 || viewport.VertexCount != 8 || viewport.TriangleCount != 12)
            throw new Exception("Loaded mesh statistics differ from real input.");
        viewport.SelectedObject = "กล่องทดสอบ";
        viewport.SaveScreenshot(Path.Combine(artifacts, "viewport-smoke.png"));
        foreach (string view in new[] { "top", "front", "right", "isometric" })
        {
            viewport.SetView(view);
            using var bitmap = new Bitmap(960, 640);
            viewport.DrawToBitmap(bitmap, new Rectangle(0, 0, 960, 640));
        }
        viewport.Wireframe = true;
        viewport.SaveScreenshot(Path.Combine(artifacts, "viewport-wireframe-smoke.png"));
        string invalidPreview = Path.Combine(artifacts, "viewport-invalid-smoke.json");
        File.WriteAllText(invalidPreview, """{"objects":[{"name":"invalid","vertices":[[0,0,0]],"triangles":[[0,1,2]]}]}""");
        try { viewport.LoadScene(invalidPreview); throw new Exception("Invalid mesh should fail."); }
        catch (InvalidDataException) { }
        if (viewport.TriangleCount != 12 || viewport.SelectedObject != "กล่องทดสอบ")
            throw new Exception("Failed load replaced successful scene.");

        // A tessellated sphere exercises the agreed 60k-triangle upper preview bound.
        string stress = Path.Combine(artifacts, "viewport-stress-smoke.json");
        var sphereVertices = new List<double[]>();
        var sphereTriangles = new List<int[]>();
        const int longitudeCount = 200, latitudeCount = 150;
        for (int latitude = 0; latitude <= latitudeCount; latitude++)
        {
            double pitch = Math.PI * latitude / latitudeCount;
            for (int longitude = 0; longitude <= longitudeCount; longitude++)
            {
                double yaw = 2 * Math.PI * longitude / longitudeCount;
                sphereVertices.Add([Math.Sin(pitch) * Math.Cos(yaw), Math.Sin(pitch) * Math.Sin(yaw), Math.Cos(pitch)]);
                if (latitude == 0 || longitude == 0) continue;
                int a = (latitude - 1) * (longitudeCount + 1) + longitude - 1;
                int b = a + 1, c = latitude * (longitudeCount + 1) + longitude - 1, d = c + 1;
                sphereTriangles.Add([a, c, b]);
                sphereTriangles.Add([b, c, d]);
            }
        }
        File.WriteAllText(stress, JsonSerializer.Serialize(new
        {
            objects = new[] { new { name = "60000 triangles", vertices = sphereVertices, triangles = sphereTriangles, color = new[] { 0.3, 0.6, 0.8, 1.0 } } }
        }));
        viewport.LoadScene(stress);
        viewport.Wireframe = false;
        var timer = Stopwatch.StartNew();
        viewport.SaveScreenshot(Path.Combine(artifacts, "viewport-stress-smoke.png"));
        timer.Stop();
        Console.WriteLine($"PASS: load 8 vertices/12 faces; 4 camera views; wireframe; invalid input preserves scene; 60000-triangle render {timer.ElapsedMilliseconds} ms.");
        Console.WriteLine(Path.Combine(artifacts, "viewport-smoke.png"));
        return 0;
    }
}
