using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class LinkEditor : FlowLayoutPanel
{
    private const string CopyText = "Own copy";
    private const string ShareText = "Share";
    private const string NoneText = "(none)";
    private const int ListedUsers = 30;

    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 20 };
    private readonly TextBox _newId = new() { PlaceholderText = "New ID" };
    private readonly Label _arrow = new() { Text = "→", AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
    private readonly Label _usage = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 6, 0, 0), UseMnemonic = false };
    private readonly ToolTip _tip = new() { AutoPopDelay = 30000 };
    private readonly LinkChoice _link;
    private readonly CreatorEngine _engine;
    private readonly Action<string> _idEdited;
    private bool _loading;

    public LinkEditor(LinkChoice link, IReadOnlyList<Choice<string>> groups, CreatorEngine engine, Action<string> idEdited)
    {
        _link = link;
        _engine = engine;
        _idEdited = idEdited;
        AutoSize = true;
        WrapContents = false;
        Margin = new Padding(0, 1, 0, 1);
        _mode.Width = LogicalToDeviceUnits(UiTheme.Grow(100));
        _group.Width = LogicalToDeviceUnits(UiTheme.Grow(210));
        _newId.Width = LogicalToDeviceUnits(UiTheme.Grow(150));
        _mode.Items.AddRange([CopyText, ShareText]);
        _group.Items.Add(new Choice<string>(string.Empty, NoneText));
        _group.Items.AddRange([.. groups]);
        if (link.SourceId.Length > 0 && !groups.Any(g => string.Equals(g.Value, link.SourceId, StringComparison.OrdinalIgnoreCase)))
        {
            _group.Items.Add(new Choice<string>(link.SourceId, $"{link.SourceId} (not found)"));
        }
        Controls.AddRange([_mode, _group, _arrow, _newId, _usage]);

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
                UpdateState();
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }

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
        var users = _engine.Users(_link.Link.Kind, _link.SourceId);
        _usage.Visible = users.Count > 0;
        _usage.Text = $"used by {users.Count}";
        var listed = string.Join(Environment.NewLine, users.Take(ListedUsers));
        _tip.SetToolTip(_usage, users.Count > ListedUsers ? $"{listed}{Environment.NewLine}… and {users.Count - ListedUsers} more" : listed);
    }
}
