using System.Diagnostics;
using System.Windows.Forms.Integration;
using System.Xml.Linq;
using LsrXmlEditor.PedViewer.GameData;
using LsrXmlEditor.PedViewer.Rendering;

namespace LsrXmlEditor.PedViewer.UI;

public sealed class PedViewerPanel : UserControl
{
    private const int FrameInterval = 33;
    private const int RowHeight = 29;
    private const int MaxTexture = 25;
    private const int MinViewHeight = 160;
    private const int SettingsHeight = 330;
    private const string FemaleMarker = "female";
    private const string MaleFreemode = "mp_m_freemode_01";
    private const string FemaleFreemode = "mp_f_freemode_01";

    private static readonly (double Height, double Zoom)[] ZoneFocus = [(0.7, 0.45), (0.93, 0.18), (0.68, 0.5), (0.68, 0.5), (0.28, 0.55), (0.28, 0.55)];

    private readonly PreviewScene _scene = new();
    private readonly ComponentRow[] _rows = new ComponentRow[PedCatalog.ComponentCount];
    private readonly Dictionary<string, PedCatalog?> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripLabel _title = new();
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ToolTip _toolTip = new();
    private readonly NumericUpDown _hairColor = new() { Minimum = 0 };
    private readonly NumericUpDown _hairHighlight = new() { Minimum = 0 };
    private readonly SplitContainer _split;
    private readonly HeadBlendSection _blend;
    private readonly TattooPanel _tattoos = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = FrameInterval };
    private readonly Stopwatch _animationClock = new();
    private readonly ToolStrip _animationBar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, Visible = false };
    private readonly ToolStripLabel _animationLabel = new();
    private readonly ToolStripButton _playPause = new("&Pause");
    private readonly ToolStripButton _stopAnimation = new("&Stop");
    private Action<string>? _entryWriter;
    private PedAnimator? _animator;
    private GameArchive? _archive;
    private PedCatalog? _catalog;
    private Outfit? _outfit;
    private OverlaySet _overlays = OverlaySet.Empty;
    private int _generation;
    private bool _suppressEvents;
    private bool _splitterFitted;

    public PedViewerPanel()
    {
        var host = new ElementHost { Dock = DockStyle.Fill, Child = _scene };
        _title.Font = new Font(Font, FontStyle.Bold);

        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, LayoutStyle = ToolStripLayoutStyle.Flow };
        var resetView = new ToolStripButton("Reset &view");
        var resetOutfit = new ToolStripButton("Reset &outfit");
        var settings = new ToolStripButton("S&ettings") { CheckOnClick = true, Checked = true };
        toolbar.Items.AddRange([_title, new ToolStripSeparator(), resetView, resetOutfit, settings]);
        _animationBar.Items.AddRange([_playPause, _stopAnimation, new ToolStripSeparator(), _animationLabel]);

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, Padding = new Padding(6, 4, 6, 0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(124)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(70)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(56)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var c = 0; c < PedCatalog.ComponentCount; c++)
        {
            var row = new ComponentRow(c);
            _rows[c] = row;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(RowHeight)));
            table.Controls.Add(row.Visible, 0, c);
            table.Controls.Add(row.Drawable, 1, c);
            table.Controls.Add(row.Texture, 2, c);
            table.Controls.Add(row.Source, 3, c);
            var component = c;
            row.Visible.CheckedChanged += (_, _) => _scene.SetVisible(component, row.Visible.Checked);
            row.Drawable.ValueChanged += (_, _) => Run(() => OnComponentChangedAsync(component, resetTexture: true));
            row.Texture.ValueChanged += (_, _) => Run(() => OnComponentChangedAsync(component, resetTexture: false));
        }

        _blend = new HeadBlendSection(table, PedCatalog.ComponentCount, LogicalToDeviceUnits(RowHeight));
        _hairColor.Maximum = HairPalette.Count - 1;
        _hairHighlight.Maximum = HairPalette.Count - 1;
        _hairColor.Width = _hairHighlight.Width = LogicalToDeviceUnits(56);

        var hair = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(3, 0, 3, 3) };
        hair.Controls.AddRange(
        [
            new Label { Text = "&Hair color:", AutoSize = true, Anchor = AnchorStyles.Left },
            _hairColor,
            new Label { Text = "Hi&ghlight:", AutoSize = true, Anchor = AnchorStyles.Left },
            _hairHighlight
        ]);

        var clothing = new TabPage("Clothing") { UseVisualStyleBackColor = true };
        clothing.Controls.Add(table);
        clothing.Controls.Add(hair);
        var tattoos = new TabPage("Tattoos") { UseVisualStyleBackColor = true, Padding = new Padding(4) };
        tattoos.Controls.Add(_tattoos);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.AddRange([clothing, tattoos]);

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(6, 2, 6, 2),
            Text = "Drag: rotate · Right drag: move · Wheel: zoom"
        };

        _split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = 5 };
        _split.Panel1.Controls.Add(host);
        _split.Panel1.Controls.Add(_animationBar);
        _split.Panel1.Controls.Add(hint);
        _split.Panel2.Controls.Add(tabs);

        var statusStrip = new StatusStrip { SizingGrip = false };
        statusStrip.Items.Add(_status);

        Controls.Add(_split);
        Controls.Add(toolbar);
        Controls.Add(statusStrip);

        resetView.Click += (_, _) => _scene.ResetView();
        resetOutfit.Click += (_, _) => Run(ResetOutfitAsync);
        settings.CheckedChanged += (_, _) => _split.Panel2Collapsed = !settings.Checked;
        _playPause.Click += (_, _) => TogglePlayback();
        _stopAnimation.Click += (_, _) => StopAnimation();
        _animationTimer.Tick += (_, _) => AnimateFrame();
        _blend.Changed += (_, _) => Run(OnBlendChangedAsync);
        _hairColor.ValueChanged += (_, _) => Run(OnHairColorChangedAsync);
        _hairHighlight.ValueChanged += (_, _) => Run(OnHairColorChangedAsync);
        _tattoos.ShowTattoos.CheckedChanged += (_, _) => Run(ReloadPartsAsync);
        _tattoos.AppliedChanged += (_, _) => Run(OnTattoosChangedAsync);
        _tattoos.FocusRequested += (_, e) => FocusZone(e.Zone, e.Facing);
        _tattoos.WriteRequested += (_, _) => WriteTattoos();
        _split.SizeChanged += (_, _) => FitSplitter();
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                StopAnimation();
            }
        };
        _status.Text = "Double-click an entry to show it here.";
    }

    public void SetEntryWriter(Action<string>? writer)
    {
        _entryWriter = writer;
        _tattoos.EnableWriting(writer is not null);
    }

    public async Task<string?> ShowOutfitAsync(string gtaFolder, string outfitXml, string title)
    {
        var outfit = Outfit.Parse(outfitXml, title);
        if (outfit is null)
        {
            return "The selected entry has no ModelName and cannot be shown in 3D.";
        }

        StopAnimation();
        return await LoadOutfitAsync(gtaFolder, outfit, ++_generation);
    }

    public async Task<string?> ShowAnimationAsync(string gtaFolder, string dictionary, string clip, string title)
    {
        StopAnimation();
        var generation = ++_generation;
        if (_outfit is null || _archive is null || !string.Equals(_archive.Folder, gtaFolder, StringComparison.OrdinalIgnoreCase))
        {
            var female = clip.Contains(FemaleMarker, StringComparison.OrdinalIgnoreCase) || dictionary.Contains(FemaleMarker, StringComparison.OrdinalIgnoreCase);
            var outfit = new Outfit(title, female ? FemaleFreemode : MaleFreemode, [], -1, -1, [], [], null);
            var error = await LoadOutfitAsync(gtaFolder, outfit, generation);
            if (error is not null || _catalog is null || generation != _generation)
            {
                return error;
            }
        }

        var archive = _archive!;
        var pedModel = _outfit!.Model;
        _status.Text = $"Loading animation {clip} …";
        string? failure = null;
        var animator = await Task.Run(() => PedAnimator.Create(archive, pedModel, dictionary, clip, out failure));
        if (generation != _generation)
        {
            return null;
        }

        if (animator is null)
        {
            _status.Text = failure ?? $"Animation {dictionary} / {clip} could not be loaded.";
            return null;
        }

        _animator = animator;
        _scene.KeepDeformable = true;
        await ReloadPartsAsync();
        _title.Text = $"{title} ({_outfit.Model})";
        _animationLabel.Text = $"{animator.ClipName} ({animator.Duration:0.0} s)";
        _animationBar.Visible = true;
        _playPause.Text = "&Pause";
        _animationClock.Restart();
        _animationTimer.Start();
        _status.Text = $"Playing {animator.Dictionary} / {animator.ClipName}";
        return null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
            _toolTip.Dispose();
            _loadGate.Dispose();
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

    private void AnimateFrame()
    {
        if (_animator is null)
        {
            return;
        }

        _animator.Pose(_animationClock.Elapsed.TotalSeconds);
        _scene.Deform(_animator.Skin);
    }

    private static bool Restore(MeshData mesh, float[] buffer)
    {
        Array.Copy(mesh.Positions, buffer, buffer.Length);
        return true;
    }

    private void TogglePlayback()
    {
        if (_animationClock.IsRunning)
        {
            _animationClock.Stop();
            _animationTimer.Stop();
            _playPause.Text = "&Play";
        }
        else
        {
            _animationClock.Start();
            _animationTimer.Start();
            _playPause.Text = "&Pause";
        }
    }

    private void StopAnimation()
    {
        if (_animator is null)
        {
            return;
        }

        _animationTimer.Stop();
        _animationClock.Reset();
        _animator = null;
        _scene.Deform(Restore);
        _scene.KeepDeformable = false;
        _animationBar.Visible = false;
    }

    private async Task<string?> LoadOutfitAsync(string gtaFolder, Outfit outfit, int generation)
    {
        UseWaitCursor = true;
        try
        {
            _title.Text = $"{outfit.Title} ({outfit.Model})";
            var archive = await GameArchive.OpenAsync(gtaFolder, new Progress<string>(text => _status.Text = text));
            if (!ReferenceEquals(archive, _archive))
            {
                _catalogs.Clear();
                _archive = archive;
            }

            var catalog = await CatalogForAsync(archive, outfit.Model);
            var tattoos = await Task.Run(() => TattooCatalog.For(archive));
            if (generation != _generation)
            {
                return null;
            }

            _outfit = outfit;
            _catalog = catalog;
            if (catalog is null)
            {
                _scene.ClearParts();
                _tattoos.Clear();
                _status.Text = $"No variation data found for model \"{outfit.Model}\".";
                return null;
            }

            _tattoos.Bind(tattoos, TattooCatalog.GenderOf(outfit.Model), outfit.Decorations);
            await ReloadAsync(generation);
            return null;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void FitSplitter()
    {
        if (_splitterFitted)
        {
            return;
        }

        var max = _split.Height - _split.Panel2MinSize - _split.SplitterWidth;
        var min = LogicalToDeviceUnits(MinViewHeight);
        if (max <= min)
        {
            return;
        }

        _split.SplitterDistance = Math.Clamp(_split.Height - LogicalToDeviceUnits(SettingsHeight), min, max);
        _splitterFitted = true;
    }

    private async Task<PedCatalog?> CatalogForAsync(GameArchive archive, string model)
    {
        if (_catalogs.TryGetValue(model, out var cached))
        {
            return cached;
        }

        _status.Text = $"Reading ped variations for {model} …";
        var catalog = await Task.Run(() => PedCatalog.Build(archive, model));
        _catalogs[model] = catalog;
        return catalog;
    }

    private Task ResetOutfitAsync()
    {
        if (_outfit is null)
        {
            return Task.CompletedTask;
        }

        if (_archive is not null)
        {
            _tattoos.Bind(TattooCatalog.For(_archive), TattooCatalog.GenderOf(_outfit.Model), _outfit.Decorations);
        }

        return ReloadAsync(_generation);
    }

    private async Task ReloadAsync(int generation)
    {
        if (_catalog is not { } catalog || _outfit is not { } outfit || _archive is null)
        {
            return;
        }

        _suppressEvents = true;
        _hairColor.Value = outfit.HairColor >= 0 ? Math.Min(outfit.HairColor, HairPalette.Count - 1) : HairPalette.DefaultPrimary;
        _hairHighlight.Value = outfit.HairHighlight >= 0 ? Math.Min(outfit.HairHighlight, HairPalette.Count - 1) : _hairColor.Value;
        for (var c = 0; c < PedCatalog.ComponentCount; c++)
        {
            var item = outfit.Components.FirstOrDefault(x => x.Component == c);
            _rows[c].Drawable.Maximum = Math.Max(0, catalog.Totals[c] - 1);
            _rows[c].Drawable.Value = Math.Clamp(item?.Drawable ?? 0, 0, (int)_rows[c].Drawable.Maximum);
            _rows[c].Texture.Maximum = MaxTexture;
            _rows[c].Texture.Value = Math.Clamp(item?.Texture ?? 0, 0, MaxTexture);
        }

        _blend.Load(outfit.Blend, Math.Max(0, catalog.Totals[PartData.HeadComponent] - 1));
        UpdateHeadRow();
        _suppressEvents = false;
        _status.Text = "Loading tattoos and overlays …";
        var overlays = await BuildOverlaysAsync();
        if (generation != _generation)
        {
            return;
        }

        _overlays = overlays;
        await ReloadPartsAsync();
        _scene.ResetView();
    }

    private async Task ReloadPartsAsync()
    {
        if (_catalog is not { } catalog || _outfit is not { } outfit)
        {
            return;
        }

        var generation = _generation;
        await _loadGate.WaitAsync();
        try
        {
            if (generation != _generation)
            {
                return;
            }

            _status.Text = "Loading clothing …";
            var selection = Enumerable.Range(0, PedCatalog.ComponentCount)
                .Select(c => (Component: c, Drawable: (int)_rows[c].Drawable.Value, Texture: (int)_rows[c].Texture.Value))
                .ToList();
            var style = CurrentStyle();
            var blend = _blend.Current;
            var parts = await Task.Run(() => selection.Select(s => ResolveAndExtract(catalog, s.Component, s.Drawable, s.Texture, style, blend)).ToList());
            if (generation != _generation)
            {
                return;
            }

            _scene.ClearParts();
            foreach (var (part, data, blended) in parts)
            {
                Apply(part, data, blended);
            }

            _status.Text = $"{outfit.Model} · {catalog.CollectionCount} clothing collections · {_overlays.Summary}";
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private Task<OverlaySet> BuildOverlaysAsync()
    {
        var archive = _archive!;
        var decorations = _tattoos.Applied.ToList();
        var headOverlays = _outfit!.HeadOverlays;
        return Task.Run(() => OverlaySet.Build(archive, decorations, headOverlays));
    }

    private HairColors CurrentHair() => new((int)_hairColor.Value, (int)_hairHighlight.Value);

    private async Task OnHairColorChangedAsync()
    {
        if (!_suppressEvents && _catalog is not null)
        {
            await OnComponentChangedAsync(PartData.HairComponent, resetTexture: false);
        }
    }

    private async Task OnTattoosChangedAsync()
    {
        if (_catalog is null || _archive is null || _outfit is null)
        {
            return;
        }

        var generation = _generation;
        var overlays = await BuildOverlaysAsync();
        if (generation == _generation)
        {
            _overlays = overlays;
            await ReloadPartsAsync();
        }
    }

    private async Task OnBlendChangedAsync()
    {
        if (_suppressEvents)
        {
            return;
        }

        UpdateHeadRow();
        await OnComponentChangedAsync(PartData.HeadComponent, resetTexture: false);
    }

    private void UpdateHeadRow()
    {
        var row = _rows[PartData.HeadComponent];
        row.Drawable.Enabled = row.Texture.Enabled = _blend.Current is null;
    }

    private void FocusZone(DecorationZone zone, double? facing)
    {
        var (height, zoom) = ZoneFocus[(int)zone];
        var yaw = facing ?? zone switch
        {
            DecorationZone.LeftArm => Math.PI / 2,
            DecorationZone.RightArm => -Math.PI / 2,
            _ => 0
        };
        _scene.Focus(yaw, height, zoom);
    }

    private void WriteTattoos()
    {
        if (_entryWriter is null)
        {
            return;
        }

        var element = new XElement("AppliedOverlays",
            _tattoos.Applied.Select(a => new XElement("AppliedOverlay",
                new XElement("CollectionName", a.Collection),
                new XElement("OverlayName", a.Name))));
        _entryWriter(element.ToString());
        _status.Text = $"{_tattoos.Applied.Count} overlays written to the entry.";
    }

    private async Task OnComponentChangedAsync(int component, bool resetTexture)
    {
        if (_suppressEvents || _catalog is not { } catalog)
        {
            return;
        }

        await _loadGate.WaitAsync();
        try
        {
            if (resetTexture)
            {
                _suppressEvents = true;
                _rows[component].Texture.Value = 0;
                _suppressEvents = false;
            }

            var drawable = (int)_rows[component].Drawable.Value;
            var texture = (int)_rows[component].Texture.Value;
            _status.Text = $"Loading {PedCatalog.ComponentNames[component]} {drawable} …";
            var style = CurrentStyle();
            var blend = _blend.Current;
            var generation = _generation;
            var (part, data, blended) = await Task.Run(() => ResolveAndExtract(catalog, component, drawable, texture, style, blend));
            if (generation != _generation)
            {
                return;
            }

            Apply(part, data, blended);
            _status.Text = $"{PedCatalog.ComponentNames[component]}: {Describe(part, blended)}";
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private PartStyle CurrentStyle() => new(CurrentHair(), _tattoos.ShowTattoos.Checked ? _overlays : OverlaySet.Empty);

    private static (ComponentPart Part, PartData Data, bool Blended) ResolveAndExtract(PedCatalog catalog, int component, int drawable, int texture, PartStyle style, HeadBlend? blend)
    {
        if (component == PartData.HeadComponent && blend is not null && HeadBlender.Build(catalog, blend) is { } morph)
        {
            return (morph.Base, MeshExtractor.Extract(morph, style), true);
        }

        var part = catalog.Resolve(component, drawable, texture);
        return (part, MeshExtractor.Extract(part, style), false);
    }

    private static string Describe(ComponentPart part, bool blended) => blended
        ? "Head blend (parent heads and skins mixed)"
        : part.Drawable is null ? "No drawable found" : $"{part.Collection} #{part.LocalDrawable} · {part.DrawableName} · {part.TextureName}";

    private void Apply(ComponentPart part, PartData data, bool blended)
    {
        var row = _rows[part.Component];
        if (!blended)
        {
            _suppressEvents = true;
            row.Texture.Maximum = Math.Max(0, part.TextureCount - 1);
            row.Texture.Value = Math.Clamp(part.Texture, 0, (int)row.Texture.Maximum);
            _suppressEvents = false;
        }

        row.Source.Text = blended ? "Head blend" : part.Drawable is null ? "None" : $"{part.Collection} #{part.LocalDrawable}";
        var detail = Describe(part, blended);
        _toolTip.SetToolTip(row.Visible, detail);
        _toolTip.SetToolTip(row.Source, detail);
        _scene.SetPart(data, row.Visible.Checked);
    }

    private sealed class ComponentRow
    {
        public ComponentRow(int component)
        {
            Visible = new CheckBox { Text = PedCatalog.ComponentNames[component], Checked = true, AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true };
            Drawable = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0 };
            Texture = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0 };
            Source = new Label { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
        }

        public CheckBox Visible { get; }

        public NumericUpDown Drawable { get; }

        public NumericUpDown Texture { get; }

        public Label Source { get; }
    }
}
