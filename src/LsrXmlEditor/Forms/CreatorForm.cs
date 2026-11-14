using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms;

public enum CreatorMode
{
    Gang,
    GangDen,
    Location,
    Agency
}

public sealed record LocationType(string Container, string Element, string Title);

public sealed record CreatorData(IReadOnlyList<LocationType> LocationTypes, IReadOnlyList<(string Code, string Name)> Zones, IReadOnlyList<string> Counties);

public sealed class CreatorForm : Form
{
    private const string CopyOption = "Copy as new";
    private const string ReusePrefix = "Use ";
    private const string IdField = "ID";
    private const string NameField = "Name";
    private const string FullNameField = "FullName";
    private const string GangPrefix = "AMBIENT_GANG_";
    private const int TextWidth = 260;
    private const int LinkWidth = 220;
    private const int ListHeight = 150;
    private static readonly string[] PlacementLabels = ["X", "Y", "Z", "Heading"];

    private readonly CreatorMode _mode;
    private readonly CreatorEngine _engine;
    private readonly CreatorData _data;
    private readonly TableLayoutPanel _layout = new() { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, Padding = new Padding(12, 8, 12, 8) };
    private readonly ComboBox _type = Combo();
    private readonly ComboBox _owner = Combo();
    private readonly ComboBox _template = Combo();
    private readonly TextBox _id = new();
    private readonly Dictionary<string, TextBox> _texts = [];
    private readonly List<LinkRow> _links = [];
    private TextBox[] _placement = [];
    private readonly CheckBox _withDen = new() { Text = "Also create a &den (copy of the template gang's den)", AutoSize = true, Checked = true };
    private readonly TextBox _denName = new();
    private TextBox[] _denPlacement = [];
    private readonly CheckedListBox _zones = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly CheckedListBox _counties = new() { CheckOnClick = true, IntegralHeight = false };
    private LinkRow? _denMenu;
    private int _bodyStart;
    private bool _filling;

    public CreatorForm(CreatorMode mode, CreatorEngine engine, CreatorData data)
    {
        _mode = mode;
        _engine = engine;
        _data = data;
        Text = mode switch
        {
            CreatorMode.Gang => "New gang",
            CreatorMode.GangDen => "New gang den",
            CreatorMode.Location => "New shop / location",
            _ => "New agency"
        };
        Icon = StockImages.AppIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(780));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var create = new Button { Text = "&Create", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        create.Click += (_, _) => CreateEntry();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([cancel, create]);
        AcceptButton = create;
        CancelButton = cancel;

        Controls.Add(_layout);
        Controls.Add(buttons);

        _type.SelectedIndexChanged += (_, _) => FillTemplates();
        _owner.SelectedIndexChanged += (_, _) => SelectOwnerDen();
        _template.SelectedIndexChanged += (_, _) => BuildBody();
        _id.TextChanged += (_, _) => SuggestIds();
        BuildHeader();
    }

    public XElement? Result { get; private set; }

    private EntityKind Kind => _mode switch
    {
        CreatorMode.Gang => CreatorCatalog.Gang,
        CreatorMode.Agency => CreatorCatalog.Agency,
        CreatorMode.GangDen => CreatorCatalog.Location(CreatorCatalog.GangDenElement),
        _ => CreatorCatalog.Location(SelectedType?.Element ?? string.Empty)
    };

    private LocationType? SelectedType => (_type.SelectedItem as Choice<LocationType>)?.Value;

    private XElement? Template => (_template.SelectedItem as Choice<XElement>)?.Value;

    private void BuildHeader()
    {
        if (_mode == CreatorMode.Location)
        {
            _type.Items.AddRange([.. _data.LocationTypes.Select(t => new Choice<LocationType>(t, t.Title))]);
            AddRow("&Type:", _type);
        }

        if (_mode == CreatorMode.GangDen)
        {
            _owner.Items.AddRange([.. _engine.Entries(CreatorCatalog.Gang).Select(g => new Choice<XElement>(g, Describe(g, IdField)))]);
            AddRow("&Gang:", _owner);
        }

        AddRow("&Template:", _template);
        _bodyStart = _layout.RowCount;
        if (_mode == CreatorMode.Location)
        {
            _type.SelectedIndex = _type.Items.Count > 0 ? 0 : -1;
        }
        else
        {
            FillTemplates();
            if (_mode == CreatorMode.GangDen && _owner.Items.Count > 0)
            {
                _owner.SelectedIndex = 0;
            }
        }
    }

