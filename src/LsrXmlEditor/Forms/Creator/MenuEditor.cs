using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class MenuEditor : TableLayoutPanel
{
    private static readonly (string Field, string Header, int Width)[] NumberColumns =
    [
        (MenuEditing.BuyField, "Player buys for", 100),
        (MenuEditing.SellField, "Player sells for", 100),
        (MenuEditing.StockField, "Stock", 70)
    ];

    private readonly DataGridView _grid = new()
    {
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2
    };

    private readonly ComboBox _item = new()
    {
        DropDownStyle = ComboBoxStyle.DropDown,
        AutoCompleteMode = AutoCompleteMode.SuggestAppend,
        AutoCompleteSource = AutoCompleteSource.ListItems,
        MaxDropDownItems = 20
    };

    private readonly CreatorEngine _engine;
    private readonly Dictionary<string, string> _categories;
    private readonly bool _editable;
    private XElement? _menu;
    private bool _loading;

    public MenuEditor(IReadOnlyList<MenuChoice> menus, bool editable, CreatorEngine engine)
    {
        _engine = engine;
        _editable = editable;
        _categories = engine.ModItems().ToDictionary(i => i.Name, i => i.Category, StringComparer.OrdinalIgnoreCase);
        AutoSize = true;
        ColumnCount = 1;
        Margin = new Padding(0, 0, 0, 6);

        BuildGrid();
        ComboBox? variants = null;
        if (menus.Count > 1)
        {
            variants = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = LogicalToDeviceUnits(420), MaxDropDownItems = 20 };
            variants.Items.AddRange([.. menus.Select(m => new Choice<XElement>(m.Menu, m.Title))]);
            variants.SelectedIndexChanged += (_, _) =>
            {
                if (variants.SelectedItem is Choice<XElement> choice)
                {
                    Display(choice.Value);
                }
            };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.AddRange([new Label { Text = "Variant:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, variants]);
            Controls.Add(row);
        }

        Controls.Add(_grid);
        if (editable)
        {
            Controls.Add(BuildToolbar());
        }

        if (variants is not null)
        {
            variants.SelectedIndex = 0;
        }
        else if (menus.Count > 0)
        {
            Display(menus[0].Menu);
        }
    }

    public event EventHandler? Changed;

    private void BuildGrid()
    {
        _grid.Size = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(260));
        _grid.ReadOnly = !_editable;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Item", Width = LogicalToDeviceUnits(220), ReadOnly = true, ValueType = typeof(string) });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Width = LogicalToDeviceUnits(80), ReadOnly = true, ValueType = typeof(string) });
        foreach (var (_, header, width) in NumberColumns)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Width = LogicalToDeviceUnits(width),
                ValueType = typeof(int),
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight }
            });
        }

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Illicit", Width = LogicalToDeviceUnits(50), ValueType = typeof(bool) });
        _grid.CellValidating += (_, e) =>
        {
            if (!_loading && IsNumberColumn(e.ColumnIndex) && !int.TryParse(Convert.ToString(e.FormattedValue, CultureInfo.CurrentCulture), NumberStyles.Integer, CultureInfo.CurrentCulture, out int _))
            {
                _grid.Rows[e.RowIndex].ErrorText = "Enter a whole number.";
                e.Cancel = true;
            }
        };
        _grid.CellEndEdit += (_, e) => _grid.Rows[e.RowIndex].ErrorText = string.Empty;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellValueChanged += (_, e) => Write(e.RowIndex, e.ColumnIndex);
        _grid.KeyDown += (_, e) =>
        {
            if (_editable && e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode)
            {
                RemoveSelected();
                e.Handled = true;
            }
        };
    }

    private FlowLayoutPanel BuildToolbar()
    {
        _item.Width = LogicalToDeviceUnits(300);
        _item.Items.AddRange([.. _engine.ModItems().Select(i => i.Name)]);
        var add = new Button { Text = "&Add", AutoSize = true };
        var remove = new Button { Text = "Re&move selected", AutoSize = true };
        add.Click += (_, _) => AddItem();
        remove.Click += (_, _) => RemoveSelected();
        _item.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                AddItem();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        var toolbar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        toolbar.Controls.AddRange([new Label { Text = "Item:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) }, _item, add, remove]);
        return toolbar;
    }

    private void Display(XElement menu)
    {
        _menu = menu;
        _loading = true;
        _grid.Rows.Clear();
        foreach (var item in MenuEditing.Items(menu))
        {
            AddRow(item);
        }

        _loading = false;
    }

    private int AddRow(XElement item)
    {
        var name = MenuEditing.Text(item, MenuEditing.ItemField);
        var index = _grid.Rows.Add(
            name,
            _categories.GetValueOrDefault(name, "?"),
            MenuEditing.Number(item, MenuEditing.BuyField),
            MenuEditing.Number(item, MenuEditing.SellField),
            MenuEditing.Number(item, MenuEditing.StockField),
            MenuEditing.Flag(item, MenuEditing.IllicitField));
        _grid.Rows[index].Tag = item;
        return index;
    }

    private void Write(int row, int column)
    {
        if (_loading || row < 0 || _grid.Rows[row].Tag is not XElement item)
        {
            return;
        }

        var value = _grid.Rows[row].Cells[column].Value;
        if (IsNumberColumn(column))
        {
            MenuEditing.SetNumber(item, NumberColumns[column - 2].Field, Convert.ToInt32(value, CultureInfo.CurrentCulture));
        }
        else if (column == _grid.ColumnCount - 1)
        {
            MenuEditing.SetFlag(item, MenuEditing.IllicitField, value is true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddItem()
    {
        var name = _item.Text.Trim();
        if (_menu is null || name.Length == 0)
        {
            return;
        }

        var existing = _grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => string.Equals(r.Cells[0].Value as string, name, StringComparison.OrdinalIgnoreCase));
        var index = existing?.Index ?? -1;
        if (existing is null)
        {
            var item = MenuEditing.Add(_menu, name, _engine.MenuItemPattern(name));
            _loading = true;
            index = AddRow(item);
            _loading = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        _grid.ClearSelection();
        _grid.Rows[index].Selected = true;
        _grid.FirstDisplayedScrollingRowIndex = index;
        _grid.CurrentCell = _grid.Rows[index].Cells[2];
        _item.Text = string.Empty;
    }

    private void RemoveSelected()
    {
        var rows = _grid.SelectedRows.Cast<DataGridViewRow>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (row.Tag is XElement item)
            {
                MenuEditing.Remove(item);
            }

            _grid.Rows.Remove(row);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsNumberColumn(int column) => column >= 2 && column < 2 + NumberColumns.Length;
}
