using LsrXmlEditor.Controls;
using LsrXmlEditor.Core.Backups;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.Services;

namespace LsrXmlEditor.Forms;

public sealed class BackupsForm : Form
{
    private static readonly ListColumn[] Columns =
    [
        new("Time", 150), new("File", 380), new("Size", 80, HorizontalAlignment.Right)
    ];

    private readonly Workspace _workspace;
    private readonly ItemListView _list = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _filter = new() { Width = 260 };

    public BackupsForm(Workspace workspace)
    {
        _workspace = workspace;
        Font = UiTheme.Font;
        SuspendLayout();
        Text = "Backups";
        Icon = StockImages.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 520);
        MinimumSize = new Size(560, 360);
        ShowInTaskbar = false;
        MinimizeBox = false;

        var info = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            UseMnemonic = false,
            Padding = new Padding(10, 8, 10, 6),
            Text = $"A copy is created automatically before every save.\nLocation: {workspace.Backups!.BackupRoot}"
        };

        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        filterRow.Controls.AddRange([new Label { Text = "&Filter:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) }, _filter]);

        var restore = new Button { Text = "&Restore", AutoSize = true };
        var reveal = new Button { Text = "Show in &Explorer", AutoSize = true };
        var close = new Button { Text = "&Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange([close, reveal, restore]);

        var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        listPanel.Controls.Add(_list);

        Controls.Add(listPanel);
        Controls.Add(filterRow);
        Controls.Add(info);
        Controls.Add(buttons);
        AcceptButton = restore;
        CancelButton = close;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout();
        UiTheme.Attach(this);

        restore.Click += (_, _) => Restore();
        reveal.Click += (_, _) => Dialogs.Reveal((_list.SelectedRow?.Tag as BackupInfo)?.BackupPath ?? _workspace.Backups!.BackupRoot);
        _filter.TextChanged += (_, _) => _list.ApplyFilter(_filter.Text);
        _list.DoubleClick += (_, _) => Restore();
        Fill();
    }

    public bool RestoredAny { get; private set; }

    private void Fill()
    {
        var rows = _workspace.Backups!.List()
            .Select(b => new ListRow(b, [Format.TimeWithSeconds(b.Timestamp), b.RelativePath, Format.Size(b.SizeBytes)]))
            .ToList();
        _list.SetContent(Columns, rows);
        _list.ApplyFilter(_filter.Text);
    }

    private void Restore()
    {
        if (_list.SelectedRow?.Tag is not BackupInfo backup)
        {
            return;
        }

        if (_workspace.IsDirty(Path.Combine(_workspace.Folder, backup.RelativePath)))
        {
            Dialogs.Info(this, $"{backup.RelativePath} has unsaved changes. Save or reload it first.");
            return;
        }

        if (!Dialogs.Confirm(this, $"Restore {backup.RelativePath} to the version from {Format.TimeWithSeconds(backup.Timestamp)}?\n\nThe current version is backed up first."))
        {
            return;
        }

        _workspace.Backups!.Restore(backup, _workspace.Folder);
        _workspace.Discard(Path.Combine(_workspace.Folder, backup.RelativePath));
        RestoredAny = true;
        Fill();
        Dialogs.Info(this, $"{backup.RelativePath} has been restored.");
    }
}
