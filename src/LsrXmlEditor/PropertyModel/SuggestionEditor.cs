using System.ComponentModel;
using System.Drawing.Design;
using System.Windows.Forms.Design;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.PropertyModel;

public sealed class SuggestionEditor : UITypeEditor
{
    public static SuggestionEditor Instance { get; } = new();

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.DropDown;

    public override bool IsDropDownResizable => true;

    public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
    {
        if (context?.PropertyDescriptor is not XmlFieldDescriptor { Suggestions: { } suggestions }
            || provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService service)
        {
            return value;
        }

        using var picker = new SuggestionPicker(suggestions.Values(), value as string ?? string.Empty, service.CloseDropDown, Form.ActiveForm?.DeviceDpi ?? SuggestionPicker.BaseDpi);
        service.DropDownControl(picker);
        return picker.Picked ?? value;
    }
}

internal sealed class SuggestionPicker : UserControl
{
    public const int BaseDpi = 96;

    private const int VisibleRows = 16;
    private const int PickerWidth = 340;

    private readonly IReadOnlyList<string> _values;
    private readonly Action _close;
    private readonly TextBox _filter = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "Type to filter" };
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };

    public SuggestionPicker(IReadOnlyList<string> values, string current, Action close, int dpi)
    {
        _values = values;
        _close = close;
        Font = UiTheme.Font;
        BackColor = _filter.BackColor = _list.BackColor = UiTheme.Window;
        ForeColor = _filter.ForeColor = _list.ForeColor = UiTheme.Text;
        Controls.Add(_list);
        Controls.Add(_filter);
        Size = new Size(UiTheme.Grow(PickerWidth) * dpi / BaseDpi, _filter.PreferredHeight + _list.ItemHeight * VisibleRows + 2);
        ActiveControl = _filter;

        _filter.TextChanged += (_, _) => Fill(null);
        _filter.KeyDown += (_, e) => e.Handled = e.SuppressKeyPress = Step(e.KeyCode);
        _list.KeyPress += (_, e) => Redirect(e);
        _list.MouseClick += (_, _) => Pick();
        Fill(current);
    }

    public string? Picked { get; private set; }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Enter:
                Pick();
                return true;
            case Keys.Escape:
                _close();
                return true;
            default:
                return base.ProcessDialogKey(keyData);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(_filter.Focus);
    }

    private void Fill(string? select)
    {
        var terms = _filter.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = terms.Length == 0
            ? _values
            : [.. _values.Where(v => terms.All(t => v.Contains(t, StringComparison.OrdinalIgnoreCase)))];

        _list.BeginUpdate();
        _list.Items.Clear();
        _list.Items.AddRange([.. matches]);
        _list.EndUpdate();

        var index = select is null ? -1 : _list.FindStringExact(select);
        _list.SelectedIndex = index >= 0 ? index : _list.Items.Count > 0 ? 0 : -1;
    }

    private bool Step(Keys key)
    {
        var step = key switch
        {
            Keys.Down => 1,
            Keys.Up => -1,
            Keys.PageDown => VisibleRows,
            Keys.PageUp => -VisibleRows,
            _ => 0
        };

        if (step == 0 || _list.Items.Count == 0)
        {
            return false;
        }

        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + step, 0, _list.Items.Count - 1);
        return true;
    }

    private void Redirect(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
        {
            return;
        }

        _filter.Focus();
        _filter.AppendText(e.KeyChar.ToString());
        e.Handled = true;
    }

    private void Pick()
    {
        Picked = _list.SelectedItem as string ?? (_filter.Text.Trim() is { Length: > 0 } typed ? typed : null);
        _close();
    }
}
