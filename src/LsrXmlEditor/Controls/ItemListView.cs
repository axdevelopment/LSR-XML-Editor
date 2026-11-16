using System.Globalization;
using LsrXmlEditor.Core.Themes;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Controls;

public sealed record ListColumn(string Header, int Width, HorizontalAlignment Alignment = HorizontalAlignment.Left);

public sealed record ListRow(object Tag, string[] Cells);

public sealed class ItemListView : ListView
{
    private const NumberStyles NumberCellStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint;
    private const TextFormatFlags CellFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

    private List<ListRow> _allRows = [];
    private List<ListRow> _rows = [];
    private string _filter = string.Empty;
    private int _sortColumn = -1;
    private bool _sortDescending;
    private bool _selectionQueued;
    private int _hot = -1;

    public ItemListView()
    {
        View = View.Details;
        VirtualMode = true;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = true;
        GridLines = false;
        BorderStyle = BorderStyle.None;
        HeaderStyle = ColumnHeaderStyle.Clickable;
        DoubleBuffered = true;
        RetrieveVirtualItem += (_, e) => e.Item = CreateItem(_rows[e.ItemIndex]);
        ColumnClick += (_, e) => SortBy(e.Column);
        SelectedIndexChanged += (_, _) => QueueSelectionSettled();
        VirtualItemsSelectionRangeChanged += (_, _) => QueueSelectionSettled();
        KeyDown += (_, e) =>
        {
            if (e.KeyData == (Keys.Control | Keys.A))
            {
                SelectAll();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        if (UiTheme.Palette is { } palette)
        {
            DrawThemed(palette);
        }
    }

    public event EventHandler? RowsChanged;

    public event EventHandler? SelectionSettled;

    public int RowCount => _rows.Count;

    public int TotalCount => _allRows.Count;

    public ListRow? SelectedRow => SelectedIndices.Count == 0 || SelectedIndices[0] >= _rows.Count ? null : _rows[SelectedIndices[0]];

    public IReadOnlyList<ListRow> SelectedRows
    {
        get
        {
            var rows = new List<ListRow>(SelectedIndices.Count);
            foreach (int index in SelectedIndices)
            {
                if (index < _rows.Count)
                {
                    rows.Add(_rows[index]);
                }
            }

            return rows;
        }
    }

    public void SelectAll()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        BeginUpdate();
        try
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                SelectedIndices.Add(i);
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    public void FlushSelection()
    {
        if (!_selectionQueued)
        {
            return;
        }

        _selectionQueued = false;
        SelectionSettled?.Invoke(this, EventArgs.Empty);
    }

    private void QueueSelectionSettled()
    {
        if (_selectionQueued || !IsHandleCreated)
        {
            return;
        }

        _selectionQueued = true;
        BeginInvoke(FlushSelection);
    }

    public void SetContent(IReadOnlyList<ListColumn> columns, IReadOnlyList<ListRow> rows)
    {
        BeginUpdate();
        try
        {
            SelectedIndices.Clear();
            VirtualListSize = 0;
            if (!SameColumns(columns))
            {
                Columns.Clear();
                foreach (var column in columns)
                {
                    Columns.Add(column.Header, LogicalToDeviceUnits(UiTheme.Grow(column.Width)), column.Alignment);
                }
            }

            _allRows = [.. rows];
            _sortColumn = -1;
            Rebuild();
        }
        finally
        {
            EndUpdate();
        }
    }

    public void ApplyFilter(string filter)
    {
        _filter = filter.Trim();
        var selected = SelectedRows.Select(r => r.Tag).ToList();
        BeginUpdate();
        try
        {
            Rebuild();
        }
        finally
        {
            EndUpdate();
        }

        RestoreSelection(selected);
    }

    private void RestoreSelection(IReadOnlyList<object> tags)
    {
        if (tags.Count == 0)
        {
            return;
        }

        if (tags.Count == 1)
        {
            SelectTag(tags[0], focus: false);
            return;
        }

        var wanted = new HashSet<object>(tags, ReferenceEqualityComparer.Instance);
        BeginUpdate();
        try
        {
            SelectedIndices.Clear();
            for (var i = 0; i < _rows.Count; i++)
            {
                if (wanted.Contains(_rows[i].Tag))
                {
                    SelectedIndices.Add(i);
                }
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    public void UpdateRow(object tag, string[] cells)
    {
        var source = _allRows.FindIndex(r => ReferenceEquals(r.Tag, tag));
        if (source >= 0)
        {
            _allRows[source] = _allRows[source] with { Cells = cells };
        }

        var index = _rows.FindIndex(r => ReferenceEquals(r.Tag, tag));
        if (index >= 0)
        {
            _rows[index] = _rows[index] with { Cells = cells };
            RedrawItems(index, index, true);
        }
    }

    public bool SelectTag(object tag, bool focus = true) => SelectWhere(t => Equals(t, tag), focus);

    public bool SelectWhere(Func<object, bool> predicate, bool focus = true)
    {
        var index = _rows.FindIndex(r => predicate(r.Tag));
        if (index < 0)
        {
            return false;
        }

        SelectedIndices.Clear();
        SelectedIndices.Add(index);
        FocusedItem = Items[index];
        EnsureVisible(index);
        if (focus)
        {
            Focus();
        }

        return true;
    }

    public void SelectClosest(Func<object, int?> position, int target)
    {
        var best = -1;
        var distance = int.MaxValue;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (position(_rows[i].Tag) is { } value && Math.Abs(value - target) < distance)
            {
                best = i;
                distance = Math.Abs(value - target);
            }
        }

        if (best >= 0)
        {
            SelectIndex(best);
        }
    }

    public void SelectIndex(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        index = Math.Clamp(index, 0, _rows.Count - 1);
        SelectedIndices.Clear();
        SelectedIndices.Add(index);
        EnsureVisible(index);
    }

    private void DrawThemed(ThemePalette palette)
    {
        OwnerDraw = true;
        DrawColumnHeader += (_, e) =>
        {
            using (var back = new SolidBrush(palette.Header))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }

            using (var line = new Pen(palette.Border))
            {
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            TextRenderer.DrawText(e.Graphics, e.Header?.Text, Font, Padded(e.Bounds), palette.Text, CellFlags | Align(e.Header?.TextAlign));
        };
        DrawItem += (_, _) => { };
        DrawSubItem += (_, e) =>
        {
            var selected = SelectedIndices.Contains(e.ItemIndex);
            var (back, fore) = selected
                ? Focused ? (palette.Selection, palette.SelectionText) : (palette.InactiveSelection, palette.Text)
                : e.ItemIndex == _hot ? (palette.Hover, palette.Text) : (palette.Window, palette.Text);
            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, Font, Padded(e.Bounds), fore, CellFlags | Align(Columns[e.ColumnIndex].TextAlign));
        };
        MouseMove += (_, e) => Hover(HitTest(e.Location).Item?.Index ?? -1);
        MouseLeave += (_, _) => Hover(-1);
        GotFocus += (_, _) => Invalidate();
        LostFocus += (_, _) => Invalidate();
    }

    private void Hover(int index)
    {
        if (index == _hot)
        {
            return;
        }

        var previous = _hot;
        _hot = index;
        foreach (var row in new[] { previous, index }.Where(r => r >= 0 && r < VirtualListSize))
        {
            RedrawItems(row, row, true);
        }
    }

    private Rectangle Padded(Rectangle bounds) =>
        new(bounds.X + LogicalToDeviceUnits(6), bounds.Y, Math.Max(0, bounds.Width - LogicalToDeviceUnits(10)), bounds.Height);

    private static TextFormatFlags Align(HorizontalAlignment? alignment) => alignment switch
    {
        HorizontalAlignment.Right => TextFormatFlags.Right,
        HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
        _ => TextFormatFlags.Left
    };

    private void Rebuild()
    {
        _rows = _filter.Length == 0
            ? _allRows.ToList()
            : _allRows.Where(r => r.Cells.Any(c => c.Contains(_filter, StringComparison.OrdinalIgnoreCase))).ToList();
        SortRows();
        VirtualListSize = _rows.Count;
        Invalidate();
        RowsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SortBy(int column)
    {
        _sortDescending = column == _sortColumn && !_sortDescending;
        _sortColumn = column;
        var selected = SelectedRows.Select(r => r.Tag).ToList();
        SortRows();
        Invalidate();
        RestoreSelection(selected);
    }

    private void SortRows()
    {
        if (_sortColumn < 0)
        {
            return;
        }

        var column = _sortColumn;
        Comparison<ListRow> comparison = (a, b) => CompareCells(a.Cells.ElementAtOrDefault(column), b.Cells.ElementAtOrDefault(column));
        _rows.Sort(_sortDescending ? (a, b) => comparison(b, a) : comparison);
    }

    private static int CompareCells(string? a, string? b)
    {
        if (TryNumber(a, out var x) && TryNumber(b, out var y))
        {
            return x.CompareTo(y);
        }

        return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool TryNumber(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var space = text.IndexOf(' ');
        var head = space < 0 ? text.AsSpan() : text.AsSpan(0, space);
        return double.TryParse(head, NumberCellStyle, Format.Culture, out value);
    }

    private bool SameColumns(IReadOnlyList<ListColumn> columns) =>
        Columns.Count == columns.Count && columns.Select((c, i) => Columns[i].Text == c.Header).All(x => x);

    private static ListViewItem CreateItem(ListRow row)
    {
        var item = new ListViewItem(row.Cells.Length > 0 ? row.Cells[0] : string.Empty) { Tag = row.Tag };
        for (var i = 1; i < row.Cells.Length; i++)
        {
            item.SubItems.Add(row.Cells[i]);
        }

        return item;
    }
}
