using System.Media;
using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms.Creator;

public sealed class CreatorForm : Form
{
    private const int StepWidth = 210;
    private const int StepHeight = 34;

    private readonly CreatorMode _mode;
    private readonly CreatorEngine _engine;
    private readonly Dictionary<GroupKind, IReadOnlyList<Choice<string>>> _groups = [];
    private readonly ListBox _steps = new()
    {
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false,
        TabStop = false
    };

    private readonly Label _title = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _description = new() { AutoSize = true, UseMnemonic = false, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 0) };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly PictureBox _statusIcon = new() { SizeMode = PictureBoxSizeMode.CenterImage, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 6, 0) };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false };
    private readonly Button _back = new() { Text = "< Back", AutoSize = true, UseMnemonic = false };
    private readonly Button _next = new() { Text = "Next >", AutoSize = true, UseMnemonic = false };
    private readonly Button _create = new() { Text = "Create", AutoSize = true, UseMnemonic = false };
    private readonly ToolTip _tips = new();
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
    private readonly Image _errorImage = StockImages.Small(StockIconId.Error);
    private readonly Image _warningImage = StockImages.Small(StockIconId.Warning);
    private readonly Image _readyImage = StockImages.Small(StockIconId.Info);
    private readonly Font _titleFont;
    private readonly BasicsPage _basics;
    private readonly LinksPage _links;
    private readonly LocationPage _location;
    private readonly DenPage _den;
    private readonly MenusPage _menus;
    private readonly RelationsPage _relations;
    private readonly AreaPage _area;
    private readonly SettingsPage _settings;
    private readonly SummaryPage _summary;
    private readonly List<CreatorPage> _visible = [];
    private CreatorPage? _current;
    private CreatorDraft? _draft;
    private IReadOnlyList<CreatorProblem> _problems = [];
    private bool _touched;

    public CreatorForm(CreatorMode mode, CreatorEngine engine, CreatorData data)
    {
        _mode = mode;
        _engine = engine;
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
        KeyPreview = true;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(LogicalToDeviceUnits(UiTheme.Grow(1000)), area.Width), Math.Min(LogicalToDeviceUnits(UiTheme.Grow(720)), area.Height));
        MinimumSize = new Size(LogicalToDeviceUnits(820), LogicalToDeviceUnits(540));
        Font = UiTheme.Font;
        _titleFont = new Font(Font.FontFamily, Font.SizeInPoints * 4f / 3f, FontStyle.Bold);
        _title.Font = _titleFont;

        _basics = new BasicsPage(mode, engine, data, Rebase);
        _links = mode is CreatorMode.Gang or CreatorMode.Agency
            ? new LinksPage("Members, cars & weapons", "Where the peds, vehicles, weapons and menus of the new " + (mode == CreatorMode.Gang ? "gang" : "agency") + " come from.", engine, Groups)
            : new LinksPage("Menu & staff", "Which menu the place uses and who works there.", engine, Groups);
        _location = new LocationPage(data.SavedPositions);
        _den = new DenPage(engine, data.SavedPositions, Groups);
        _menus = new MenusPage(engine);
        _relations = new RelationsPage(engine);
        _area = new AreaPage(mode, engine, data);
        _settings = new SettingsPage(engine);
        _summary = new SummaryPage(data, ShowStep);
        foreach (var page in new CreatorPage[] { _basics, _links, _location, _den, _menus, _relations, _area, _settings, _summary })
        {
            page.Changed += (sender, _) =>
            {
                _touched |= sender != _basics;
                UpdateState();
            };
        }

        BuildLayout();
        UiTheme.Attach(this);
        _steps.DrawItem += DrawStep;
        _steps.SelectedIndexChanged += (_, _) =>
        {
            if (_steps.SelectedIndex >= 0 && _steps.SelectedIndex < _visible.Count && _visible[_steps.SelectedIndex] != _current)
            {
                ShowPage(_visible[_steps.SelectedIndex]);
            }
        };
        _back.Click += (_, _) => Go(-1);
        _next.Click += (_, _) => Go(1);
        _create.Click += (_, _) => CreateEntry();
        CancelButton = _cancel;
        _tips.SetToolTip(_back, "Previous step (Ctrl+Page Up)");
        _tips.SetToolTip(_next, "Next step (Ctrl+Page Down)");
        _tips.SetToolTip(_create, "Add the new entry (Ctrl+Enter)");

        var kind = _basics.InitialKind;
        if (engine.Entries(kind).FirstOrDefault() is { } template)
        {
            _draft = new CreatorDraft(mode, engine, kind, template);
        }
    }

    public XElement? Result { get; private set; }

    public Exception? Failure { get; private set; }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_draft is null)
        {
            Dialogs.Info(this, $"There is nothing to copy from – no {_basics.InitialKind.Title} was found in {_engine.FileOf(_basics.InitialKind.FileBase)}.");
            BeginInvoke(Close);
            return;
        }

        RebuildSteps();
        ShowPage(_basics);
        UpdateState();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _basics.FocusInitial();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.PageDown:
            case Keys.Control | Keys.Tab:
                Go(1);
                return true;
            case Keys.Control | Keys.PageUp:
            case Keys.Control | Keys.Shift | Keys.Tab:
                Go(-1);
                return true;
            case Keys.Control | Keys.Enter:
                CreateEntry();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var page in new CreatorPage[] { _basics, _links, _location, _den, _menus, _relations, _area, _settings, _summary }.Where(p => p.Parent is null))
            {
                page.Dispose();
            }

            _titleFont.Dispose();
            _tips.Dispose();
            _errorImage.Dispose();
            _warningImage.Dispose();
            _readyImage.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 14, 16, 10)
        };
        header.Controls.AddRange([_title, _description]);
        _content.Padding = new Padding(16, 4, 8, 8);

        var main = new Panel { Dock = DockStyle.Fill };
        main.Controls.Add(_content);
        main.Controls.Add(header);

        var stepsPanel = new Panel { Dock = DockStyle.Left, Width = LogicalToDeviceUnits(UiTheme.Grow(StepWidth)), BackColor = UiTheme.Window, Padding = new Padding(0, 10, 0, 0) };
        _steps.BackColor = UiTheme.Window;
        _steps.ForeColor = UiTheme.Text;
        _steps.ItemHeight = Math.Max(LogicalToDeviceUnits(StepHeight), Font.Height + LogicalToDeviceUnits(14));
        stepsPanel.Controls.Add(_steps);
        var divider = new Label { Dock = DockStyle.Left, Width = 1, BackColor = SystemColors.ControlDark };

        _statusIcon.Size = new Size(LogicalToDeviceUnits(18), LogicalToDeviceUnits(18));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        buttons.Controls.AddRange([_back, _next, _create, _cancel]);
        _create.Margin = new Padding(12, 3, 3, 3);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(12, 8, 12, 10) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(_statusIcon, 0, 0);
        bottom.Controls.Add(_status, 1, 0);
        bottom.Controls.Add(buttons, 2, 0);
        var line = new Label { Dock = DockStyle.Bottom, Height = 1, BackColor = SystemColors.ControlDark };

        Controls.Add(main);
        Controls.Add(divider);
        Controls.Add(stepsPanel);
        Controls.Add(line);
        Controls.Add(bottom);
    }

    private CreatorDraft Rebase(XElement template, EntityKind kind)
    {
        var old = _draft!;
        if (_touched && !Dialogs.Confirm(this, "Starting from another entry resets what you set up in the other steps (links, menus, friends & enemies, settings). Continue?"))
        {
            BeginInvoke(() => ShowPage(_basics));
            return old;
        }

        _touched = false;
        var draft = new CreatorDraft(_mode, _engine, kind, template);
        if (old.Name.Length > 0)
        {
            draft.SetName(old.Name);
        }

        foreach (var field in old.ManualFields.Where(draft.Main.Has))
        {
            draft.SetField(field, old.Main.Get(field));
        }

        if (old.Main.Placement is { } placement && old.Main.Origin is { } origin && !placement.Equals(origin) && draft.Main.Placement is not null)
        {
            draft.Main.Placement = placement;
            draft.Main.MoveAttached = old.Main.MoveAttached;
        }

        if (_mode == CreatorMode.Location && old.Main.OwnerChanged && draft.Main.HasOwner)
        {
            draft.Main.Owner = old.Main.Owner;
        }

        draft.Zones.UnionWith(old.Zones);
        draft.Counties.UnionWith(old.Counties);
        if (_mode == CreatorMode.GangDen)
        {
            draft.OwnerGangId = old.OwnerGangId;
            draft.PrimaryDen = old.PrimaryDen;
        }

        _draft = draft;
        RebuildSteps();
        BeginInvoke(() =>
        {
            if (_current is { } page)
            {
                ShowPage(page);
            }

            UpdateState();
        });
        return draft;
    }

    private void RebuildSteps()
    {
        var draft = _draft!;
        _visible.Clear();
        _visible.Add(_basics);
        switch (_mode)
        {
            case CreatorMode.Gang:
                AddIf(_links, draft.Main.Links.Count > 0);
                _visible.Add(_den);
                AddIf(_menus, draft.HasMenus);
                _visible.AddRange([_relations, _area]);
                break;
            case CreatorMode.Agency:
                AddIf(_links, draft.Main.Links.Count > 0);
                _visible.Add(_area);
                break;
            default:
                AddIf(_location, draft.Main.Placement is not null);
                AddIf(_links, draft.Main.Links.Count > 0);
                AddIf(_menus, draft.HasMenus);
                break;
        }

        _visible.AddRange([_settings, _summary]);
        _steps.BeginUpdate();
        _steps.Items.Clear();
        _steps.Items.AddRange([.. _visible.Select(p => p.Title)]);
        _steps.EndUpdate();
        if (_current is not null && !_visible.Contains(_current))
        {
            _current = null;
        }

        if (_current is not null)
        {
            _steps.SelectedIndex = _visible.IndexOf(_current);
        }
    }

    private void AddIf(CreatorPage page, bool condition)
    {
        if (condition)
        {
            _visible.Add(page);
        }
    }

    private void ShowStep(CreatorStep step)
    {
        if (_visible.FirstOrDefault(p => p.Step == step) is { } page)
        {
            ShowPage(page);
        }
    }

    private void Go(int delta)
    {
        var index = _current is null ? 0 : _visible.IndexOf(_current) + delta;
        if (index >= 0 && index < _visible.Count)
        {
            ShowPage(_visible[index]);
        }
    }

    private void ShowPage(CreatorPage page)
    {
        if (_draft is null)
        {
            return;
        }

        _content.SuspendLayout();
        if (_current is not null && _current != page)
        {
            _content.Controls.Remove(_current);
        }

        _current = page;
        page.Dock = page.FillsHeight ? DockStyle.Fill : DockStyle.Top;
        _content.AutoScroll = !page.FillsHeight;
        page.Present(_draft);
        if (page.Parent != _content)
        {
            _content.Controls.Add(page);
        }

        _content.AutoScrollPosition = Point.Empty;
        _content.ResumeLayout();

        _title.Text = page.Title;
        _description.Text = page.Description;
        var index = _visible.IndexOf(page);
        _steps.SelectedIndex = index;
        _back.Enabled = index > 0;
        _next.Enabled = index < _visible.Count - 1;
        if (page == _basics)
        {
            _basics.FocusInitial();
        }
        else
        {
            page.SelectNextControl(page, true, true, true, true);
        }
    }

    private void UpdateState()
    {
        if (_draft is null)
        {
            return;
        }

        _problems = _draft.Problems();
        _steps.Invalidate();
        var blocking = _problems.FirstOrDefault(p => p.Blocking);
        var warnings = _problems.Count(p => !p.Blocking);
        if (blocking is not null)
        {
            _statusIcon.Image = _errorImage;
            _status.Text = blocking.Message;
        }
        else if (warnings > 0)
        {
            _statusIcon.Image = _warningImage;
            _status.Text = warnings == 1 ? _problems.First(p => !p.Blocking).Message : $"Ready to create – {warnings} things worth a look in the summary.";
        }
        else
        {
            _statusIcon.Image = _readyImage;
            _status.Text = "Ready to create – the other steps are optional.";
        }

        _status.MaximumSize = new Size(Math.Max(LogicalToDeviceUnits(200), ClientSize.Width - LogicalToDeviceUnits(480)), 0);
    }

    private void DrawStep(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _visible.Count)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var background = new SolidBrush(selected ? UiTheme.Selection : UiTheme.Window))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }

        var page = _visible[e.Index];
        var color = selected ? UiTheme.SelectionText : UiTheme.Text;
        var padding = LogicalToDeviceUnits(14);
        var text = $"{e.Index + 1}.  {page.Title}";
        TextRenderer.DrawText(e.Graphics, text, e.Font, new Rectangle(e.Bounds.X + padding, e.Bounds.Y, e.Bounds.Width - padding * 3, e.Bounds.Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var problems = _problems.Where(p => p.Step == page.Step).ToList();
        if (problems.Count > 0)
        {
            var image = problems.Any(p => p.Blocking) ? _errorImage : _warningImage;
            var size = LogicalToDeviceUnits(16);
            e.Graphics.DrawImage(image, new Rectangle(e.Bounds.Right - size - padding / 2, e.Bounds.Y + (e.Bounds.Height - size) / 2, size, size));
        }
    }

    private void CreateEntry()
    {
        if (_draft is null)
        {
            return;
        }

        UpdateState();
        if (_problems.FirstOrDefault(p => p.Blocking) is { } problem)
        {
            ShowStep(problem.Step);
            SystemSounds.Beep.Play();
            return;
        }

        try
        {
            Result = _draft.Commit();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException)
        {
            Failure = ex;
            DialogResult = DialogResult.Abort;
        }
    }

    private IReadOnlyList<Choice<string>> Groups(GroupKind kind)
    {
        if (!_groups.TryGetValue(kind, out var list))
        {
            list = [.. _engine.Groups(kind)
                .Reverse()
                .Select(g => (Id: g.Element(kind.IdElement)?.Value.Trim() ?? string.Empty, Size: CreatorEngine.GroupSize(g)))
                .Where(g => g.Id.Length > 0)
                .DistinctBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => new Choice<string>(g.Id, $"{g.Id} ({g.Size})"))];
            _groups[kind] = list;
        }

        return list;
    }
}
