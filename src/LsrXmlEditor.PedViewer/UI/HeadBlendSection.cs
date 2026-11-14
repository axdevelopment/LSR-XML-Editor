using LsrXmlEditor.PedViewer.GameData;

namespace LsrXmlEditor.PedViewer.UI;

internal sealed class HeadBlendSection
{
    private const int MaxParent = 45;
    private const decimal MixStep = 0.05m;
    private const int LabelIndent = 16;

    private static readonly string[] ParentNames = ["Mother", "Father", "Third"];

    private readonly TableLayoutPanel _table;
    private readonly int _rowHeight;
    private readonly List<int> _rows = [];
    private readonly List<Control> _controls = [];
    private readonly NumericUpDown[] _shapes = new NumericUpDown[3];
    private readonly NumericUpDown[] _skins = new NumericUpDown[3];
    private readonly NumericUpDown _shapeMix;
    private readonly NumericUpDown _skinMix;
    private readonly NumericUpDown _thirdMix;
    private bool _suppress;
    private bool _shown = true;

    public HeadBlendSection(TableLayoutPanel table, int firstRow, int rowHeight)
    {
        _table = table;
        _rowHeight = rowHeight;
        var row = firstRow;

        Toggle = new CheckBox { Text = "Head &blend", Checked = true, AutoSize = false, Dock = DockStyle.Fill };
        Toggle.CheckedChanged += (_, _) => Raise();
        AddRow(row++, Toggle, Caption("Shape"), Caption("Skin"), null);

        for (var p = 0; p < ParentNames.Length; p++)
        {
            _shapes[p] = Number(0, MaxParent, 0, 1);
            _skins[p] = Number(0, MaxParent, 0, 1);
            AddRow(row++, Label(ParentNames[p]), _shapes[p], _skins[p], Hint(p == 2 ? "Optional third parent" : "Parent head"));
        }

        _shapeMix = Number(0, 1, 2, MixStep);
        _skinMix = Number(0, 1, 2, MixStep);
        _thirdMix = Number(0, 1, 2, MixStep);
        AddRow(row++, Label("Resemblance"), _shapeMix, null, Hint("0 = mother · 1 = father"));
        AddRow(row++, Label("Skin tone"), _skinMix, null, Hint("0 = mother · 1 = father"));
        AddRow(row, Label("Third mix"), _thirdMix, null, Hint("weight of the third parent"));
    }

    public event EventHandler? Changed;

    public CheckBox Toggle { get; }

    public HeadBlend? Current => _shown && Toggle.Checked
        ? new HeadBlend(
            (int)_shapes[0].Value, (int)_shapes[1].Value, (int)_shapes[2].Value,
            (int)_skins[0].Value, (int)_skins[1].Value, (int)_skins[2].Value,
            (float)_shapeMix.Value, (float)_skinMix.Value, (float)_thirdMix.Value)
        : null;

    public void Load(HeadBlend? blend, int maxHead)
    {
        _suppress = true;
        try
        {
            SetShown(blend is not null);
            var value = blend ?? HeadBlend.Default;
            int[] shapes = [value.ShapeFirst, value.ShapeSecond, value.ShapeThird];
            int[] skins = [value.SkinFirst, value.SkinSecond, value.SkinThird];
            for (var p = 0; p < ParentNames.Length; p++)
            {
                Set(_shapes[p], shapes[p], maxHead);
                Set(_skins[p], skins[p], maxHead);
            }

            _shapeMix.Value = (decimal)Math.Round(value.ShapeMix, 2);
            _skinMix.Value = (decimal)Math.Round(value.SkinMix, 2);
            _thirdMix.Value = (decimal)Math.Round(value.ThirdMix, 2);
            Toggle.Checked = true;
        }
        finally
        {
            _suppress = false;
        }
    }

    private static void Set(NumericUpDown control, int value, int max)
    {
        control.Maximum = Math.Max(0, max);
        control.Value = Math.Clamp(value, 0, (int)control.Maximum);
    }

    private void SetShown(bool shown)
    {
        _shown = shown;
        _table.SuspendLayout();
        foreach (var row in _rows)
        {
            _table.RowStyles[row].Height = shown ? _rowHeight : 0;
        }

        foreach (var control in _controls)
        {
            control.Visible = shown;
        }

        _table.ResumeLayout();
    }

    private void AddRow(int row, Control first, Control? second, Control? third, Control? fourth)
    {
        _table.RowStyles.Add(new RowStyle(SizeType.Absolute, _rowHeight));
        _rows.Add(row);
        Control?[] cells = [first, second, third, fourth];
        for (var column = 0; column < cells.Length; column++)
        {
            if (cells[column] is { } control)
            {
                _table.Controls.Add(control, column, row);
                _controls.Add(control);
            }
        }
    }

    private NumericUpDown Number(decimal min, decimal max, int decimals, decimal increment)
    {
        var control = new NumericUpDown { Dock = DockStyle.Fill, Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = increment };
        control.ValueChanged += (_, _) => Raise();
        return control;
    }

    private void Raise()
    {
        if (!_suppress)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static Label Label(string text) =>
        new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(LabelIndent, 0, 0, 0) };

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText };

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
}