    private void FillTemplates()
    {
        _template.Items.Clear();
        var idField = _mode is CreatorMode.Gang or CreatorMode.Agency ? IdField : NameField;
        _template.Items.AddRange([.. _engine.Entries(Kind).Select(e => new Choice<XElement>(e, Describe(e, idField)))]);
        _template.SelectedIndex = _template.Items.Count > 0 ? 0 : -1;
    }

    private void SelectOwnerDen()
    {
        if ((_owner.SelectedItem as Choice<XElement>)?.Value.Element(IdField)?.Value is not { } gangId)
        {
            return;
        }

        var index = _template.Items.Cast<Choice<XElement>>().ToList()
            .FindIndex(c => c.Value.Element(CreatorCatalog.AssociationField)?.Value == gangId);
        if (index >= 0)
        {
            _template.SelectedIndex = index;
        }
    }

    private void BuildBody()
    {
        if (Template is not { } template)
        {
            return;
        }

        _filling = true;
        _layout.SuspendLayout();
        var keep = _bodyStart;
        foreach (var control in _layout.Controls.Cast<Control>().Where(c => _layout.GetRow(c) >= keep).ToList())
        {
            _layout.Controls.Remove(control);
            if (control != _id && control != _withDen && control != _denName && control != _zones && control != _counties)
            {
                control.Dispose();
            }
        }

        while (_layout.RowStyles.Count > keep)
        {
            _layout.RowStyles.RemoveAt(_layout.RowStyles.Count - 1);
        }

        _layout.RowCount = keep;
        _texts.Clear();
        _links.Clear();
        _denMenu = null;

        if (Kind.IdField is not null)
        {
            AddSection("Identity");
            AddRow("&ID:", _id);
        }
        else
        {
            AddSection("Names");
        }

        foreach (var field in Kind.TextFields.Where(f => template.Element(f) is { HasElements: false }))
        {
            var box = new TextBox { Text = template.Element(field)!.Value, Width = LogicalToDeviceUnits(TextWidth) };
            if (Kind.IdField is null && field == NameField)
            {
                box.TextChanged += (_, _) => SuggestIds();
            }

            _texts[field] = box;
            AddRow(DisplayNames.Humanize(field) + ":", box);
        }

        var links = Kind.Links.Where(l => template.Element(l.Field)?.Value.Trim() is { Length: > 0 }).ToList();
        if (links.Count > 0)
        {
            AddSection("Linked data");
            foreach (var link in links)
            {
                _links.Add(AddLink(link, template.Element(link.Field)!.Value.Trim()));
            }
        }

        if (_mode is CreatorMode.GangDen or CreatorMode.Location)
        {
            AddSection("Location");
            _placement = AddPlacement(template);
        }

        if (_mode == CreatorMode.Gang)
        {
            AddGangExtras(template);
        }

        if (_mode == CreatorMode.Agency)
        {
            AddSection("Jurisdiction");
            FillList(_zones, _data.Zones.Select(z => $"{z.Name} ({z.Code})"));
            AddRow("&Zones:", _zones, ListHeight);
            FillList(_counties, _data.Counties);
            AddRow("C&ounties:", _counties, ListHeight);
        }

        _layout.RowCount++;
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.ResumeLayout();
        _filling = false;
        SuggestIds();
    }

    private void AddGangExtras(XElement template)
    {
        var gangId = template.Element(IdField)?.Value;
        var den = _engine.Entries(CreatorCatalog.Location(CreatorCatalog.GangDenElement))
            .FirstOrDefault(d => d.Element(CreatorCatalog.AssociationField)?.Value == gangId);
        if (den is not null)
        {
            AddSection("Den");
            _withDen.Tag = den;
            AddRow(string.Empty, _withDen);
            _denName.Text = den.Element(NameField)?.Value ?? string.Empty;
            _denName.Width = LogicalToDeviceUnits(TextWidth);
            AddRow("Den &name:", _denName);
            _denPlacement = AddPlacement(den);
            if (den.Element(CreatorCatalog.MenuField)?.Value.Trim() is { Length: > 0 } menu)
            {
                _denMenu = AddLink(CreatorCatalog.LocationLinks[0], menu, "Den menu");
            }
        }
        else
        {
            _withDen.Tag = null;
        }

        AddSection("Territory");
        FillList(_zones, _data.Zones.Select(z => $"{z.Name} ({z.Code})"));
        AddRow("&Zones:", _zones, ListHeight);
    }

