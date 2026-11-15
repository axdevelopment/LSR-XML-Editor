using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal abstract class CreatorPage : TableLayoutPanel
{
    protected const int FieldWidth = 300;
    private Font? _sectionFont;

    protected CreatorPage(CreatorStep step, string title, string description)
    {
        Step = step;
        Title = title;
        Description = description;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        Padding = new Padding(0, 0, 8, 12);
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public event EventHandler? Changed;

    public CreatorStep Step { get; }

    public string Title { get; }

    public string Description { get; }

    public virtual bool FillsHeight => false;

    protected bool Loading { get; private set; }

    public void Present(CreatorDraft draft)
    {
        Loading = true;
        SuspendLayout();
        try
        {
            Bind(draft);
        }
        finally
        {
            ResumeLayout();
            Loading = false;
        }
    }

    protected abstract void Bind(CreatorDraft draft);

    protected void Quietly(Action action)
    {
        var loading = Loading;
        Loading = true;
        try
        {
            action();
        }
        finally
        {
            Loading = loading;
        }
    }

    internal void OnChanged()
    {
        if (!Loading)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    protected void Clear()
    {
        foreach (var control in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(control);
            control.Dispose();
        }

        RowStyles.Clear();
        RowCount = 0;
    }

    protected Label AddSection(string title, string? hint = null)
    {
        _sectionFont ??= new Font(Font, FontStyle.Bold);
        var label = new Label { Text = title, AutoSize = true, UseMnemonic = false, Font = _sectionFont, Margin = new Padding(0, RowCount == 0 ? 0 : 14, 0, 4) };
        AddSpanning(label);
        if (hint is not null)
        {
            AddHint(hint);
        }

        return label;
    }

    protected Label AddHint(string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            UseMnemonic = false,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6),
            MaximumSize = new Size(LogicalToDeviceUnits(640), 0)
        };
        AddSpanning(label);
        return label;
    }

    protected T AddRow<T>(string caption, T control) where T : Control
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 7, 12, 0) }, 0, row);
        if (control is ComboBox or TextBox && control.Width < LogicalToDeviceUnits(FieldWidth))
        {
            control.Width = LogicalToDeviceUnits(FieldWidth);
        }

        control.Anchor = AnchorStyles.Left;
        Controls.Add(control, 1, row);
        return control;
    }

    protected T AddSpanning<T>(T control) where T : Control
    {
        var row = RowCount++;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(control, 0, row);
        SetColumnSpan(control, 2);
        return control;
    }

    protected static ComboBox Combo(bool editable = false) => new()
    {
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
        AutoCompleteMode = editable ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None,
        AutoCompleteSource = editable ? AutoCompleteSource.ListItems : AutoCompleteSource.None,
        MaxDropDownItems = 20
    };

    protected TextBox Field(string value, Action<string> changed)
    {
        var box = new TextBox { Text = value };
        box.TextChanged += (_, _) =>
        {
            if (!Loading)
            {
                changed(box.Text);
                OnChanged();
            }
        };
        return box;
    }

    protected CheckBox Check(string text, bool value, Action<bool> changed)
    {
        var box = new CheckBox { Text = text, AutoSize = true, Checked = value, Margin = new Padding(0, 4, 0, 4) };
        box.CheckedChanged += (_, _) =>
        {
            if (!Loading)
            {
                changed(box.Checked);
                OnChanged();
            }
        };
        return box;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sectionFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed record Choice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}
