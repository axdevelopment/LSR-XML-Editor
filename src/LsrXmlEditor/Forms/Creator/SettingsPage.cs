using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Xml;
using LsrXmlEditor.PropertyModel;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class SettingsPage : CreatorPage
{
    private readonly CreatorEngine _engine;
    private readonly PropertyGrid _grid = new()
    {
        Dock = DockStyle.Fill,
        PropertySort = PropertySort.Categorized,
        ToolbarVisible = true,
        HelpVisible = true,
        LineColor = SystemColors.ControlLight,
        Margin = Padding.Empty
    };

    private readonly ComboBox _target = Combo();
    private readonly Dictionary<EntityKind, FieldKindIndex> _kinds = [];
    private CreatorDraft? _draft;

    public SettingsPage(CreatorEngine engine)
        : base(CreatorStep.Settings, "All settings", "Every value of the new entry – spawn limits, payments, reputation, map blip, opening hours and more.")
    {
        _engine = engine;
        AutoSize = false;
        Dock = DockStyle.Fill;
        RowCount = 3;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var hint = new Label
        {
            Text = "These start as a copy of the template. Changes here are kept, even if you go back and rename things.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 6)
        };
        Controls.Add(hint, 0, 0);
        SetColumnSpan(hint, 2);
        Controls.Add(new Label { Text = "&Edit:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) }, 0, 1);
        _target.Width = LogicalToDeviceUnits(FieldWidth);
        Controls.Add(_target, 1, 1);
        Controls.Add(_grid, 0, 2);
        SetColumnSpan(_grid, 2);
        _target.SelectedIndexChanged += (_, _) => ShowTarget();
    }

    public override bool FillsHeight => true;

    protected override void Bind(CreatorDraft draft)
    {
        _draft = draft;
        draft.Main.Apply();
        draft.Den?.Apply();
        _target.Items.Clear();
        _target.Items.Add(new Choice<EntryDraft>(draft.Main, $"New {draft.Kind.Title}"));
        if (draft.Mode == CreatorMode.Gang && draft.DenEnabled && draft.Den is { } den)
        {
            _target.Items.Add(new Choice<EntryDraft>(den, "New gang den"));
        }

        _target.Visible = _target.Items.Count > 1;
        GetControlFromPosition(0, 1)!.Visible = _target.Visible;
        _target.SelectedIndex = 0;
    }

    private void ShowTarget()
    {
        if (_draft is null || _target.SelectedItem is not Choice<EntryDraft> { Value: var target })
        {
            return;
        }

        if (!_kinds.TryGetValue(target.Kind, out var kinds))
        {
            kinds = FieldKindIndex.Build(_engine.Entries(target.Kind));
            _kinds[target.Kind] = kinds;
        }

        var draft = _draft;
        var context = new EditContext([target.Entry], kinds, changed => OnFieldsChanged(draft, target, changed), () => BeginInvoke(_grid.Refresh));
        _grid.SelectedObject = new XmlNodeObject(target.Entry, context);
    }

    private void OnFieldsChanged(CreatorDraft draft, EntryDraft target, IReadOnlyList<XElement> changed)
    {
        if (target == draft.Main)
        {
            foreach (var element in changed.Where(e => e.Parent == target.Entry))
            {
                draft.MarkManual(element.Name.LocalName);
            }
        }

        OnChanged();
    }
}