    private LinkRow AddLink(LinkField link, string templateValue, string? label = null)
    {
        var choice = Combo();
        choice.Items.AddRange([CopyOption, ReusePrefix + templateValue]);
        choice.SelectedIndex = link.CopyByDefault ? 0 : 1;
        choice.Width = LogicalToDeviceUnits(LinkWidth);
        var id = new TextBox { Width = LogicalToDeviceUnits(LinkWidth) };
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        panel.Controls.AddRange([choice, id]);
        var row = new LinkRow(link, templateValue, choice, id);
        choice.SelectedIndexChanged += (_, _) => id.Enabled = row.Copy;
        id.TextChanged += (_, _) => row.Edited |= !_filling;
        id.Enabled = row.Copy;
        AddRow($"{label ?? link.Label}:", panel);
        return row;
    }

    private TextBox[] AddPlacement(XElement source)
    {
        var boxes = PlacementLabels.Select(_ => new TextBox()).ToArray();
        var position = source.Element("EntrancePosition");
        string[] values =
        [
            position?.Element("X")?.Value ?? "0",
            position?.Element("Y")?.Value ?? "0",
            position?.Element("Z")?.Value ?? "0",
            source.Element("EntranceHeading")?.Value ?? "0"
        ];
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        for (var i = 0; i < boxes.Length; i++)
        {
            boxes[i].Text = values[i];
            boxes[i].Width = LogicalToDeviceUnits(80);
            panel.Controls.Add(new Label { Text = PlacementLabels[i] + ":", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(i == 0 ? 0 : 8, 6, 2, 0) });
            panel.Controls.Add(boxes[i]);
        }

        AddRow("&Position:", panel);
        return boxes;
    }

    private void SuggestIds()
    {
        if (_filling)
        {
            return;
        }

        var token = Token(Kind.IdField is not null ? _id.Text : _texts.GetValueOrDefault(NameField)?.Text ?? string.Empty);
        _filling = true;
        foreach (var row in _links.Append(_denMenu).OfType<LinkRow>().Where(r => !r.Edited))
        {
            row.Id.Text = token.Length == 0 ? string.Empty : token + row.Link.Suffix;
        }

        _filling = false;
    }

    private void CreateEntry()
    {
        var errors = Problems();
        if (errors.Count > 0)
        {
            Dialogs.Info(this, string.Join("\n", errors));
            return;
        }

        var template = Template!;
        var values = _texts.ToDictionary(t => t.Key, t => t.Value.Text.Trim());
        var plans = _links.Select(r => r.Plan).ToList();
        switch (_mode)
        {
            case CreatorMode.Gang:
            {
                var id = _id.Text.Trim();
                Result = _engine.Create(Kind, new EntryPlan(template, id, values, plans, null));
                if (_withDen.Checked && _withDen.Tag is XElement den)
                {
                    var name = _denName.Text.Trim();
                    _engine.CreateDen(den, id, new Dictionary<string, string> { [NameField] = name, [FullNameField] = name }, Placement(_denPlacement)!, _denMenu?.Plan);
                }

                _engine.AddTerritories(id, CheckedCodes(_zones));
                break;
            }

            case CreatorMode.Agency:
            {
                var id = _id.Text.Trim();
                var templateId = template.Element(IdField)?.Value ?? string.Empty;
                Result = _engine.Create(Kind, new EntryPlan(template, id, values, plans, null));
                _engine.AddZoneJurisdictions(id, templateId, CheckedCodes(_zones));
                _engine.AddCountyJurisdictions(id, templateId, _counties.CheckedItems.Cast<string>());
                break;
            }

            case CreatorMode.GangDen:
            {
                var gangId = (_owner.SelectedItem as Choice<XElement>)!.Value.Element(IdField)!.Value;
                Result = _engine.CreateDen(template, gangId, values, Placement(_placement)!, plans.FirstOrDefault());
                break;
            }

            default:
                Result = _engine.Create(Kind, new EntryPlan(template, null, values, plans, Placement(_placement)));
                break;
        }

        DialogResult = DialogResult.OK;
    }

