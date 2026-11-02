using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text.Json;

namespace ThaiModelStudio.Controls;

public sealed record SceneObjectInfo(string Name, int VertexCount, int TriangleCount);

/// <summary>A dependency-free, orbitable viewport for Blender's evaluated mesh preview.</summary>
public sealed class ModelViewport : Control
{
    private sealed class Mesh
    {
        public required string Name { get; init; }
        public required Vector3[] Vertices { get; init; }
        public required int[][] Triangles { get; init; }
        public int[][] Edges { get; init; } = [];
        public int[][] Polygons { get; init; } = [];
        public required Color Color { get; init; }
        public Color[]? TriangleColors { get; init; }
        public Vector3 Minimum { get; init; }
        public Vector3 Maximum { get; init; }
    }

    private readonly record struct ProjectedVertex(PointF Point, float Depth, bool Visible);
    private readonly record struct DrawTriangle(PointF A, PointF B, PointF C, float Depth, Color Color, int ObjectIndex);

    private List<Mesh> _meshes = [];
    private List<SceneObjectInfo> _objects = [];
    private readonly List<DrawTriangle> _triangles = [];
    private Vector3 _target;
    private float _radius = 3;
    private float _distance = 9;
    private float _yaw = -0.85f;
    private float _pitch = 0.48f;
    private bool _wireframe;
    private bool _showTopology;
    private int _previewCuts;
    private bool _showGrid = true;
    private string? _selectedObject;
    private bool _mouseDown;
    private bool _dragged;
    private MouseButtons _dragButton;
    private Point _lastMouse;
    private Point _mouseStart;
    private Vector3 _eye;
    private Vector3 _right;
    private Vector3 _up;
    private Vector3 _forward;
    private float _focal;
    private bool _isSimplified;

