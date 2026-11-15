using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class LinkEditor : FlowLayoutPanel
{
    private const string CopyText = "Own copy";
    private const string ShareText = "Share";
    private const string NoneText = "(none)";

    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
    private readonly TextBox _newId = new() { PlaceholderText = "New ID" };
    private readonly Label _arrow = new() { Text = "→", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
    private readonly LinkChoice _link;
    private readonly Action<string> _idEdited;
    private bool _loading;

    public LinkEditor(LinkChoice link, IReadOnlyList<Choice<string>> groups, Action<string> idEdited)
    {
        _link = link;
        _idEdited = idEdited;
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0, 1, 0, 1);
        _mode.Width = LogicalToDeviceUnits(100);
        _group.Width = LogicalToDeviceUnits(250);
        _newId.Width = LogicalToDeviceUnits(200);
        _mode.Items.AddRange([CopyText, ShareText]);
        _group.Items.Add(new Choice<string>(string.Empty, NoneText));
        _group.Items.AddRange([.. groups]);
        Controls.AddRange([_mode, _group, _arrow, _newId]);

        _mode.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading)
            {
                _link.Copy = _mode.SelectedIndex == 0;
                UpdateState();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        _group.SelectedIndexChanged += (_, _) =>
        {
            if (!_loading && _group.SelectedItem is Choice<string> choice)
            {
                _link.SourceId = choice.Value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        _newId.TextChanged += (_, _) =>
        {
            if (!_loading)
            {
                _idEdited(_newId.Text);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
        Sync();
    }

    public event EventHandler? Changed;

    private void Sync()
    {
        _loading = true;
        _mode.SelectedIndex = _link.Copy ? 0 : 1;
        var index = _group.Items.Cast<Choice<string>>().ToList().FindIndex(c => string.Equals(c.Value, _link.SourceId, StringComparison.OrdinalIgnoreCase));
        _group.SelectedIndex = Math.Max(index, 0);
        if (!_newId.Focused)
        {
            _newId.Text = _link.NewId;
        }

        UpdateState();
        _loading = false;
    }

    private void UpdateState()
    {
        _arrow.Visible = _link.Copy;
        _newId.Visible = _link.Copy;
    }
}
