using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class SummaryPage : CreatorPage
{
    private const string ErrorKey = "error";
    private const string WarningKey = "warning";
    private const string ChangeKey = "change";

    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        HeaderStyle = ColumnHeaderStyle.None,
        FullRowSelect = true,
        ShowItemToolTips = true,
        Margin = Padding.Empty
    };

    private readonly ImageList _images = new() { ColorDepth = ColorDepth.Depth32Bit };
    private readonly Action<CreatorStep> _jump;
    private readonly Dictionary<string, string> _zones;

    public SummaryPage(CreatorData data, Action<CreatorStep> jump)
        : base(CreatorStep.Summary, "Summary", "Check what will be added, then click Create.")
    {
        _jump = jump;
        _zones = data.Zones.ToDictionary(z => z.Code, z => z.Name, StringComparer.OrdinalIgnoreCase);
        AutoSize = false;
        Dock = DockStyle.Fill;
        RowCount = 2;
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var size = LogicalToDeviceUnits(16);
        _images.ImageSize = new Size(size, size);
        _images.Images.Add(ErrorKey, StockImages.Small(StockIconId.Error));
        _images.Images.Add(WarningKey, StockImages.Small(StockIconId.Warning));
        _images.Images.Add(ChangeKey, StockImages.Small(StockIconId.DocumentWithAssociation));
        _list.SmallImageList = _images;
        _list.Columns.Add(string.Empty);
        _list.Resize += (_, _) => _list.Columns[0].Width = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        _list.ItemActivate += (_, _) =>
        {
            if (_list.SelectedItems.Count > 0 && _list.SelectedItems[0].Tag is CreatorStep step)
            {
                _jump(step);
            }
        };
        var note = new Label
        {
            Text = "Nothing is written to disk yet. The new entries open in the editor – save them with Save all (Ctrl+Shift+S). One Ctrl+Z removes everything again.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0),
            MaximumSize = new Size(LogicalToDeviceUnits(640), 0)
        };
        Controls.Add(_list, 0, 0);
        SetColumnSpan(_list, 2);
        Controls.Add(note, 0, 1);
        SetColumnSpan(note, 2);
    }

    public override bool FillsHeight => true;

    protected override void Bind(CreatorDraft draft)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Groups.Clear();
        var problems = draft.Problems();
        if (problems.Count > 0)
        {
            var group = _list.Groups.Add("problems", "To check (double-click to jump there)");
            foreach (var problem in problems)
            {
                _list.Items.Add(new ListViewItem(problem.Message, problem.Blocking ? ErrorKey : WarningKey) { Group = group, Tag = problem.Step, ToolTipText = problem.Message });
            }
        }

        foreach (var file in draft.Changes(code => _zones.GetValueOrDefault(code, code)).GroupBy(c => c.File))
        {
            var group = _list.Groups.Add(file.Key, file.Key);
            foreach (var change in file)
            {
                _list.Items.Add(new ListViewItem(change.Text, ChangeKey) { Group = group, ToolTipText = change.Text });
            }
        }

        _list.EndUpdate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _images.Dispose();
        }

        base.Dispose(disposing);
    }
}