    private List<string> Problems()
    {
        var errors = new List<string>();
        if (Template is null)
        {
            errors.Add("Choose a template.");
            return errors;
        }

        if (_mode == CreatorMode.GangDen && _owner.SelectedItem is null)
        {
            errors.Add("Choose the gang the den belongs to.");
        }

        if (Kind.IdField is not null)
        {
            var id = _id.Text.Trim();
            if (id.Length == 0)
            {
                errors.Add("Enter an ID.");
            }
            else if (_engine.EntryExists(Kind, id))
            {
                errors.Add($"The ID \"{id}\" is already used.");
            }
        }

        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _links.Append(_denMenu).OfType<LinkRow>().Where(r => r.Copy && (r != _denMenu || _withDen.Checked)))
        {
            var id = row.Id.Text.Trim();
            if (id.Length == 0)
            {
                errors.Add($"Enter a new ID for {row.Link.Label.ToLowerInvariant()}.");
            }
            else if (_engine.GroupExists(row.Link.Kind, id) || !planned.Add(row.Link.Kind.Element + "|" + id))
            {
                errors.Add($"The ID \"{id}\" for {row.Link.Label.ToLowerInvariant()} is already used.");
            }
        }

        if (_mode is CreatorMode.GangDen or CreatorMode.Location && Placement(_placement) is null
            || _mode == CreatorMode.Gang && _withDen.Checked && _withDen.Tag is not null && Placement(_denPlacement) is null)
        {
            errors.Add("Position values must be numbers.");
        }

        return errors;
    }

    private static Placement? Placement(TextBox[] boxes)
    {
        var values = new double[boxes.Length];
        for (var i = 0; i < boxes.Length; i++)
        {
            if (!double.TryParse(boxes[i].Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return new Placement(values[0], values[1], values[2], values[3]);
    }

    private IEnumerable<string> CheckedCodes(CheckedListBox list) =>
        list.CheckedIndices.Cast<int>().Select(i => _data.Zones[i].Code);

    private void FillList(CheckedListBox list, IEnumerable<string> items)
    {
        list.Items.Clear();
        list.Items.AddRange([.. items]);
        list.Width = LogicalToDeviceUnits(TextWidth + 120);
    }

    private void AddSection(string title)
    {
        var label = new Label { Text = title, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 12, 0, 4) };
        label.Disposed += (_, _) => label.Font.Dispose();
        AddRow(string.Empty, label);
        _layout.SetColumnSpan(label, 2);
        _layout.SetColumn(label, 0);
    }

    private void AddRow(string caption, Control control, int height = 0)
    {
        var row = _layout.RowCount++;
        _layout.RowStyles.Add(height > 0 ? new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(height)) : new RowStyle(SizeType.AutoSize));
        if (caption.Length > 0)
        {
            _layout.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) }, 0, row);
        }

        if (control is ComboBox or TextBox && control.Width < LogicalToDeviceUnits(TextWidth))
        {
            control.Width = LogicalToDeviceUnits(TextWidth);
        }

        control.Anchor = height > 0 ? AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom : AnchorStyles.Left;
        _layout.Controls.Add(control, 1, row);
    }

    private static string Describe(XElement entry, string field)
    {
        var id = entry.Element(field)?.Value.Trim() ?? string.Empty;
        var name = entry.Element(FullNameField)?.Value.Trim();
        return string.IsNullOrEmpty(name) || name == id ? id : $"{id} – {name}";
    }

    private static string Token(string text)
    {
        var value = text.Trim();
        if (value.StartsWith(GangPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[GangPrefix.Length..];
        }

        var parts = Regex.Split(value, "[^A-Za-z0-9]+");
        return string.Concat(parts.Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + (p.All(char.IsUpper) ? p[1..].ToLowerInvariant() : p[1..])));
    }

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private sealed class LinkRow(LinkField link, string templateValue, ComboBox choice, TextBox id)
    {
        public LinkField Link { get; } = link;

        public TextBox Id { get; } = id;

        public bool Edited { get; set; }

        public bool Copy => (string?)choice.SelectedItem == CopyOption;

        public LinkPlan Plan => new(Link, templateValue, Copy, Id.Text.Trim());
    }

    private sealed record Choice<T>(T Value, string Text)
    {
        public override string ToString() => Text;
    }
}
