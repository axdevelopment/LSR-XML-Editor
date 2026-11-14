using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Brush = System.Windows.Media.Brush;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using WpfColor = System.Windows.Media.Color;

namespace LsrXmlEditor.PedViewer.Rendering;

public sealed class PreviewScene : Border
{
    private const double FieldOfView = 40;
    private const double ZoomStep = 1.1;
    private const double FrameDistance = 1.9;
    private const double MinZoom = 0.15;
    private const double MaxZoom = 8;
    private const double NearPlane = 0.02;
    private const double FarPlane = 30;
    private const double PanSpeed = 0.002;
    private const double RotateSpeed = 0.01;
    private const double MaxPitch = 1.4;
    private const double MinSize = 0.05;

    private static readonly Brush FallbackBrush = Freeze(new SolidColorBrush(WpfColor.FromRgb(170, 170, 170)));

    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = FieldOfView };
    private readonly DirectionalLight _keyLight = new(WpfColor.FromRgb(200, 200, 200), new Vector3D(0, -0.3, 1));
    private readonly SortedDictionary<int, ScenePart> _parts = [];
    private readonly HashSet<int> _hidden = [];
    private readonly Dictionary<int, List<Deformable>> _deformable = [];
    private readonly Model3DGroup _root = new();
    private readonly Model3DGroup _lights = new();
    private Point3D _target = new(0, 0, 0);
    private double _yaw;
    private double _pitch = 0.1;
    private double _distance = 3.2;
    private double _minDistance = 0.4;
    private double _maxDistance = 12;
    private double _defaultYaw;
    private Point? _dragStart;
    private bool _panning;

    public PreviewScene()
    {
        Background = SystemParameters.HighContrast
            ? System.Windows.SystemColors.WindowBrush
            : new LinearGradientBrush(WpfColor.FromRgb(236, 238, 242), WpfColor.FromRgb(196, 200, 208), 90);
        _viewport.Camera = _camera;

        _lights.Children.Add(new AmbientLight(WpfColor.FromRgb(110, 110, 110)));
        _lights.Children.Add(_keyLight);
        _lights.Children.Add(new DirectionalLight(WpfColor.FromRgb(70, 70, 80), new Vector3D(0.6, 0.4, -0.6)));
        _root.Children.Add(_lights);
        _viewport.Children.Add(new ModelVisual3D { Content = _root });
        Child = _viewport;

        MouseDown += OnMouseDown;
        MouseUp += (_, _) => ReleaseMouseCapture();
        LostMouseCapture += (_, _) => _dragStart = null;
        MouseMove += OnMouseMove;
        MouseWheel += (_, e) =>
        {
            _distance = Math.Clamp(e.Delta > 0 ? _distance / ZoomStep : _distance * ZoomStep, _minDistance, _maxDistance);
            UpdateCamera();
        };
        UpdateCamera();
    }

    public bool KeepDeformable { get; set; }

    public void SetParts(IEnumerable<PartData> parts)
    {
        _parts.Clear();
        _deformable.Clear();
        _hidden.Clear();
        foreach (var part in parts)
        {
            Store(part);
        }

        Rebuild();
    }

    public void SetPart(PartData part, bool visible)
    {
        Store(part);
        if (visible)
        {
            _hidden.Remove(part.Component);
        }
        else
        {
            _hidden.Add(part.Component);
        }

        Rebuild();
    }

    public void SetVisible(int component, bool visible)
    {
        var changed = visible ? _hidden.Remove(component) : _hidden.Add(component);
        if (changed)
        {
            Rebuild();
        }
    }

    public void ClearParts()
    {
        _parts.Clear();
        _deformable.Clear();
        Rebuild();
    }

    public void Deform(Func<MeshData, float[], bool> deform)
    {
        foreach (var item in _deformable.Values.SelectMany(list => list))
        {
            if (deform(item.Mesh, item.Buffer))
            {
                item.Apply();
            }
        }
    }

    public void FrameAll() => Frame(0.5, 1);

    public void Focus(double yaw, double height, double zoom)
    {
        _yaw = yaw;
        _pitch = 0;
        Frame(height, zoom);
    }

    private void Frame(double height, double zoom)
    {
        var bounds = Rect3D.Empty;
        foreach (var part in _parts.Values)
        {
            bounds.Union(part.Body.Bounds);
        }

        if (bounds.IsEmpty)
        {
            return;
        }

        var size = Math.Max(MinSize, Math.Max(bounds.SizeY, Math.Max(bounds.SizeX, bounds.SizeZ)));
        _target = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY * height, bounds.Z + bounds.SizeZ / 2);
        _distance = size * FrameDistance * zoom;
        _minDistance = size * MinZoom;
        _maxDistance = size * MaxZoom;
        UpdateCamera();
    }

    public void ResetView(double? yaw = null, double pitch = 0.1)
    {
        _defaultYaw = yaw ?? _defaultYaw;
        _yaw = _defaultYaw;
        _pitch = pitch;
        FrameAll();
    }

    private void Store(PartData part)
    {
        var deformable = new List<Deformable>();
        var body = new Model3DGroup();
        var material = CreateMaterial(part.Texture);
        foreach (var mesh in part.Meshes)
        {
            body.Children.Add(new GeometryModel3D(Geometry(mesh, deformable), material) { BackMaterial = material });
        }

        var decals = CreateDecals(part.Decals, deformable);
        if (deformable.Count == 0)
        {
            body.Freeze();
            decals?.Freeze();
            _deformable.Remove(part.Component);
        }
        else
        {
            _deformable[part.Component] = deformable;
        }

        _parts[part.Component] = new ScenePart(body, decals, part.IsTransparent);
    }

    private MeshGeometry3D Geometry(MeshData mesh, List<Deformable> deformable)
    {
        var geometry = CreateGeometry(mesh);
        if (KeepDeformable && mesh.Skin is not null)
        {
            deformable.Add(new Deformable(geometry, mesh));
        }
        else
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private void Rebuild()
    {
        _root.Children.Clear();
        _root.Children.Add(_lights);
        var visible = _parts.Where(p => !_hidden.Contains(p.Key)).Select(p => p.Value).ToList();
        foreach (var part in visible.Where(p => !p.Transparent))
        {
            _root.Children.Add(part.Body);
        }

        foreach (var part in visible.Where(p => p.Transparent))
        {
            _root.Children.Add(part.Body);
        }

        foreach (var part in visible.Where(p => p.Decals is not null))
        {
            _root.Children.Add(part.Decals!);
        }
    }

    private Model3DGroup? CreateDecals(IReadOnlyList<DecalData> decals, List<Deformable> deformable)
    {
        if (decals.Count == 0)
        {
            return null;
        }

        var group = new Model3DGroup();
        var materials = new Dictionary<TextureData, Material>(ReferenceEqualityComparer.Instance);
        foreach (var decal in decals)
        {
            if (!materials.TryGetValue(decal.Texture, out var material))
            {
                material = CreateMaterial(decal.Texture);
                materials[decal.Texture] = material;
            }

            group.Children.Add(new GeometryModel3D(Geometry(decal.Mesh, deformable), material));
        }

        return group;
    }

    private sealed record ScenePart(Model3DGroup Body, Model3DGroup? Decals, bool Transparent);

    private sealed class Deformable(MeshGeometry3D geometry, MeshData mesh)
    {
        private readonly Point3DCollection[] _frames = [Points(mesh.Positions.Length / 3), Points(mesh.Positions.Length / 3)];
        private int _frame;

        public MeshData Mesh { get; } = mesh;

        public float[] Buffer { get; } = new float[mesh.Positions.Length];

        public void Apply()
        {
            var target = _frames[_frame];
            _frame ^= 1;
            geometry.Positions = null;
            for (var i = 0; i < target.Count; i++)
            {
                target[i] = new Point3D(Buffer[i * 3], Buffer[i * 3 + 2], -Buffer[i * 3 + 1]);
            }

            geometry.Positions = target;
        }

        private static Point3DCollection Points(int count)
        {
            var points = new Point3DCollection(count);
            for (var i = 0; i < count; i++)
            {
                points.Add(default);
            }

            return points;
        }
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _panning = e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        var current = e.GetPosition(this);
        var dx = current.X - start.X;
        var dy = current.Y - start.Y;
        _dragStart = current;

        if (_panning)
        {
            _target = new Point3D(_target.X, _target.Y + dy * _distance * PanSpeed, _target.Z);
        }
        else
        {
            _yaw -= dx * RotateSpeed;
            _pitch = Math.Clamp(_pitch + dy * RotateSpeed, -MaxPitch, MaxPitch);
        }

        UpdateCamera();
    }

    private void UpdateCamera()
    {
        var offset = new Vector3D(
            Math.Sin(_yaw) * Math.Cos(_pitch),
            Math.Sin(_pitch),
            Math.Cos(_yaw) * Math.Cos(_pitch)) * _distance;
        _camera.Position = _target + offset;
        _camera.LookDirection = -offset;
        _camera.UpDirection = new Vector3D(0, 1, 0);
        _camera.NearPlaneDistance = _distance * NearPlane;
        _camera.FarPlaneDistance = _distance * FarPlane;
        _keyLight.Direction = -offset;
    }

    private static MeshGeometry3D CreateGeometry(MeshData mesh)
    {
        var count = mesh.Positions.Length / 3;
        var texCoords = new PointCollection(count);
        for (var i = 0; i < count; i++)
        {
            texCoords.Add(new Point(mesh.TexCoords[i * 2], mesh.TexCoords[i * 2 + 1]));
        }

        texCoords.Freeze();
        var indices = new Int32Collection(mesh.Indices);
        indices.Freeze();
        return new MeshGeometry3D
        {
            Positions = ToPoints(mesh.Positions),
            TextureCoordinates = texCoords,
            TriangleIndices = indices
        };
    }

    private static Point3DCollection ToPoints(float[] positions)
    {
        var count = positions.Length / 3;
        var points = new Point3DCollection(count);
        for (var i = 0; i < count; i++)
        {
            points.Add(new Point3D(positions[i * 3], positions[i * 3 + 2], -positions[i * 3 + 1]));
        }

        points.Freeze();
        return points;
    }

    private static Material CreateMaterial(TextureData? texture)
    {
        Brush brush = FallbackBrush;
        if (texture is not null)
        {
            var bitmap = BitmapSource.Create(texture.Width, texture.Height, 96, 96, PixelFormats.Bgra32, null, texture.Bgra, texture.Width * 4);
            bitmap.Freeze();
            brush = Freeze(new ImageBrush(bitmap) { ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile });
        }

        var material = new DiffuseMaterial(brush);
        material.Freeze();
        return material;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
