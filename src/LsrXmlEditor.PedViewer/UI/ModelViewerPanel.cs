using System.Windows.Forms.Integration;
using LsrXmlEditor.PedViewer.GameData;
using LsrXmlEditor.PedViewer.Rendering;

namespace LsrXmlEditor.PedViewer.UI;

public sealed class ModelViewerPanel : UserControl
{
    private const string Hint = "Drag: rotate · Right drag: move · Wheel: zoom";
    private const double VehicleYaw = Math.PI - 0.75;
    private const double PropYaw = 0.6;
    private const double ModelPitch = 0.25;

    private readonly PreviewScene _scene = new();
    private readonly ToolStripLabel _title = new();
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly NumericUpDown _primary = new() { Minimum = 0 };
    private readonly NumericUpDown _secondary = new() { Minimum = 0 };
    private readonly Label _hint = new() { Dock = DockStyle.Bottom, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(6, 2, 6, 2), Text = Hint };
    private readonly ToolStripItem[] _paintItems;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GameArchive? _archive;
    private GameModel? _model;
    private int _generation;
    private bool _suppress;

    public ModelViewerPanel()
    {
        var host = new ElementHost { Dock = DockStyle.Fill, Child = _scene };
        _title.Font = new Font(Font, FontStyle.Bold);
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, LayoutStyle = ToolStripLayoutStyle.Flow };
        var resetView = new ToolStripButton("Reset &view");
        resetView.Click += (_, _) => _scene.ResetView();

        _primary.Maximum = _secondary.Maximum = VehiclePalette.Count - 1;
        _primary.Width = _secondary.Width = LogicalToDeviceUnits(56);
        _primary.ValueChanged += (_, _) => Run(RepaintAsync);
        _secondary.ValueChanged += (_, _) => Run(RepaintAsync);
        _paintItems =
        [
            new ToolStripSeparator(),
            new ToolStripLabel("Primary:"),
            new ToolStripControlHost(_primary) { ToolTipText = "Primary paint color ID (colors are approximate)" },
            new ToolStripLabel("Secondary:"),
            new ToolStripControlHost(_secondary) { ToolTipText = "Secondary paint color ID (colors are approximate)" }
        ];

        toolbar.Items.AddRange([_title, new ToolStripSeparator(), resetView, .. _paintItems]);

        var statusStrip = new StatusStrip { SizingGrip = false };
        statusStrip.Items.Add(_status);

        Controls.Add(host);
        Controls.Add(_hint);
        Controls.Add(toolbar);
        Controls.Add(statusStrip);
    }

    public async Task<string?> ShowModelAsync(string gtaFolder, string modelName, VehicleColors? colors, string title)
    {
        var generation = ++_generation;
        UseWaitCursor = true;
        try
        {
            _title.Text = $"{title} ({modelName})";
            var archive = await GameArchive.OpenAsync(gtaFolder, new Progress<string>(text => _status.Text = text));
            _status.Text = $"Loading {modelName} …";
            var model = await Task.Run(() => ModelLoader.Load(archive, modelName));
            if (generation != _generation)
            {
                return null;
            }

            _archive = archive;
            _model = model;
            if (model is null)
            {
                _scene.ClearParts();
                _status.Text = $"No model file found for \"{modelName}\".";
                return null;
            }

            var isVehicle = model.Kind == ModelKind.Vehicle;
            foreach (var item in _paintItems)
            {
                item.Visible = isVehicle;
            }

            _suppress = true;
            _primary.Value = Math.Clamp(colors?.Primary ?? VehiclePalette.DefaultPrimary, 0, (int)_primary.Maximum);
            _secondary.Value = Math.Clamp(colors?.Secondary ?? VehiclePalette.DefaultSecondary, 0, (int)_secondary.Maximum);
            _suppress = false;

            if (await RenderAsync(generation))
            {
                _scene.ResetView(model.Kind switch
                {
                    ModelKind.Vehicle => VehicleYaw,
                    ModelKind.Weapon => 0,
                    _ => PropYaw
                }, ModelPitch);
            }

            return null;
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
            return null;
        }
        finally
        {
            _suppress = false;
            UseWaitCursor = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
            _title.Font?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _status.Text = "Error: " + ex.Message;
        }
    }

    private async Task RepaintAsync()
    {
        if (!_suppress && _model?.Kind == ModelKind.Vehicle)
        {
            await RenderAsync(_generation);
        }
    }

    private async Task<bool> RenderAsync(int generation)
    {
        if (_archive is not { } archive || _model is not { } model)
        {
            return false;
        }

        await _gate.WaitAsync();
        try
        {
            var colors = new VehicleColors((int)_primary.Value, (int)_secondary.Value);
            var parts = await Task.Run(() => ModelExtractor.Extract(archive, model, colors));
            if (generation != _generation)
            {
                return false;
            }

            _scene.SetParts(parts);
            var meshes = parts.Sum(p => p.Meshes.Count);
            var untextured = parts.Where(p => p.Texture is null).Sum(p => p.Meshes.Count);
            _status.Text = $"{model.Kind} · {model.FileName} · {meshes} meshes" + (untextured > 0 ? $" · {untextured} without texture" : string.Empty);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
