using LsrXmlEditor.PedViewer.GameData;

namespace LsrXmlEditor.PedViewer.UI;

internal sealed class TattooPanel : UserControl
{
    private const int ApplyDelay = 300;
    private const string AllZones = "All zones";

    private static readonly string[] ZoneTitles = ["Torso", "Head", "Left arm", "Right arm", "Left leg", "Right leg"];

    private readonly ComboBox _zone = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _search = new() { PlaceholderText = "Search tattoos", Dock = DockStyle.Fill };
    private readonly CheckBox _appliedOnly = new() { Text = "&Applied only", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TattooList _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable
    };

    private readonly Label _count = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _clear = new() { Text = "&Remove all", AutoSize = true };
    private readonly Button _write = new() { Text = "&Write to entry", AutoSize = true, Enabled = false };
    private readonly System.Windows.Forms.Timer _applyTimer = new() { Interval = ApplyDelay };
    private readonly List<AppliedOverlay> _applied = [];
    private IReadOnlyList<Decoration> _available = [];
    private TattooCatalog? _catalog;
    private bool _suppress;
    private bool _canWrite;

    public TattooPanel()
    {
        ShowTattoos = new CheckBox { Text = "&Show tattoos", Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
        _zone.Items.Add(AllZones);
        _zone.Items.AddRange(ZoneTitles);
        _zone.SelectedIndex = 0;

        _list.Columns.Add("Tattoo", LogicalToDeviceUnits(190));
        _list.Columns.Add("Zone", LogicalToDeviceUnits(70));
        _list.Columns.Add("Collection", LogicalToDeviceUnits(150));

        var filter = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(0, 0, 0, 3) };
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(110)));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filter.Controls.Add(_zone, 0, 0);
        filter.Controls.Add(_search, 1, 0);
        filter.Controls.Add(_appliedOnly, 2, 0);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
        actions.Controls.AddRange([_count, ShowTattoos, _clear, _write]);

        Controls.Add(_list);
        Controls.Add(filter);
        Controls.Add(actions);

        _zone.SelectedIndexChanged += (_, _) =>
        {
            Populate();
            if (_zone.SelectedIndex > 0)
            {
                FocusRequested?.Invoke(this, new FocusEventArgs((DecorationZone)(_zone.SelectedIndex - 1), null));
            }
        };
        _search.TextChanged += (_, _) => Populate();
        _appliedOnly.CheckedChanged += (_, _) => Populate();
        _list.ItemChecked += OnItemChecked;
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_list.SelectedItems.Count == 1 && _list.SelectedItems[0].Tag is Decoration decoration)
            {
                FocusRequested?.Invoke(this, new FocusEventArgs(decoration.Zone, decoration.Facing));
            }
        };
        _clear.Click += (_, _) =>
        {
            _applied.RemoveAll(a => _catalog?.Find(a.Collection, a.Name) is not { IsHairOverlay: true });
            Populate();
            ScheduleApply();
        };
        _write.Click += (_, _) =>
        {
            WriteRequested?.Invoke(this, EventArgs.Empty);
            _write.Enabled = false;
        };
        _applyTimer.Tick += (_, _) =>
        {
            _applyTimer.Stop();
            AppliedChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? AppliedChanged;

    public event EventHandler<FocusEventArgs>? FocusRequested;

    public event EventHandler? WriteRequested;

    public CheckBox ShowTattoos { get; }


    public IReadOnlyList<AppliedOverlay> Applied => _applied;

    public void EnableWriting(bool enabled) => _canWrite = enabled;

    public void Bind(TattooCatalog catalog, DecorationGender gender, IReadOnlyList<AppliedOverlay> applied)
    {
        _catalog = catalog;
        _available = [.. catalog.All.Where(d => d.IsTattoo && !d.IsHairOverlay && (gender == DecorationGender.Any || d.Gender == DecorationGender.Any || d.Gender == gender))];
        _applied.Clear();
        _applied.AddRange(applied);
        _write.Enabled = false;
        Populate();
    }

    public void Clear()
    {
        _catalog = null;
        _available = [];
        _applied.Clear();
        _write.Enabled = false;
        Populate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _applyTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Populate()
    {
        var zone = _zone.SelectedIndex - 1;
        var query = _search.Text.Trim();
        var applied = _applied.ToHashSet(OverlayComparer.Instance);
        var unknown = _applied
            .Where(a => _catalog?.Find(a.Collection, a.Name) is null)
            .Select(a => new Decoration(a.Collection, a.Name, a.Name, string.Empty, string.Empty, DecorationZone.Torso, DecorationGender.Any, 0, 0, 0, 0, 0, 0, true, false));
        var rows = _available
            .Concat(_applied.Select(a => _catalog?.Find(a.Collection, a.Name)).OfType<Decoration>().Where(d => !d.IsHairOverlay && !_available.Contains(d)))
            .Concat(unknown)
            .Where(d => zone < 0 || (int)d.Zone == zone)
            .Where(d => !_appliedOnly.Checked || applied.Contains(Key(d)))
            .Where(d => query.Length == 0 || d.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase) || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || d.Collection.Contains(query, StringComparison.OrdinalIgnoreCase));

        _suppress = true;
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _list.Items.AddRange([.. rows.Select(d => new ListViewItem([d.Label, ZoneTitles[(int)d.Zone], d.Collection]) { Tag = d, Checked = applied.Contains(Key(d)), ToolTipText = d.Name })]);
        }
        finally
        {
            _list.EndUpdate();
            _suppress = false;
        }

        UpdateCount();
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_suppress || _list.Restoring || e.Item.Tag is not Decoration decoration)
        {
            return;
        }

        var key = Key(decoration);
        var index = _applied.FindIndex(a => OverlayComparer.Instance.Equals(a, key));
        if (e.Item.Checked == index >= 0)
        {
            return;
        }

        if (e.Item.Checked)
        {
            _applied.Add(key);
        }
        else
        {
            _applied.RemoveAt(index);
        }

        UpdateCount();
        ScheduleApply();
    }

    private void ScheduleApply()
    {
        _write.Enabled = _canWrite;
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void UpdateCount()
    {
        var count = _applied.Count(a => _catalog?.Find(a.Collection, a.Name) is not { IsHairOverlay: true });
        _count.Text = count == 1 ? "1 tattoo applied" : $"{count} tattoos applied";
    }

    private static AppliedOverlay Key(Decoration decoration) => new(decoration.Collection, decoration.Name);

    private sealed class TattooList : ListView
    {
        public bool Restoring { get; private set; }

        protected override void OnHandleCreated(EventArgs e)
        {
            Restoring = true;
            try
            {
                base.OnHandleCreated(e);
            }
            finally
            {
                Restoring = false;
            }
        }
    }

    public sealed class FocusEventArgs(DecorationZone zone, double? facing) : EventArgs
    {
        public DecorationZone Zone { get; } = zone;

        public double? Facing { get; } = facing;
    }
}