    public ModelViewport()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(17, 24, 39);
        ForeColor = Color.FromArgb(199, 211, 228);
        Font = new Font("Leelawadee UI", 9f);
        TabStop = true;
        MinimumSize = new Size(180, 180);
        AccessibleName = "มุมมองโมเดลสามมิติ";
        AccessibleDescription = "คลิกขวาที่โมเดลเพื่อค้นหาคำสั่ง ลากซ้ายเพื่อหมุน ลากขวาเพื่อเลื่อน หมุนลูกล้อเพื่อซูม กด F เพื่อพอดีจอ";
    }

    public IReadOnlyList<SceneObjectInfo> Objects => _objects.AsReadOnly();
    public int TriangleCount => _objects.Sum(item => item.TriangleCount);
    public int VertexCount => _objects.Sum(item => item.VertexCount);
    public bool IsSimplified => _isSimplified;
    public int SourceTriangleCount { get; private set; }
    public bool HasScene => _meshes.Count > 0;
    public void ClearScene()
    {
        _meshes.Clear(); _objects.Clear(); SelectedObject = null; SourceTriangleCount = 0; _isSimplified = false;
        Invalidate(); SceneLoaded?.Invoke(this, EventArgs.Empty);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (_selectedObject == value) return;
            _selectedObject = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Wireframe
    {
        get => _wireframe;
        set { _wireframe = value; Invalidate(); }
    }
    [DefaultValue(false)]
    public bool ShowTopology
    {
        get => _showTopology;
        set { _showTopology = value; Invalidate(); }
    }
    [DefaultValue(0)]
    public int PreviewCuts
    {
        get => _previewCuts;
        set { _previewCuts = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    [DefaultValue(true)]
    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; Invalidate(); }
    }

    public event EventHandler? SceneLoaded;
    public event EventHandler<string>? ObjectSelected;
    public event EventHandler<string>? ObjectContextRequested;

    public void LoadScene(string previewPath, bool preserveView = false)
    {
        var previousTarget = _target;
        var previousRadius = _radius;
        var previousDistance = _distance;
        var previousSelection = _selectedObject;
        using var stream = File.OpenRead(previewPath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        if (!document.RootElement.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("ข้อมูลตัวอย่างโมเดลไม่ถูกต้อง: ไม่พบรายการวัตถุ");

        var meshes = new List<Mesh>();
        foreach (var item in objects.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? "วัตถุ" : "วัตถุ";
            var vertexData = item.GetProperty("vertices");
            var triangleData = item.GetProperty("triangles");
            if (vertexData.GetArrayLength() > 2_000_000 || triangleData.GetArrayLength() > 2_000_000)
                throw new InvalidDataException("โมเดลตัวอย่างใหญ่เกินขีดจำกัดของหน้าต่างแสดงผล");
            var vertices = new Vector3[vertexData.GetArrayLength()];
            var minimum = new Vector3(float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity);
            var index = 0;
            foreach (var point in vertexData.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 3)
                    throw new InvalidDataException($"ตำแหน่งจุดของ {name} ไม่ถูกต้อง");
                var vertex = new Vector3(point[0].GetSingle(), point[1].GetSingle(), point[2].GetSingle());
                if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
                    throw new InvalidDataException($"ตำแหน่งจุดของ {name} มีค่าที่แสดงผลไม่ได้");
                vertices[index++] = vertex;
                minimum = Vector3.Min(minimum, vertex);
                maximum = Vector3.Max(maximum, vertex);
            }

            var triangles = new List<int[]>();
            foreach (var triangle in triangleData.EnumerateArray())
            {
                if (triangle.ValueKind != JsonValueKind.Array || triangle.GetArrayLength() < 3)
                    throw new InvalidDataException($"ข้อมูลหน้าสามเหลี่ยมของ {name} ไม่ถูกต้อง");
                int a = triangle[0].GetInt32(), b = triangle[1].GetInt32(), c = triangle[2].GetInt32();
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length)
                    throw new InvalidDataException($"หน้าสามเหลี่ยมของ {name} อ้างอิงจุดที่ไม่มีอยู่");
                triangles.Add([a, b, c]);
            }
            var edges = new List<int[]>();
            if (item.TryGetProperty("edges", out var edgeData) && edgeData.ValueKind == JsonValueKind.Array)
                foreach (var edge in edgeData.EnumerateArray())
                {
                    if (edge.ValueKind != JsonValueKind.Array || edge.GetArrayLength() != 2) continue;
                    int a = edge[0].GetInt32(), b = edge[1].GetInt32();
                    if (a >= 0 && b >= 0 && a < vertices.Length && b < vertices.Length) edges.Add([a, b]);
                }
            var polygons = new List<int[]>();
            if (item.TryGetProperty("polygons", out var polygonData) && polygonData.ValueKind == JsonValueKind.Array)
                foreach (var face in polygonData.EnumerateArray())
                {
                    if (face.ValueKind != JsonValueKind.Array || face.GetArrayLength() is < 3 or > 64) continue;
                    var indices = face.EnumerateArray().Select(v => v.GetInt32()).ToArray();
                    if (indices.All(v => v >= 0 && v < vertices.Length)) polygons.Add(indices);
                }

            var color = item.TryGetProperty("color", out var colorData)
                ? ReadColor(colorData, Color.FromArgb(255, 106, 176, 214))
                : Color.FromArgb(255, 106, 176, 214);
            Color[]? triangleColors = null;
            if (item.TryGetProperty("triangleColors", out var faceColorData) && faceColorData.ValueKind == JsonValueKind.Array)
                triangleColors = faceColorData.EnumerateArray().Select(value => ReadColor(value, color)).ToArray();
            if (vertices.Length == 0) minimum = maximum = Vector3.Zero;
            meshes.Add(new Mesh
            {
                Name = name, Vertices = vertices, Triangles = triangles.ToArray(), Edges = edges.ToArray(), Polygons = polygons.ToArray(), Color = color,
                TriangleColors = triangleColors, Minimum = minimum, Maximum = maximum
            });
        }

        // A malformed preview never replaces the last successful scene.
        _meshes = meshes;
        _objects = meshes.Select(mesh => new SceneObjectInfo(mesh.Name, mesh.Vertices.Length, mesh.Triangles.Length)).ToList();
        _selectedObject = null;
        _isSimplified = document.RootElement.TryGetProperty("simplified", out var simplified) && simplified.ValueKind == JsonValueKind.True;
        SourceTriangleCount = document.RootElement.TryGetProperty("sourceTriangleCount", out var sourceCount)
            && sourceCount.TryGetInt32(out int count) ? count : TriangleCount;
        _triangles.Clear();
        FitScene();
        if (preserveView)
        {
            _target = previousTarget; _radius = previousRadius; _distance = previousDistance;
            SelectedObject = previousSelection;
        }
        SceneLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void FitScene()
    {
        var nonempty = _meshes.Where(mesh => mesh.Vertices.Length > 0).ToList();
        if (nonempty.Count > 0)
        {
            var minimum = nonempty.Aggregate(new Vector3(float.PositiveInfinity), (current, mesh) => Vector3.Min(current, mesh.Minimum));
            var maximum = nonempty.Aggregate(new Vector3(float.NegativeInfinity), (current, mesh) => Vector3.Max(current, mesh.Maximum));
            _target = (minimum + maximum) * 0.5f;
            _radius = Math.Max((maximum - minimum).Length() * 0.5f, 0.05f);
        }
        else
        {
            _target = Vector3.Zero;
            _radius = 3;
        }
        float aspect = Math.Max(0.15f, ClientSize.Width / (float)Math.Max(1, ClientSize.Height));
        _distance = _radius * 3.1f * Math.Max(1f, 1f / aspect);
        Invalidate();
    }

    public void SetView(string view)
    {
        switch (view.ToLowerInvariant())
        {
            case "front": case "หน้า": case "ด้านหน้า": _yaw = -MathF.PI / 2; _pitch = 0; break;
            case "back": case "หลัง": _yaw = MathF.PI / 2; _pitch = 0; break;
            case "right": case "ขวา": _yaw = 0; _pitch = 0; break;
            case "left": case "ซ้าย": _yaw = MathF.PI; _pitch = 0; break;
            case "top": case "บน": case "ด้านบน": _yaw = -MathF.PI / 2; _pitch = MathF.PI / 2 - 0.001f; break;
            default: _yaw = -0.85f; _pitch = 0.48f; break;
        }
        Invalidate();
    }

    public void SaveScreenshot(string path)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            throw new InvalidOperationException("หน้าต่างแสดงผลยังไม่มีขนาด");
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        using (var graphics = Graphics.FromImage(bitmap)) DrawScene(graphics);
        bitmap.Save(path, ImageFormat.Png);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawScene(e.Graphics);
    }

    private void DrawScene(Graphics graphics)
    {
        graphics.Clear(BackColor);
        if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
        graphics.SmoothingMode = _mouseDown ? SmoothingMode.None : SmoothingMode.AntiAlias;
        SetupCamera();
        if (_showGrid) DrawGrid(graphics);
        BuildTriangles();

        // Antialiasing each separate triangle leaves cracks along shared edges.
        // Use exact polygon coverage for the mesh, then restore smooth overlay lines.
        if (!_wireframe) graphics.SmoothingMode = SmoothingMode.None;

        using var brush = new SolidBrush(Color.White);
        using var wirePen = new Pen(Color.FromArgb(90, 166, 207, 225), 0.75f);
        var polygon = new PointF[3];
        foreach (var triangle in _triangles)
        {
            polygon[0] = triangle.A; polygon[1] = triangle.B; polygon[2] = triangle.C;
            if (_wireframe)
            {
                wirePen.Color = _meshes[triangle.ObjectIndex].Name == _selectedObject
                    ? Color.FromArgb(242, 191, 80) : Color.FromArgb(140, 155, 183, 204);
                graphics.DrawPolygon(wirePen, polygon);
            }
            else
            {
                brush.Color = triangle.Color;
                graphics.FillPolygon(brush, polygon);
            }
        }
        graphics.SmoothingMode = _mouseDown ? SmoothingMode.None : SmoothingMode.AntiAlias;
        if (_previewCuts > 0) DrawQuickSubdivision(graphics);
        if (_showTopology) DrawTopology(graphics);
        DrawSelection(graphics);
        DrawAxes(graphics);
        DrawOverlay(graphics);
    }

    private void DrawQuickSubdivision(Graphics graphics)
    {
        if (string.IsNullOrEmpty(_selectedObject)) return;
        using var pen = new Pen(Color.FromArgb(135, 72, 224, 245), 0.9f);
        static PointF Along(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        int cuts = Math.Min(_previewCuts, 20);
        foreach (var mesh in _meshes.Where(m => m.Name == _selectedObject))
            foreach (var face in mesh.Polygons.Take(3000))
            {
                if (face.Length != 4) continue;
                var p = face.Select(index => Project(mesh.Vertices[index])).ToArray();
                if (p.Any(v => !v.Visible)) continue;
                for (int i = 1; i <= cuts; i++)
                {
                    float t = i / (float)(cuts + 1);
                    graphics.DrawLine(pen, Along(p[0].Point, p[1].Point, t), Along(p[3].Point, p[2].Point, t));
                    graphics.DrawLine(pen, Along(p[0].Point, p[3].Point, t), Along(p[1].Point, p[2].Point, t));
                }
            }
    }

    private void DrawTopology(Graphics graphics)
    {
        if (string.IsNullOrEmpty(_selectedObject)) return;
        using var pen = new Pen(Color.FromArgb(230, 90, 240, 196), 1.15f);
        foreach (var mesh in _meshes.Where(m => m.Name == _selectedObject))
        {
            int stride = Math.Max(1, (int)Math.Ceiling(mesh.Edges.Length / 25000d));
            for (int i = 0; i < mesh.Edges.Length; i += stride)
            {
                var edge = mesh.Edges[i];
                DrawWorldLine(graphics, pen, mesh.Vertices[edge[0]], mesh.Vertices[edge[1]]);
            }
        }
    }

    private void SetupCamera()
    {
        var direction = new Vector3(MathF.Cos(_pitch) * MathF.Cos(_yaw), MathF.Cos(_pitch) * MathF.Sin(_yaw), MathF.Sin(_pitch));
        _eye = _target + direction * _distance;
        _forward = -direction;
        _right = Vector3.Normalize(Vector3.Cross(_forward, Vector3.UnitZ));
        _up = Vector3.Normalize(Vector3.Cross(_right, _forward));
        _focal = Math.Max(1, ClientSize.Height) * 0.5f / MathF.Tan(0.4f);
    }

    private ProjectedVertex Project(Vector3 point)
    {
        var relative = point - _eye;
        float depth = Vector3.Dot(relative, _forward);
        if (depth < Math.Max(0.000001f, _radius * 0.0001f)) return new ProjectedVertex(default, depth, false);
        float x = ClientSize.Width * 0.5f + Vector3.Dot(relative, _right) * _focal / depth;
        float y = ClientSize.Height * 0.5f - Vector3.Dot(relative, _up) * _focal / depth;
        return new ProjectedVertex(new PointF(x, y), depth,
            float.IsFinite(x) && float.IsFinite(y) && Math.Abs(x) < 1e7f && Math.Abs(y) < 1e7f);
    }

    private void BuildTriangles()
    {
        _triangles.Clear();
        var light = Vector3.Normalize(new Vector3(-0.4f, -0.6f, 1f));
        int stride = _mouseDown && TriangleCount > 18_000 ? (int)Math.Ceiling(TriangleCount / 18_000d) : 1;
        for (int objectIndex = 0; objectIndex < _meshes.Count; objectIndex++)
        {
            var mesh = _meshes[objectIndex];
            var projected = new ProjectedVertex[mesh.Vertices.Length];
            for (int i = 0; i < projected.Length; i++) projected[i] = Project(mesh.Vertices[i]);
            bool selected = mesh.Name == _selectedObject;
            for (int faceIndex = 0; faceIndex < mesh.Triangles.Length; faceIndex += stride)
            {
                var face = mesh.Triangles[faceIndex];
                var a = projected[face[0]]; var b = projected[face[1]]; var c = projected[face[2]];
                if (!a.Visible || !b.Visible || !c.Visible) continue;
                if (Math.Max(a.Point.X, Math.Max(b.Point.X, c.Point.X)) < 0 ||
                    Math.Min(a.Point.X, Math.Min(b.Point.X, c.Point.X)) > ClientSize.Width ||
                    Math.Max(a.Point.Y, Math.Max(b.Point.Y, c.Point.Y)) < 0 ||
                    Math.Min(a.Point.Y, Math.Min(b.Point.Y, c.Point.Y)) > ClientSize.Height) continue;
                var normal = Vector3.Cross(mesh.Vertices[face[1]] - mesh.Vertices[face[0]], mesh.Vertices[face[2]] - mesh.Vertices[face[0]]);
                if (normal.LengthSquared() < 1e-18f) continue;
                normal = Vector3.Normalize(normal);
                // Two-sided shading also displays open surfaces and mirrored meshes predictably.
                float intensity = 0.36f + 0.64f * Math.Abs(Vector3.Dot(normal, light));
                var baseColor = mesh.TriangleColors is { } colors && faceIndex < colors.Length ? colors[faceIndex] : mesh.Color;
                var color = Shade(baseColor, intensity, selected);
                _triangles.Add(new DrawTriangle(a.Point, b.Point, c.Point, (a.Depth + b.Depth + c.Depth) / 3, color, objectIndex));
            }
        }
        _triangles.Sort((a, b) => b.Depth.CompareTo(a.Depth));
    }

    private void DrawGrid(Graphics graphics)
    {
        float desired = _radius / 4;
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(Math.Max(0.00001f, desired))));
        float ratio = desired / magnitude;
        float step = magnitude * (ratio < 2 ? 1 : ratio < 5 ? 2 : 5);
        float centerX = MathF.Round(_target.X / step) * step;
        float centerY = MathF.Round(_target.Y / step) * step;
        float extent = step * 12;
        using var gridPen = new Pen(Color.FromArgb(38, 52, 72), 1);
        using var majorPen = new Pen(Color.FromArgb(50, 67, 87), 1);
        for (int i = -12; i <= 12; i++)
        {
            var pen = i % 5 == 0 ? majorPen : gridPen;
            float x = centerX + step * i, y = centerY + step * i;
            DrawWorldLine(graphics, pen, new Vector3(x, centerY - extent, 0), new Vector3(x, centerY + extent, 0));
            DrawWorldLine(graphics, pen, new Vector3(centerX - extent, y, 0), new Vector3(centerX + extent, y, 0));
        }
        using var xPen = new Pen(Color.FromArgb(135, 167, 67, 80), 1.2f);
        using var yPen = new Pen(Color.FromArgb(135, 63, 151, 116), 1.2f);
        DrawWorldLine(graphics, xPen, new Vector3(centerX - extent, 0, 0), new Vector3(centerX + extent, 0, 0));
        DrawWorldLine(graphics, yPen, new Vector3(0, centerY - extent, 0), new Vector3(0, centerY + extent, 0));
    }

    private void DrawWorldLine(Graphics graphics, Pen pen, Vector3 a, Vector3 b)
    {
        // Clip lines against the camera plane before projection so the grid does not disappear at shallow angles.
        float near = Math.Max(0.000001f, _radius * 0.01f);
        float depthA = Vector3.Dot(a - _eye, _forward), depthB = Vector3.Dot(b - _eye, _forward);
        if (depthA < near && depthB < near) return;
        if (depthA < near) a = Vector3.Lerp(a, b, (near - depthA) / (depthB - depthA));
        else if (depthB < near) b = Vector3.Lerp(b, a, (near - depthB) / (depthA - depthB));
        var pa = Project(a); var pb = Project(b);
        if (pa.Visible && pb.Visible) graphics.DrawLine(pen, pa.Point, pb.Point);
    }

    private void DrawSelection(Graphics graphics)
    {
        if (string.IsNullOrEmpty(_selectedObject)) return;
        using var pen = new Pen(Color.FromArgb(235, 195, 111), 1.4f) { DashStyle = DashStyle.Dash };
        foreach (var mesh in _meshes.Where(mesh => mesh.Name == _selectedObject && mesh.Vertices.Length > 0))
        {
            Vector3[] corners = [
                new(mesh.Minimum.X, mesh.Minimum.Y, mesh.Minimum.Z), new(mesh.Maximum.X, mesh.Minimum.Y, mesh.Minimum.Z),
                new(mesh.Maximum.X, mesh.Maximum.Y, mesh.Minimum.Z), new(mesh.Minimum.X, mesh.Maximum.Y, mesh.Minimum.Z),
                new(mesh.Minimum.X, mesh.Minimum.Y, mesh.Maximum.Z), new(mesh.Maximum.X, mesh.Minimum.Y, mesh.Maximum.Z),
                new(mesh.Maximum.X, mesh.Maximum.Y, mesh.Maximum.Z), new(mesh.Minimum.X, mesh.Maximum.Y, mesh.Maximum.Z)
            ];
            for (int i = 0; i < 4; i++)
            {
                DrawWorldLine(graphics, pen, corners[i], corners[(i + 1) % 4]);
                DrawWorldLine(graphics, pen, corners[i + 4], corners[(i + 1) % 4 + 4]);
                DrawWorldLine(graphics, pen, corners[i], corners[i + 4]);
            }
        }
    }

    private void DrawAxes(Graphics graphics)
    {
        var origin = new PointF(ClientSize.Width - 55, 57);
        using var background = new SolidBrush(Color.FromArgb(165, 17, 24, 39));
        graphics.FillEllipse(background, origin.X - 38, origin.Y - 38, 76, 76);
        var axes = new[]
        {
            (Vector3.UnitX, "X", Color.FromArgb(236, 113, 121)),
            (Vector3.UnitY, "Y", Color.FromArgb(97, 206, 158)),
            (Vector3.UnitZ, "Z", Color.FromArgb(117, 170, 248))
        };
        foreach (var (axis, label, color) in axes.OrderBy(item => Vector3.Dot(item.Item1, _forward)))
        {
            var end = new PointF(origin.X + Vector3.Dot(axis, _right) * 25, origin.Y - Vector3.Dot(axis, _up) * 25);
            using var pen = new Pen(color, 2);
            using var brush = new SolidBrush(color);
            graphics.DrawLine(pen, origin, end);
            graphics.FillEllipse(brush, end.X - 3, end.Y - 3, 6, 6);
            graphics.DrawString(label, Font, brush, end.X + 3, end.Y - 10);
        }
    }

    private void DrawOverlay(Graphics graphics)
    {
        using var muted = new SolidBrush(Color.FromArgb(137, 156, 181));
        using var foreground = new SolidBrush(ForeColor);
        using var green = new SolidBrush(Color.FromArgb(104, 211, 173));
        if (!HasScene)
        {
            using var titleFont = new Font(Font.FontFamily, 16f, FontStyle.Bold);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            graphics.DrawString("เริ่มสร้างโมเดลด้วยโค้ด", titleFont, foreground,
                new RectangleF(10, ClientSize.Height / 2f - 48, ClientSize.Width - 20, 45), format);
            graphics.DrawString("เลือกตัวอย่าง แล้วกด สร้างโมเดล (F5)", Font, muted,
                new RectangleF(10, ClientSize.Height / 2f, ClientSize.Width - 20, 32), format);
        }
        else
        {
            graphics.FillEllipse(green, 18, 23, 6, 6);
            graphics.DrawString("มุมมอง 3 มิติ", Font, foreground, 31, 15);
            graphics.DrawString($"{_objects.Count:N0} วัตถุ  ·  {TriangleCount:N0} สามเหลี่ยม" + (_isSimplified ? "  ·  แสดงผลแบบลดรายละเอียด" : ""),
                Font, muted, 17, 39);
        }
        if (ClientSize.Width >= 380)
        {
            string hint = "คลิกขวา: ค้นหาคำสั่ง   ·   ลากซ้าย: หมุน   ·   ลากขวา: เลื่อน   ·   ลูกล้อ: ซูม";
            graphics.DrawString(hint, Font, muted, 16, ClientSize.Height - 29);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right or MouseButtons.Middle)) return;
        Focus();
        _mouseDown = true;
        _dragged = false;
        _dragButton = e.Button;
        _lastMouse = _mouseStart = e.Location;
        Capture = true;
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_mouseDown) return;
        int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;
        if (Math.Abs(e.X - _mouseStart.X) + Math.Abs(e.Y - _mouseStart.Y) > 4) _dragged = true;
        if (dx == 0 && dy == 0) return;
        if (_dragButton == MouseButtons.Left)
        {
            _yaw -= dx * 0.008f;
            _pitch = Math.Clamp(_pitch + dy * 0.008f, -MathF.PI / 2 + 0.01f, MathF.PI / 2 - 0.01f);
        }
        else
        {
            SetupCamera();
            float scale = _distance / Math.Max(1, _focal);
            _target += (-_right * dx + _up * dy) * scale;
        }
        _lastMouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        bool pick = _mouseDown && !_dragged && _dragButton is MouseButtons.Left or MouseButtons.Right;
        bool context = pick && _dragButton == MouseButtons.Right;
        _mouseDown = false;
        Capture = false;
        Cursor = Cursors.Default;
        if (pick)
        {
            // Rebuild without the interactive sampling before picking.
            SetupCamera();
            BuildTriangles();
            for (int i = _triangles.Count - 1; i >= 0; i--)
            {
                var triangle = _triangles[i];
                if (!ContainsPoint(triangle, e.Location)) continue;
                SelectedObject = _meshes[triangle.ObjectIndex].Name;
                ObjectSelected?.Invoke(this, SelectedObject);
                if (context) ObjectContextRequested?.Invoke(this, SelectedObject);
                break;
            }
        }
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        _mouseDown = false;
        Cursor = Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _distance = Math.Clamp(_distance * MathF.Pow(0.85f, e.Delta / 120f), _radius * 0.04f, _radius * 250);
        Invalidate();
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Home or Keys.F || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.F or Keys.Home)
        {
            FitScene();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.NumPad1) SetView("front");
        else if (e.KeyCode == Keys.NumPad7) SetView("top");
        else if (e.KeyCode == Keys.NumPad3) SetView("right");
    }

    private static bool ContainsPoint(DrawTriangle triangle, PointF point)
    {
        static float Cross(PointF p, PointF a, PointF b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
        float a = Cross(point, triangle.A, triangle.B);
        float b = Cross(point, triangle.B, triangle.C);
        float c = Cross(point, triangle.C, triangle.A);
        return !((a < 0 || b < 0 || c < 0) && (a > 0 || b > 0 || c > 0));
    }

    private static Color ReadColor(JsonElement element, Color fallback)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 3) return fallback;
        static int Channel(JsonElement value, bool linear = true)
        {
            if (!value.TryGetSingle(out float number) || !float.IsFinite(number)) return 180;
            if (linear) number = number <= 0.0031308f ? number * 12.92f : 1.055f * MathF.Pow(Math.Max(0, number), 1 / 2.4f) - 0.055f;
            return (int)Math.Clamp(number * 255, 0, 255);
        }
        return Color.FromArgb(element.GetArrayLength() > 3 ? Math.Max(40, Channel(element[3], false)) : 255,
            Channel(element[0]), Channel(element[1]), Channel(element[2]));
    }

    private static Color Shade(Color color, float intensity, bool selected)
    {
        int red = (int)Math.Clamp(color.R * intensity + (selected ? 18 : 0), 0, 255);
        int green = (int)Math.Clamp(color.G * intensity + (selected ? 12 : 0), 0, 255);
        int blue = (int)Math.Clamp(color.B * intensity, 0, 255);
        return Color.FromArgb(color.A, red, green, blue);
    }
}
