using System.Diagnostics;
using System.Xml.Linq;
using LsrXmlEditor.Controls;
using LsrXmlEditor.Core.Search;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.Services;

namespace LsrXmlEditor.Forms;

public sealed class SearchForm : Form
{
    private const int MaxHits = 5000;
    private const int MinQueryLength = 2;

    private static readonly ListColumn[] Columns =
    [
        new("File", 200), new("Section", 150), new("Entry", 180), new("Field", 240), new("Value", 260)
    ];

    private readonly Workspace _workspace;
    private readonly Action<SearchHit> _open;
    private readonly SearchRunner _runner = new(new SearchSourceCache(), MaxHits);
    private readonly TextBox _query = new() { Width = 360 };
    private readonly CheckBox _matchCase = new() { Text = "Match &case", AutoSize = true };
    private readonly CheckBox _fieldNames = new() { Text = "Search &field names", AutoSize = true, Checked = true };
    private readonly CheckBox _variants = new() { Text = "Include alte&rnate configs", AutoSize = true, Checked = true };
    private readonly Button _search = new() { Text = "&Search", AutoSize = true };
    private readonly Button _stop = new() { Text = "S&top", AutoSize = true, Enabled = false };
    private readonly ItemListView _results = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, Text = "Enter a search term and press Enter" };
    private CancellationTokenSource? _cancellation;

    public SearchForm(Workspace workspace, Action<SearchHit> open)
    {
        _workspace = workspace;
        _open = open;
        Font = UiTheme.Font;
        SuspendLayout();
        Text = "Search all files";
        Icon = StockImages.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1080, 620);
        MinimumSize = new Size(700, 400);
        ShowInTaskbar = false;

        var queryRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0), WrapContents = false };
        queryRow.Controls.AddRange([new Label { Text = "Fi&nd what:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) }, _query, _search, _stop]);

        var optionRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 4, 8, 6) };
        optionRow.Controls.AddRange([_matchCase, _fieldNames, _variants]);

        var resultPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
        resultPanel.Controls.Add(_results);

        var close = new Button { Text = "C&lose", AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.Add(close);

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_status);
        UiTheme.Fit(statusStrip);

        Controls.Add(resultPanel);
        Controls.Add(optionRow);
        Controls.Add(queryRow);
        Controls.Add(buttons);
        Controls.Add(statusStrip);
        AcceptButton = _search;
        CancelButton = close;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout();
        UiTheme.Attach(this);

        _results.SetContent(Columns, []);
        _search.Click += async (_, _) => await SearchAsync();
        _stop.Click += (_, _) => _cancellation?.Cancel();
        close.Click += (_, _) => Close();
        _results.DoubleClick += (_, _) => OpenSelected();
        _results.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelected();
            }
        };
        FormClosed += (_, _) => _cancellation?.Cancel();
    }

    private void OpenSelected()
    {
        if (_results.SelectedRow?.Tag is SearchHit hit)
        {
            _open(hit);
        }
    }

    private async Task SearchAsync()
    {
        var text = _query.Text.Trim();
        if (text.Length < MinQueryLength)
        {
            _status.Text = "Please enter at least two characters";
            return;
        }

        _cancellation?.Cancel();
        var cancellation = _cancellation = new CancellationTokenSource();
        var query = new SearchQuery(text, _matchCase.Checked, _fieldNames.Checked);
        var sources = _workspace.Files
            .Where(f => _variants.Checked || !f.File.IsVariant)
            .Select(f => new SearchSource(f.FullPath, f.File.RelativePath, f.File.FileName, EditedRoot(f.FullPath)))
            .ToList();
        var watch = Stopwatch.StartNew();
        var progress = new Progress<string>(name => _status.Text = $"Searching {name} …");

        _search.Enabled = false;
        _stop.Enabled = true;
        UseWaitCursor = true;
        try
        {
            var (hits, skipped) = await Task.Run(() => _runner.Run(sources, query, progress, cancellation.Token), cancellation.Token);

            _results.SetContent(Columns, hits.Select(h => new ListRow(h,
            [
                h.RelativeFile, DisplayNames.Humanize(h.CategoryName), h.EntryTitle, h.FieldPath, h.Value
            ])).ToList());

            var fileCount = hits.Select(h => h.FilePath).Distinct().Count();
            var skippedText = skipped.Count == 0 ? string.Empty : $" · {skipped.Count} unreadable files skipped ({string.Join(", ", skipped)})";
            _status.Text = hits.Count >= MaxHits
                ? $"More than {Format.Number(MaxHits)} matches – please refine the search ({watch.ElapsedMilliseconds} ms){skippedText}"
                : $"{Format.Number(hits.Count)} matches in {fileCount} files ({watch.ElapsedMilliseconds} ms) – double-click jumps to the field{skippedText}";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Search cancelled";
        }
        finally
        {
            _search.Enabled = true;
            _stop.Enabled = false;
            UseWaitCursor = false;
        }
    }

    private XElement? EditedRoot(string path) =>
        _workspace.Store.Find(path) is { IsDirty: true } document ? new XDocument(document.Document).Root : null;
}
