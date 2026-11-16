using LsrXmlEditor.Controls;
using LsrXmlEditor.Core.Changes;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.Services;

namespace LsrXmlEditor.Forms;

public sealed class ChangesForm : Form
{
    private const int SetListWidth = 440;

    private static readonly ListColumn[] SetColumns =
    [
        new("Saved", 130), new("File", 200), new("Changes", 80, HorizontalAlignment.Right)
    ];

    private static readonly ListColumn[] OperationColumns =
    [
        new("Type", 80), new("Entry", 170), new("Field", 180), new("Old", 110), new("New", 110), new("Result", 160)
    ];

    private readonly Workspace _workspace;
    private readonly ItemListView _sets = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly ItemListView _operations = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly Label _info = new() { Dock = DockStyle.Top, AutoSize = false, UseMnemonic = false, Height = 48, Padding = new Padding(10, 8, 10, 0) };

    public ChangesForm(Workspace workspace)
    {
        _workspace = workspace;
        Font = UiTheme.Font;
        SuspendLayout();
        Text = "Change log";
        Icon = StockImages.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 600);
        MinimumSize = new Size(800, 400);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5 };
        split.Panel1.Padding = new Padding(8, 0, 0, 0);
        split.Panel2.Padding = new Padding(0, 0, 8, 0);
        split.Panel1.Controls.Add(_sets);
        split.Panel2.Controls.Add(_operations);

        var applyAll = new Button { Text = "Reapply &all", AutoSize = true };
        var applySelected = new Button { Text = "Reapply &selected", AutoSize = true };
        var remove = new Button { Text = "&Remove log entry", AutoSize = true };
        var close = new Button { Text = "&Close", AutoSize = true, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        actions.Controls.AddRange([applyAll, applySelected, remove]);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(actions, 0, 0);
        bottom.Controls.Add(close, 1, 0);

        Controls.Add(split);
        Controls.Add(_info);
        Controls.Add(bottom);
        CancelButton = close;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout();
        UiTheme.Attach(this);

        Load += (_, _) => split.SetDistance(LogicalToDeviceUnits(SetListWidth));
        _sets.SelectedIndexChanged += (_, _) => ShowOperations((_sets.SelectedRow?.Tag as ChangeSet)?.Operations.Select(o => (o, (ReplayStatus?)null)));
        applyAll.Click += async (_, _) => await ReplayAsync(_workspace.ChangeLog!.Load());
        applySelected.Click += async (_, _) =>
        {
            if (_sets.SelectedRow?.Tag is ChangeSet set)
            {
                await ReplayAsync([set]);
            }
        };
        remove.Click += (_, _) => Remove();
        Fill();
    }

    public bool AppliedAny { get; private set; }

    private void Fill()
    {
        var sets = _workspace.ChangeLog!.Load().OrderByDescending(s => s.Timestamp).ToList();
        _sets.SetContent(SetColumns, sets.Select(s => new ListRow(s,
            [Format.TimeWithSeconds(s.Timestamp), s.RelativeFile, Format.Number(s.Operations.Count)])).ToList());
        _info.Text = sets.Count == 0
            ? "No changes saved yet. Every save is logged here and can be reapplied after an LSR update."
            : $"{sets.Count} saves with {Format.Number(sets.Sum(s => s.Operations.Count))} changes. "
              + "After an LSR update, choose \"Reapply all\" and save afterwards.";
        _sets.SelectIndex(0);
    }

    private void ShowOperations(IEnumerable<(ChangeOperation Operation, ReplayStatus? Status)>? results)
    {
        _operations.SetContent(OperationColumns, (results ?? []).Select(r => new ListRow(r.Operation,
        [
            KindLabel(r.Operation.Kind),
            r.Operation.EntryKey,
            r.Operation.FieldPath ?? string.Empty,
            r.Operation.OldValue ?? string.Empty,
            r.Operation.NewValue ?? string.Empty,
            StatusLabel(r.Status)
        ])).ToList());
    }

    private async Task ReplayAsync(IReadOnlyList<ChangeSet> sets)
    {
        var results = new List<ReplayResult>();
        foreach (var group in sets.OrderBy(s => s.Timestamp).GroupBy(s => s.RelativeFile, StringComparer.OrdinalIgnoreCase))
        {
            var path = Path.Combine(_workspace.Folder, group.Key);
            var operations = group.SelectMany(s => s.Operations).ToList();
            if (!File.Exists(path))
            {
                results.AddRange(operations.Select(o => new ReplayResult(o, ReplayStatus.NotFound)));
                continue;
            }

            var document = await _workspace.LoadAsync(path);
            results.AddRange(ChangeReplayer.Apply(document, operations));
        }

        var applied = results.Count(r => r.Status is ReplayStatus.Applied or ReplayStatus.AppliedOverDifferentValue);
        AppliedAny |= applied > 0;
        ShowOperations(results.Select(r => (r.Operation, (ReplayStatus?)r.Status)));
        _info.Text = $"{applied} applied · {results.Count(r => r.Status == ReplayStatus.AlreadyApplied)} already present · "
            + $"{results.Count(r => r.Status == ReplayStatus.NotFound)} not found."
            + (applied > 0 ? " The modified files are now unsaved – save them in the main window." : string.Empty);
    }

    private void Remove()
    {
        if (_sets.SelectedRow?.Tag is not ChangeSet set
            || !Dialogs.Confirm(this, $"Remove the log entry from {Format.TimeWithSeconds(set.Timestamp)} ({set.RelativeFile})?\nThe file itself is not changed."))
        {
            return;
        }

        _workspace.ChangeLog!.Remove([set.Id]);
        Fill();
    }

    private static string KindLabel(ChangeKind kind) => kind switch
    {
        ChangeKind.FieldChanged => "Field",
        ChangeKind.EntryAdded => "Added",
        ChangeKind.EntryRemoved => "Deleted",
        _ => "Replaced"
    };

    private static string StatusLabel(ReplayStatus? status) => status switch
    {
        ReplayStatus.Applied => "Applied",
        ReplayStatus.AlreadyApplied => "Already present",
        ReplayStatus.AppliedOverDifferentValue => "Applied (value differed)",
        ReplayStatus.NotFound => "Not found",
        _ => string.Empty
    };
}
