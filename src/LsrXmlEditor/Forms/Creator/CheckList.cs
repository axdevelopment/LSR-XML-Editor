namespace LsrXmlEditor.Forms.Creator;

internal sealed class CheckList : TableLayoutPanel
{
    private const int ListHeight = 220;
    private const int ListWidth = 520;

    private readonly TextBox _filter = new() { PlaceholderText = "Filter...", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 4) };
    private readonly CheckedListBox _list = new() { CheckOnClick = true, IntegralHeight = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Label _count = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 0, 0, 4) };
    private IReadOnlyList<(string Key, string Text)> _items = [];
    private HashSet<string> _selected = [];
    private bool _filling;

    public CheckList()
    {
        AutoSize = false;
        ColumnCount = 2;
        RowCount = 2;
        Margin = new Padding(0, 0, 0, 4);
        Size = new Size(LogicalToDeviceUnits(ListWidth), LogicalToDeviceUnits(ListHeight));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(90)));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(_filter, 0, 0);
        Controls.Add(_count, 1, 0);
        Controls.Add(_list, 0, 1);
        SetColumnSpan(_list, 2);
        _filter.TextChanged += (_, _) => Fill();
        _list.ItemCheck += OnItemCheck;
    }

    public event EventHandler<string>? Toggled;

    public void Bind(IReadOnlyList<(string Key, string Text)> items, HashSet<string> selected)
    {
        _items = items;
        _selected = selected;
        Fill();
    }

    public void Rebind(HashSet<string> selected)
    {
        _selected = selected;
        Fill();
    }

    private void Fill()
    {
        _filling = true;
        var filter = _filter.Text.Trim();
        var visible = _items
            .Where(i => filter.Length == 0 || i.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(i => _selected.Contains(i.Key))
            .ToList();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var item in visible)
        {
            _list.Items.Add(new Choice<string>(item.Key, item.Text), _selected.Contains(item.Key));
        }

        _list.EndUpdate();
        _filling = false;
        UpdateCount();
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_filling || _list.Items[e.Index] is not Choice<string> choice)
        {
            return;
        }

        if (e.NewValue == CheckState.Checked)
        {
            _selected.Add(choice.Value);
        }
        else
        {
            _selected.Remove(choice.Value);
        }

        UpdateCount();
        Toggled?.Invoke(this, choice.Value);
    }

    private void UpdateCount() => _count.Text = $"{_selected.Count} selected";
}
