using System.Xml;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;
using LsrXmlEditor.PropertyModel;

namespace LsrXmlEditor.Controls;

public sealed class EntryEditor : UserControl
{
    private const float TitleScale = 4f / 3f;
    private const float XmlFontSize = 9.5f;
    private const string XmlFontName = "Consolas";

    private readonly Font _titleFont;
    private readonly Font _xmlFont = new(XmlFontName, XmlFontSize);
    private readonly Label _title = new() { AutoSize = true, UseMnemonic = false, Margin = new Padding(0) };
    private readonly Label _subtitle = new() { AutoSize = true, UseMnemonic = false, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 0) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _xmlPage = new("XML");
    private string _xmlLoaded = string.Empty;
    private readonly TextBox _xml = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        AcceptsTab = true,
        BorderStyle = BorderStyle.None
    };

    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        UseMnemonic = false,
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(10, 12, 10, 0),
        Text = """
            Pick a section or file on the left and select an entry in the list to edit it here.
            Select several entries (Ctrl/Shift+Click, Ctrl+A) to change a setting in all of them at once –
            fields whose values differ between the selected entries are shown empty.

            Keyboard shortcuts
              Ctrl+F – Filter list
              Ctrl+Shift+F – Search all files
              Ctrl+D – Duplicate entry
              Del – Delete entry
              Ctrl+S – Save (with automatic backup)
              F5 – Reload folder
            """
    };

    private readonly Label _xmlStatus = new() { AutoSize = true, UseMnemonic = false, Padding = new Padding(8, 7, 0, 0) };
    private readonly ToolStripMenuItem _cloneItem = new("&Duplicate element");
    private readonly ToolStripMenuItem _removeItem = new("&Remove element");
    private XElement? _entry;
    private FieldKindIndex? _kinds;

    public EntryEditor()
    {
        SuspendLayout();
        _titleFont = new Font(Font.FontFamily, Font.SizeInPoints * TitleScale, FontStyle.Bold);
        _title.Font = _titleFont;
        _xml.Font = _xmlFont;

        Grid = new PropertyGrid
        {
            Dock = DockStyle.Fill,
            PropertySort = PropertySort.Categorized,
            ToolbarVisible = true,
            HelpVisible = true,
            LineColor = SystemColors.ControlLight
        };

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(8, 6, 8, 6)
        };
        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);

        var propertiesPage = new TabPage("Properties") { Padding = new Padding(0) };
        propertiesPage.Controls.Add(Grid);

        var applyButton = new Button { Text = "&Apply", AutoSize = true };
        var resetButton = new Button { Text = "&Revert", AutoSize = true };
        applyButton.Click += (_, _) => ApplyXml();
        resetButton.Click += (_, _) => LoadXml();
        var xmlButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        xmlButtons.Controls.AddRange([applyButton, resetButton, _xmlStatus]);
        _xmlPage.Controls.Add(_xml);
        _xmlPage.Controls.Add(xmlButtons);

        _tabs.TabPages.Add(propertiesPage);
        _tabs.TabPages.Add(_xmlPage);
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == _xmlPage)
            {
                LoadXml();
            }
        };

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_cloneItem, _removeItem]);
        menu.Opening += (_, e) =>
        {
            var enabled = SelectedItemDescriptor is not null;
            _cloneItem.Enabled = enabled;
            _removeItem.Enabled = enabled;
        };
        _cloneItem.Click += (_, _) => CloneSelectedItem();
        _removeItem.Click += (_, _) => RemoveSelectedItem();
        Grid.ContextMenuStrip = menu;

        Controls.Add(_tabs);
        Controls.Add(_hint);
        Controls.Add(header);
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout();
        Clear();
    }

    public event EventHandler<XElement>? FieldChanged;

    public event EventHandler? StructureChanged;

    public event EventHandler<IReadOnlyList<XElement>>? MultiFieldChanged;

    public PropertyGrid Grid { get; }

    public XElement? Entry => _entry;

    public void ShowEntry(XElement entry, FieldKindIndex kinds, string title, string subtitle)
    {
        _entry = entry;
        _kinds = kinds;
        if (!_tabs.TabPages.Contains(_xmlPage))
        {
            _tabs.TabPages.Add(_xmlPage);
        }

        _title.Text = title;
        _subtitle.Text = subtitle;
        Grid.SelectedObject = new XmlNodeObject(entry, CreateContext(entry, kinds));
        _xmlStatus.Text = string.Empty;
        _hint.Visible = false;
        _tabs.Visible = true;
        if (_tabs.SelectedTab == _xmlPage)
        {
            LoadXml();
        }
    }

    public void ShowEntries(IReadOnlyList<XElement> entries, FieldKindIndex kinds, string title, string subtitle)
    {
        _entry = null;
        _kinds = kinds;
        _title.Text = title;
        _subtitle.Text = subtitle;
        _xml.Text = string.Empty;
        _xmlStatus.Text = string.Empty;
        if (_tabs.TabPages.Contains(_xmlPage))
        {
            _tabs.TabPages.Remove(_xmlPage);
        }

        var context = new EditContext(entries, kinds, changed => MultiFieldChanged?.Invoke(this, changed), RefreshGrid);
        Grid.SelectedObject = new MultiNodeObject(entries, context);
        _hint.Visible = false;
        _tabs.Visible = true;
    }

    public void ShowMessage(string title, string subtitle)
    {
        Clear();
        _title.Text = title;
        _subtitle.Text = subtitle;
    }

    public void Clear()
    {
        _entry = null;
        _kinds = null;
        _title.Text = string.Empty;
        _subtitle.Text = string.Empty;
        Grid.SelectedObject = null;
        _xml.Text = string.Empty;
        _tabs.Visible = false;
        _hint.Visible = true;
    }

    public void UpdateHeader(string title, string subtitle)
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
    }

    public bool HasPendingXml => _entry is not null && _tabs.SelectedTab == _xmlPage && _xml.Text != _xmlLoaded;

    public bool ApplyPendingXml()
    {
        if (HasPendingXml)
        {
            ApplyXml();
        }

        return !HasPendingXml;
    }

    public void CommitEdit()
    {
        if (Grid.ContainsFocus)
        {
            _tabs.Focus();
            Grid.Focus();
        }
    }

    public void RevealField(string path)
    {
        if (string.IsNullOrEmpty(path) || Grid.SelectedGridItem is null)
        {
            return;
        }

        var root = Grid.SelectedGridItem;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var segments = path.Split('/');
        var level = root.GridItems.Cast<GridItem>().SelectMany(Flatten);
        GridItem? match = null;
        foreach (var segment in segments)
        {
            match = level.FirstOrDefault(g => g.PropertyDescriptor is XmlFieldDescriptor d && d.Segment == segment);
            if (match is null)
            {
                return;
            }

            if (match.Expandable)
            {
                match.Expanded = true;
            }

            level = match.GridItems.Cast<GridItem>();
        }

        match?.Select();
        _tabs.SelectedIndex = 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _xmlFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private static IEnumerable<GridItem> Flatten(GridItem item) =>
        item.GridItemType == GridItemType.Category ? item.GridItems.Cast<GridItem>() : [item];

    private static Color ErrorColor => SystemInformation.HighContrast ? SystemColors.ControlText : Color.Firebrick;

    private XmlFieldDescriptor? SelectedItemDescriptor =>
        Grid.SelectedGridItem?.PropertyDescriptor is XmlFieldDescriptor { IsCollectionItem: true } descriptor ? descriptor : null;

    private EditContext CreateContext(XElement entry, FieldKindIndex kinds) =>
        new([entry], kinds, changed => FieldChanged?.Invoke(this, changed[0]), RefreshGrid);

    private void RefreshGrid() => BeginInvoke(Grid.Refresh);

    private void ShowXmlStatus(string text, bool isError)
    {
        _xmlStatus.ForeColor = isError ? ErrorColor : SystemColors.ControlText;
        _xmlStatus.Text = text;
    }

    private void CloneSelectedItem()
    {
        if (SelectedItemDescriptor is not { } descriptor)
        {
            return;
        }

        XmlEditing.CloneAfter(descriptor.Element);
        Rebind();
    }

    private void RemoveSelectedItem()
    {
        if (SelectedItemDescriptor is not { } descriptor)
        {
            return;
        }

        XmlEditing.Remove(descriptor.Element);
        Rebind();
    }

    private void Rebind()
    {
        if (_entry is null || _kinds is null)
        {
            return;
        }

        Grid.SelectedObject = new XmlNodeObject(_entry, CreateContext(_entry, _kinds));
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LoadXml()
    {
        _xmlStatus.Text = string.Empty;
        _xml.Text = _entry is null ? string.Empty : XmlEditing.Serialize(_entry).Replace("\r\n", "\n").Replace("\n", "\r\n");
        _xmlLoaded = _xml.Text;
    }

    private void ApplyXml()
    {
        if (_entry is null)
        {
            return;
        }

        XElement replacement;
        try
        {
            replacement = XmlEditing.Deserialize(_xml.Text.Replace("\r\n", "\n"));
        }
        catch (XmlException ex)
        {
            ShowXmlStatus("Invalid XML: " + ex.Message, isError: true);
            return;
        }

        if (replacement.Name != _entry.Name)
        {
            ShowXmlStatus($"Root element must stay <{_entry.Name.LocalName}>", isError: true);
            return;
        }

        _entry.ReplaceWith(replacement);
        _entry = replacement;
        _xmlLoaded = _xml.Text;
        ShowXmlStatus("Applied", isError: false);
        Rebind();
    }
}
