using System.Runtime;
using System.Xml.Linq;
using LsrXmlEditor.Controls;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Files;
using LsrXmlEditor.Core.Storage;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Workspace;
using LsrXmlEditor.Core.Xml;
using LsrXmlEditor.Forms.Creator;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.PropertyModel;
using LsrXmlEditor.Services;

namespace LsrXmlEditor.Forms;

public sealed record EntryTag(XElement Element, int Index);

public sealed record MainFormState(Rectangle Bounds, FormWindowState WindowState, string? FilePath, string? CategoryPath, int EntryIndex);

public sealed class MainForm : Form
{
    private const int FilterWidth = 200;
    private const int MinFilterWidth = 60;
    private const string ModItemsFile = "ModItems";
    private const string ModItemReference = "ModItemName";
    private const string ModItemName = "Name";
    private const int TreeWidth = 320;
    private const int ListWidth = 430;
    private const string DeleteKeyText = "Del";
    private const string UndoKeyText = "Ctrl+Z";
    private const string RedoKeyText = "Ctrl+Y";
    private const string UndoneText = "Undone";
    private const string RedoneText = "Redone";

    private static readonly KeysConverter KeyNames = new();

    private static readonly string[] PreviewTitleFields = ["Name", "PlayerName", "ModelName", "ModelItemID"];

    private static readonly ListColumn[] GroupColumns =
    [
        new("Section", 190), new("Files", 70, HorizontalAlignment.Right), new("Entries", 80, HorizontalAlignment.Right),
        new("Variants", 80, HorizontalAlignment.Right)
    ];

    private static readonly ListColumn[] FileColumns =
    [
        new("Name", 170), new("Entries", 65, HorizontalAlignment.Right), new("File", 190), new("Variants", 70, HorizontalAlignment.Right),
        new("Size", 70, HorizontalAlignment.Right), new("Modified", 115), new("Description", 320)
    ];

    private static readonly ListColumn[] VariantColumns =
    [
        new("Variant", 220), new("Type", 90), new("Folder", 260), new("Entries", 70, HorizontalAlignment.Right),
        new("Size", 70, HorizontalAlignment.Right), new("Modified", 115)
    ];

    private static readonly ListColumn[] CategoryColumns = [new("Category", 260), new("Entries", 80, HorizontalAlignment.Right)];

    private static readonly ListColumn[] EntryColumns = [new("Name", 220), new("Description", 260)];

    private readonly Workspace _workspace;
    private readonly PedViewerHost _pedViewer = new();
    private readonly SplitContainer _outerSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5 };
    private readonly SplitContainer _innerSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5 };
    private readonly SplitContainer _editorSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 5, Panel2Collapsed = true };
    private readonly Panel _viewerContent = new() { Dock = DockStyle.Fill };
    private Control? _viewerPanel;
    private XElement? _dockedEntry;
    private readonly string? _startFolder;
    private readonly MainFormState? _restore;
    private readonly Font _headerFont;
    private readonly NavigatorTree _tree = new() { Dock = DockStyle.Fill };
    private readonly ItemListView _list = new() { Dock = DockStyle.Fill };
    private readonly EntryEditor _editor = new() { Dock = DockStyle.Fill };
    private readonly ToolStripTextBox _filter = new() { AutoSize = false, ToolTipText = "Filter list (Ctrl+F)" };
    private readonly ToolStripLabel _listInfo = new() { Alignment = ToolStripItemAlignment.Right, ForeColor = SystemColors.GrayText };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _summary = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripMenuItem _includeSubfolders = new("&Include subfolders (AlternateConfigs)") { CheckOnClick = true };
    private ToolStripMenuItem _undoMenu = null!;
    private ToolStripMenuItem _redoMenu = null!;
    private ToolStripButton _undoButton = null!;
    private ToolStripButton _redoButton = null!;
    private readonly ToolStripButton _viewerToggle = new("3D View")
    {
        CheckOnClick = true,
        DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
        ToolTipText = "Show peds, vehicles, weapons, items and animations in 3D (Ctrl+3)"
    };
    private readonly ToolStripMenuItem _recentMenu = new("Recent &folders");
    private readonly Dictionary<(string File, string Category), FieldKindIndex> _kinds = [];
    private XmlConfigDocument? _document;
    private EntryCategory? _category;
    private Task _navigation = Task.CompletedTask;
    private SearchForm? _searchForm;
    private bool _closeConfirmed;
    private bool _rebuilding;
    private readonly ModItemNames _itemNames;
    private bool _creatorOpen;

    public MainForm(Workspace workspace, string? startFolder, MainFormState? restore = null)
    {
        _workspace = workspace;
        _startFolder = startFolder;
        _restore = restore;
        _itemNames = new ModItemNames(workspace);
        _editor.Suggestions = _itemNames;
        Font = UiTheme.Font;
        _headerFont = new Font(Font, FontStyle.Bold);
        SuspendLayout();
        Text = Dialogs.AppTitle;
        Icon = StockImages.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1400, 860);
        MinimumSize = new Size(960, 600);
        KeyPreview = true;
        _filter.Width = LogicalToDeviceUnits(FilterWidth);

        BuildLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        if (restore is not null)
        {
            StartPosition = FormStartPosition.Manual;
            Location = restore.Bounds.Location;
        }

        ResumeLayout();
        Wire();
        UiTheme.Attach(this);
    }

    public event EventHandler? AppearanceChanged;

    public MainFormState CaptureState()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        var entry = SelectedEntryTags().FirstOrDefault()?.Index ?? -1;
        return _tree.SelectedTarget switch
        {
            CategoryTarget category => new(bounds, WindowState, category.File.FullPath, category.CategoryPath, entry),
            FileTarget file => new(bounds, WindowState, file.File.FullPath, null, entry),
            _ => new(bounds, WindowState, null, null, -1)
        };
    }

    public void CloseForRebuild()
    {
        _closeConfirmed = true;
        _rebuilding = true;
        Close();
    }

    public NavigatorTree Tree => _tree;

    public ItemListView List => _list;

    public EntryEditor Editor => _editor;

    public Task Navigation => _navigation;

    public string StatusText => _status.Text ?? string.Empty;

    public async Task OpenFolderAsync(string folder)
    {
        UseWaitCursor = true;
        SetStatus("Reading folder …");
        try
        {
            _kinds.Clear();
            _document = null;
            _category = null;
            await _workspace.OpenAsync(folder);
            ShowWorkspace();
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void ShowWorkspace()
    {
        _itemNames.Warm();
        _tree.Populate(_workspace);
        UpdateRecentMenu();
        UpdateSummary();
        UpdateTitle();
        SetStatus("Ready");
    }

    public async Task NavigateAsync(string filePath, string? categoryPath, int entryIndex, string? fieldPath, string? entryTitle = null)
    {
        var selected = categoryPath is not null && _tree.Select(t => t is CategoryTarget c
                && string.Equals(c.File.FullPath, filePath, StringComparison.OrdinalIgnoreCase) && c.CategoryPath == categoryPath)
            || _tree.Select(t => t is FileTarget f && string.Equals(f.File.FullPath, filePath, StringComparison.OrdinalIgnoreCase));

        if (!selected)
        {
            return;
        }

        await _navigation;
        if (entryIndex < 0)
        {
            return;
        }

        _filter.Text = string.Empty;
        var found = entryTitle is null
            ? _list.SelectWhere(t => t is EntryTag e && e.Index == entryIndex)
            : _list.SelectWhere(t => t is EntryTag e && e.Index == entryIndex && EntryIdentity.TitleOf(e.Element) == entryTitle);
        if (!found && entryTitle is not null)
        {
            _list.SelectClosest(t => t is EntryTag e && EntryIdentity.TitleOf(e.Element) == entryTitle ? e.Index : null, entryIndex);
            _list.Focus();
        }

        _list.FlushSelection();
        if (!string.IsNullOrEmpty(fieldPath))
        {
            _editor.RevealField(fieldPath);
        }
    }

    public async Task SaveCurrentAsync()
    {
        _editor.CommitEdit();
        if (_document is not null)
        {
            await SaveAsync(_document);
        }
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_restore is { } restore)
        {
            if (!_workspace.IsOpen)
            {
                return;
            }

            ShowWorkspace();
            if (restore.FilePath is { } path)
            {
                await RunAsync(() => NavigateAsync(path, restore.CategoryPath, restore.EntryIndex, null));
            }

            return;
        }

        var folder = new[] { _startFolder, _workspace.Settings.Folder, AppSettings.DefaultFolder }
                .Select(ConfigFolder.Resolve)
                .FirstOrDefault(f => f is not null)
            ?? ConfigFolder.FromGameFolders(GameInstall.Folders())
            ?? PickConfigFolder(string.Empty);

        if (folder is not null)
        {
            await RunAsync(() => OpenFolderAsync(folder));
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        _editor.CommitEdit();
        if (_closeConfirmed || !_workspace.HasUnsavedChanges && !_editor.HasPendingXml)
        {
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        var answer = Dialogs.AskSave(this, "There are unsaved changes. Save before exiting?");
        if (answer == DialogResult.Cancel)
        {
            return;
        }

        if (answer == DialogResult.Yes)
        {
            if (!_editor.ApplyPendingXml())
            {
                return;
            }

            await RunAsync(SaveAllAsync);
            if (_workspace.HasUnsavedChanges)
            {
                return;
            }
        }

        _closeConfirmed = true;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Delete && _list.Focused)
        {
            DeleteEntry();
            return true;
        }

        if (keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y) or (Keys.Control | Keys.Shift | Keys.Z) && !IsTextInputFocused())
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                Undo();
            }
            else
            {
                Redo();
            }

            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _workspace.History.Changed -= OnHistoryChanged;
            _workspace.DirtyChanged -= OnDirtyChanged;
            _workspace.DocumentReplaced -= OnDocumentReplaced;
            if (_viewerPanel is not null && !_rebuilding)
            {
                _pedViewer.Release();
            }

            _headerFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void BuildLayout()
    {
        var open = new Command("&Open folder...", () => _ = RunAsync(ChooseFolderAsync), Keys.Control | Keys.O, Glyph.Open);
        var save = new Command("&Save", () => _ = RunAsync(SaveCurrentAsync), Keys.Control | Keys.S, Glyph.Save);
        var saveAll = new Command("Save &all", () => _ = RunAsync(SaveAllAsync), Keys.Control | Keys.Shift | Keys.S, Glyph.SaveAll);
        var reload = new Command("&Reload", () => _ = RunAsync(ReloadAsync), Keys.F5, Glyph.Reload);
        var duplicate = new Command("&Duplicate entry", CloneEntry, Keys.Control | Keys.D, Glyph.Duplicate);
        var delete = new Command("De&lete entry", DeleteEntry, Icon: Glyph.Delete, ShortcutText: DeleteKeyText);
        var search = new Command("&Search all files...", ShowSearch, Keys.Control | Keys.Shift | Keys.F, Glyph.Search);
        var backups = new Command("&Backups...", ShowBackups, Icon: Glyph.Backups);
        var changes = new Command("&Change log...", ShowChanges, Icon: Glyph.ChangeLog);
        Command[] creators =
        [
            new("New &gang...", () => _ = RunAsync(() => OpenCreatorAsync(CreatorMode.Gang))),
            new("New gang &den...", () => _ = RunAsync(() => OpenCreatorAsync(CreatorMode.GangDen))),
            new("New &shop / location...", () => _ = RunAsync(() => OpenCreatorAsync(CreatorMode.Location))),
            new("New &agency...", () => _ = RunAsync(() => OpenCreatorAsync(CreatorMode.Agency)))
        ];
        var createMenu = new ToolStripMenuItem("C&reate", GlyphImage(Glyph.Add));
        createMenu.DropDownItems.AddRange([.. creators.Select(ContextItem)]);
        var createButton = new ToolStripDropDownButton("Create", GlyphImage(Glyph.Add)) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, ToolTipText = "Create a gang, gang den, shop or agency from a template" };
        createButton.DropDownItems.AddRange([.. creators.Select(ContextItem)]);
        var undo = new Command("&Undo", Undo, Icon: Glyph.Undo, ShortcutText: UndoKeyText);
        var redo = new Command("&Redo", Redo, Icon: Glyph.Redo, ShortcutText: RedoKeyText);
        _undoMenu = MenuItem(undo);
        _redoMenu = MenuItem(redo);
        _undoButton = ToolButton(undo, "Undo");
        _redoButton = ToolButton(redo, "Redo");

        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(
        [
            MenuItem(open),
            _recentMenu,
            new ToolStripSeparator(),
            MenuItem(save),
            MenuItem(saveAll),
            new ToolStripSeparator(),
            MenuItem(reload),
            new ToolStripSeparator(),
            MenuItem(new Command("E&xit", Close, Keys.Alt | Keys.F4))
        ]);

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.AddRange(
        [
            _undoMenu,
            _redoMenu,
            new ToolStripSeparator(),
            MenuItem(duplicate),
            MenuItem(delete),
            createMenu,
            new ToolStripSeparator(),
            MenuItem(new Command("&Filter list", FocusFilter, Keys.Control | Keys.F)),
            MenuItem(search)
        ]);

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add(_includeSubfolders);

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.AddRange(
        [
            MenuItem(backups),
            MenuItem(changes),
            MenuItem(new Command("View in &3D", ViewIn3D, Keys.Control | Keys.D3)),
            new ToolStripSeparator(),
            MenuItem(new Command("Open config &folder in Explorer", () => RevealFolder(_workspace.Folder))),
            MenuItem(new Command("Open &data folder (backups, change log)", () => RevealFolder(_workspace.DataRoot))),
            new ToolStripSeparator(),
            MenuItem(new Command("&Options...", ShowOptions))
        ]);

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(MenuItem(new Command("&About", () => Dialogs.Info(this,
            $"LSR XML Editor {Application.ProductVersion}\n\nEditor for the XML configuration files of Los Santos RED.\nA backup is created automatically before every save.\nThe program works completely offline.\n\nNot tested with custom content (add-on vehicles, peds, weapons) yet.\nPlease report bugs as a GitHub issue or on Discord (onlyardo).\n\nmade with ❤️ from ardo"))));

        menu.Items.AddRange([file, edit, view, tools, help]);

        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.AddRange(
        [
            ToolButton(open, "Open"),
            ToolButton(save, "Save"),
            ToolButton(saveAll, "Save all"),
            new ToolStripSeparator(),
            ToolButton(reload, "Reload"),
            new ToolStripSeparator(),
            createButton,
            new ToolStripSeparator(),
            ToolButton(search, "Search"),
            ToolButton(backups, "Backups"),
            ToolButton(changes, "Change log"),
            new ToolStripSeparator(),
            _undoButton,
            _redoButton,
            new ToolStripSeparator(),
            _viewerToggle
        ]);

        var listBar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        listBar.Items.AddRange(
        [
            new ToolStripLabel("Filter:"),
            _filter,
            new ToolStripSeparator(),
            ToolButton(duplicate, "Duplicate"),
            ToolButton(delete, "Delete"),
            _listInfo
        ]);

        listBar.Layout += (_, _) => FitFilter(listBar);

        var listPanel = new Panel { Dock = DockStyle.Fill };
        listPanel.Controls.Add(_list);
        listPanel.Controls.Add(listBar);

        var listMenu = new ContextMenuStrip();
        listMenu.Items.AddRange(
        [
            ContextItem(new Command("&Open", OpenSelectedRow)),
            ContextItem(duplicate with { Text = "D&uplicate" }),
            ContextItem(delete with { Text = "&Delete" }),
            new ToolStripSeparator(),
            ContextItem(new Command("Show in &Explorer", RevealSelected))
        ]);
        _list.ContextMenuStrip = listMenu;

        _innerSplit.Panel1.Controls.Add(listPanel);
        _innerSplit.Panel2.Controls.Add(_editorSplit);
        _editorSplit.Panel1.Controls.Add(_editor);
        _editorSplit.Panel2.Controls.Add(BuildViewerDock());
        _outerSplit.Panel1.Controls.Add(_tree);
        _outerSplit.Panel2.Controls.Add(_innerSplit);

        var statusStrip = new StatusStrip();
        statusStrip.Items.AddRange([_status, _summary]);
        UiTheme.Fit(menu, toolbar, listBar, listMenu, statusStrip);

        Controls.Add(_outerSplit);
        Controls.Add(toolbar);
        Controls.Add(menu);
        Controls.Add(statusStrip);
        MainMenuStrip = menu;

        Load += (_, _) =>
        {
            if (_restore is { } restore)
            {
                Bounds = restore.Bounds;
                WindowState = restore.WindowState == FormWindowState.Minimized ? FormWindowState.Normal : restore.WindowState;
            }

            _outerSplit.SetDistance(LogicalToDeviceUnits(UiTheme.Grow(TreeWidth)));
            _innerSplit.SetDistance(LogicalToDeviceUnits(UiTheme.Grow(ListWidth)));
        };
    }

    private void Wire()
    {
        _includeSubfolders.Checked = _workspace.Settings.IncludeSubfolders;
        _viewerToggle.Image = GlyphImage(Glyph.View3D);
        _workspace.History.Changed += OnHistoryChanged;
        UpdateHistoryCommands();
        _pedViewer.CodeWalkerFolder = _workspace.Settings.CodeWalkerFolder;
        _viewerToggle.Checked = _workspace.Settings.ShowViewer && _pedViewer.IsAvailable && _pedViewer.HasCodeWalker;
        _viewerToggle.CheckedChanged += (_, _) => OnViewerToggled();
        _includeSubfolders.CheckedChanged += async (_, _) => await RunAsync(ToggleSubfoldersAsync);
        _tree.AfterSelect += (_, _) => _navigation = RunAsync(() => ShowTargetAsync(_tree.SelectedTarget));
        _list.SelectionSettled += (_, _) =>
        {
            OnListSelectionChanged();
            DockSelectedEntry();
        };
        _list.DoubleClick += (_, _) => OpenSelectedRow();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelectedRow();
            }
        };
        _editor.Grid.SelectedGridItemChanged += (_, e) => DockGridSelection(e.NewSelection);
        _list.RowsChanged += (_, _) => _listInfo.Text = _list.RowCount == _list.TotalCount
            ? $"{Format.Number(_list.TotalCount)} items"
            : $"{Format.Number(_list.RowCount)} of {Format.Number(_list.TotalCount)}";
        _filter.TextChanged += (_, _) => _list.ApplyFilter(_filter.Text);
        _editor.FieldChanged += (_, element) => OnFieldChanged(element);
        _editor.StructureChanged += (_, _) => OnEntryStructureChanged();
        _editor.MultiFieldChanged += (_, elements) => OnMultiFieldChanged(elements);
        _workspace.DirtyChanged += OnDirtyChanged;
        _workspace.DocumentReplaced += OnDocumentReplaced;
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => BeginInvoke(() =>
    {
        _workspace.History.Close();
        UpdateHistoryCommands();
    });

    private void OnDirtyChanged(object? sender, EventArgs e) => BeginInvoke(() =>
    {
        _tree.RefreshDirtyMarkers(_workspace);
        UpdateTitle();
    });

    private void OnDocumentReplaced(object? sender, string path) => BeginInvoke(() =>
    {
        if (string.Equals(_document?.FilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            _navigation = RunAsync(() => ShowTargetAsync(_tree.SelectedTarget));
        }
    });

    private async Task ShowTargetAsync(NavTarget? target)
    {
        _filter.Text = string.Empty;
        _document = null;
        _category = null;

        switch (target)
        {
            case RootTarget:
                ShowGroups();
                break;
            case GroupTarget group:
                ShowFiles(group.Group);
                break;
            case VariantsTarget variants:
                ShowVariants(variants);
                break;
            case FileTarget file when (file.File.Stats?.Categories.Count ?? 0) > 1:
                ShowCategories(file.File);
                break;
            case FileTarget file:
                await ShowEntriesAsync(file.File, null, target);
                break;
            case CategoryTarget category:
                await ShowEntriesAsync(category.File, category.CategoryPath, target);
                break;
            default:
                _list.SetContent([], []);
                _editor.Clear();
                break;
        }
    }

    private void ShowGroups()
    {
        var rows = KnownConfigs.Groups
            .Select(group => (Group: group, Files: FilesOf(group).ToList()))
            .Where(g => g.Files.Count > 0)
            .Select(g => new ListRow(new GroupTarget(g.Group),
            [
                g.Group,
                Format.Number(g.Files.Count(f => !f.File.IsVariant)),
                Format.Number(g.Files.Where(f => !f.File.IsVariant).Sum(f => f.EntryCount)),
                Format.Number(g.Files.Count(f => f.File.IsVariant))
            ]))
            .ToList();

        _list.SetContent(GroupColumns, rows);
        var files = _workspace.Files;
        _editor.ShowMessage(Path.GetFileName(_workspace.Folder.TrimEnd('\\')),
            $"{files.Count(f => !f.File.IsVariant)} config files · {files.Count(f => f.File.IsVariant)} alternate configs · "
            + $"{Format.Number(files.Where(f => !f.File.IsVariant).Sum(f => f.EntryCount))} entries · {Format.Size(files.Sum(f => f.File.SizeBytes))}");
    }

    private void ShowFiles(string group)
    {
        var files = FilesOf(group).ToList();
        var rows = files
            .Where(f => !f.File.IsVariant)
            .OrderBy(f => KnownConfigs.Order(f.File.BaseName))
            .Select(f => new ListRow(new FileTarget(f),
            [
                f.Info.Title,
                Format.Number(f.EntryCount),
                f.File.FileName,
                Format.Number(files.Count(v => v.File.IsVariant && v.File.BaseName.Equals(f.File.BaseName, StringComparison.OrdinalIgnoreCase))),
                Format.Size(f.File.SizeBytes),
                Format.Time(f.File.LastWriteTime),
                f.Info.Description
            ]))
            .ToList();

        _list.SetContent(FileColumns, rows);
        _editor.ShowMessage(group, $"{rows.Count} files – double-click to open a file");
    }

    private void ShowVariants(VariantsTarget target)
    {
        var rows = target.Variants
            .Select(v => new ListRow(new FileTarget(v),
            [
                v.File.VariantSuffix ?? v.File.FileName,
                v.File.Kind == LsrFileKind.AdditiveVariant ? "Additive" : "Replacement",
                Path.GetDirectoryName(v.File.RelativePath) is { Length: > 0 } dir ? dir : "Main folder",
                Format.Number(v.EntryCount),
                Format.Size(v.File.SizeBytes),
                Format.Time(v.File.LastWriteTime)
            ]))
            .ToList();

        _list.SetContent(VariantColumns, rows);
        _editor.ShowMessage($"Alternate configs for {target.BaseFile.File.FileName}",
            "\"+_\" files extend the base file, \"_\" files replace it. LSR only loads files from the main folder.");
    }

    private void ShowCategories(ConfigFile file)
    {
        var rows = (file.Stats?.Categories ?? [])
            .Select(c => new ListRow(new CategoryTarget(file, c.Path, c.Name), [DisplayNames.Humanize(c.Name), Format.Number(c.Count)]))
            .ToList();

        _list.SetContent(CategoryColumns, rows);
        _editor.ShowMessage(FileTitle(file), file.Info.Description);
    }

    private async Task ShowEntriesAsync(ConfigFile file, string? categoryPath, NavTarget target)
    {
        SetStatus($"Loading {file.File.FileName} …");
        UseWaitCursor = true;
        try
        {
            var document = await _workspace.LoadAsync(file.FullPath);
            if (!ReferenceEquals(_tree.SelectedTarget, target))
            {
                return;
            }

            var category = categoryPath is null ? document.Categories.FirstOrDefault() : document.FindCategory(categoryPath);
            _document = document;
            _category = category;
            FillEntries(0);
            SetStatus($"{file.File.RelativePath} loaded");
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void FillEntries(int selectIndex)
    {
        if (_document is null || _category is null)
        {
            _list.SetContent(EntryColumns, []);
            _editor.ShowMessage("No entries", "This file has no editable entries.");
            return;
        }

        var category = _category;
        var rows = category.Entries.Select((e, i) => new ListRow(new EntryTag(e, i), EntryCells(e, category))).ToList();
        _list.SetContent(EntryColumns, rows);
        if (rows.Count == 0)
        {
            _editor.ShowMessage(DisplayNames.Humanize(category.Name), "No entries");
            return;
        }

        _list.SelectClosest(t => t is EntryTag e ? e.Index : null, selectIndex);
    }

    private static string[] EntryCells(XElement entry, EntryCategory category)
    {
        if (category.IsRecordSet)
        {
            var count = entry.Elements().Count();
            return [DisplayNames.Humanize(entry.Name.LocalName), count == 0 ? entry.Value : $"{count} settings"];
        }

        var title = EntryIdentity.TitleOf(entry);
        var subtitle = EntryIdentity.SubtitleOf(entry, title);
        return [title, subtitle == entry.Name.LocalName ? string.Empty : subtitle];
    }

    private IReadOnlyList<EntryTag> SelectedEntryTags() =>
        _list.SelectedRows.Select(r => r.Tag).OfType<EntryTag>().OrderBy(t => t.Index).ToList();

    private void OnListSelectionChanged()
    {
        if (_document is null || _category is null)
        {
            return;
        }

        var tags = SelectedEntryTags();
        if (tags.Count == 0)
        {
            return;
        }

        if (tags.Count == 1)
        {
            var cells = EntryCells(tags[0].Element, _category);
            _editor.ShowEntry(tags[0].Element, KindsFor(_document, _category), cells[0], cells[1]);
            return;
        }

        _editor.ShowEntries(
            tags.Select(t => t.Element).ToList(),
            KindsFor(_document, _category),
            $"{Format.Number(tags.Count)} entries selected",
            $"{DisplayNames.Humanize(_category.Name)} · changes apply to all selected entries · differing values are shown empty");
    }

    private void OnMultiFieldChanged(IReadOnlyList<XElement> elements)
    {
        if (_category is not null)
        {
            var category = _category;
            foreach (var tag in SelectedEntryTags())
            {
                _list.UpdateRow(tag, EntryCells(tag.Element, category));
            }
        }

        if (elements.Count > 0)
        {
            var name = DisplayNames.Humanize(elements[0].Name.LocalName);
            SetStatus(elements.Count == 1
                ? $"{name} = {elements[0].Value} in 1 entry"
                : $"{name} = {elements[0].Value} in {Format.Number(elements.Count)} entries");
        }
    }

    private FieldKindIndex KindsFor(XmlConfigDocument document, EntryCategory category)
    {
        var key = (document.FilePath, category.Path);
        if (!_kinds.TryGetValue(key, out var kinds))
        {
            kinds = FieldKindIndex.Build(category.Entries);
            _kinds[key] = kinds;
        }

        return kinds;
    }

    private void OpenSelectedRow()
    {
        switch (_list.SelectedRow?.Tag)
        {
            case GroupTarget group:
                _tree.Select(t => t == group);
                break;
            case FileTarget file:
                _tree.Select(t => t is FileTarget f && f.File == file.File);
                break;
            case CategoryTarget category:
                _tree.Select(t => t == category);
                break;
        }
    }

    private void OnFieldChanged(XElement element)
    {
        if (_list.SelectedRow?.Tag is EntryTag tag && _category is not null)
        {
            var cells = EntryCells(tag.Element, _category);
            _list.UpdateRow(tag, cells);
            _editor.UpdateHeader(cells[0], cells[1]);
        }

        var name = DisplayNames.Humanize(element.Name.LocalName);
        SetStatus($"{name} = {element.Value}");
    }

    private void OnEntryStructureChanged()
    {
        if (_list.SelectedRow?.Tag is EntryTag tag && _editor.Entry is { } entry && !ReferenceEquals(tag.Element, entry))
        {
            RefreshCategory();
            FillEntries(tag.Index);
        }

        SetStatus("Structure changed");
    }

    private void CloneEntry()
    {
        var tags = SelectedEntryTags();
        if (tags.Count == 0 || _category is null)
        {
            return;
        }

        if (_category.IsRecordSet)
        {
            Dialogs.Info(this, "Settings sections cannot be duplicated.");
            return;
        }

        foreach (var tag in tags)
        {
            XmlEditing.CloneAfter(tag.Element);
        }

        RefreshCategory();
        FillEntries(tags[0].Index + 1);
        SetStatus(tags.Count == 1
            ? "Entry duplicated – change its ID/name so LSR can tell them apart"
            : $"{Format.Number(tags.Count)} entries duplicated – change their IDs/names so LSR can tell them apart");
    }

    private void DeleteEntry()
    {
        var tags = SelectedEntryTags();
        if (tags.Count == 0 || _category is null)
        {
            return;
        }

        if (_category.IsRecordSet)
        {
            Dialogs.Info(this, "Settings sections cannot be deleted.");
            return;
        }

        var title = EntryIdentity.TitleOf(tags[0].Element);
        var question = tags.Count == 1 ? $"Delete entry \"{title}\"?" : $"Delete {Format.Number(tags.Count)} selected entries?";
        if (!Dialogs.Confirm(this, question))
        {
            return;
        }

        foreach (var tag in tags)
        {
            XmlEditing.Remove(tag.Element);
        }

        RefreshCategory();
        FillEntries(tags[0].Index);
        SetStatus(tags.Count == 1 ? $"\"{title}\" deleted" : $"{Format.Number(tags.Count)} entries deleted");
    }

    private async Task OpenCreatorAsync(CreatorMode mode)
    {
        if (!_workspace.IsOpen || _creatorOpen)
        {
            return;
        }

        _creatorOpen = true;
        try
        {
            await RunCreatorAsync(mode);
        }
        finally
        {
            _creatorOpen = false;
        }
    }

    private async Task RunCreatorAsync(CreatorMode mode)
    {
        _editor.CommitEdit();
        UseWaitCursor = true;
        CreatorSession session;
        try
        {
            session = await CreatorSession.LoadAsync(_workspace);
        }
        finally
        {
            UseWaitCursor = false;
        }

        _workspace.History.Close();
        var revision = _workspace.History.Revision;
        using var form = new CreatorForm(mode, session.Engine, session.Data);
        var result = form.ShowDialog(this);
        if (result == DialogResult.Abort)
        {
            var reverted = _workspace.History.RollBack(revision);
            if (_document is not null && reverted.Contains(_document.Document))
            {
                RefreshCategory();
                FillEntries(SelectedEntryTags().FirstOrDefault()?.Index ?? 0);
            }

            UpdateHistoryCommands();
            Dialogs.Error(this, $"The new entry could not be created, nothing was changed.\n\n{form.Failure?.Message}");
            return;
        }

        if (result != DialogResult.OK || form.Result is not { } created)
        {
            return;
        }

        _workspace.History.Close();
        UpdateHistoryCommands();
        if (session.Locate(created) is { } location)
        {
            await NavigateAsync(location.FilePath, location.CategoryPath, location.Index, null);
        }

        SetStatus($"{form.Text.Replace("New ", "Created ", StringComparison.Ordinal)} – save to keep it (Ctrl+Shift+S), undo with Ctrl+Z");
    }

    private void Undo() => ApplyHistory(_workspace.History.Undo, UndoneText);

    private void Redo() => ApplyHistory(_workspace.History.Redo, RedoneText);

    private void ApplyHistory(Func<IReadOnlyCollection<XDocument>> step, string verb)
    {
        var target = verb == UndoneText ? _workspace.History.UndoTarget : _workspace.History.RedoTarget;
        var changed = step();
        if (changed.Count == 0)
        {
            return;
        }

        if (_document is not null && changed.Contains(_document.Document))
        {
            var index = SelectedEntryTags().FirstOrDefault()?.Index ?? 0;
            RefreshCategory();
            FillEntries(index);
            OnListSelectionChanged();
        }

        UpdateHistoryCommands();
        SetStatus($"{verb}: change in {target}");
    }

    private void UpdateHistoryCommands()
    {
        var history = _workspace.History;
        _undoButton.Enabled = _undoMenu.Enabled = history.CanUndo;
        _redoButton.Enabled = _redoMenu.Enabled = history.CanRedo;
        _undoButton.ToolTipText = history.UndoTarget is { } undo ? $"Undo change in {undo} ({UndoKeyText})" : $"Undo ({UndoKeyText})";
        _redoButton.ToolTipText = history.RedoTarget is { } redo ? $"Redo change in {redo} ({RedoKeyText})" : $"Redo ({RedoKeyText})";
    }

    private static bool IsTextInputFocused() => FromHandle(GetFocus()) is TextBoxBase;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    private void RefreshCategory()
    {
        if (_document is not null && _category is not null)
        {
            _category = _document.FindCategory(_category.Path);
        }
    }

    private async Task SaveAsync(XmlConfigDocument document)
    {
        var name = Path.GetFileName(document.FilePath);
        SaveResult? result;
        try
        {
            result = await SaveWithWaitCursorAsync(document, overwriteExternalChanges: false);
        }
        catch (ExternalChangeException)
        {
            if (!Dialogs.Confirm(this, $"{name} was changed by another program after it was opened.\n\nOverwrite it with your version?"))
            {
                SetStatus($"Saving {name} cancelled");
                return;
            }

            result = await SaveWithWaitCursorAsync(document, overwriteExternalChanges: true);
        }

        var changes = result?.Changes?.Operations.Count ?? 0;
        SetStatus(result switch
        {
            null => $"{name} has no unsaved changes",
            { LogError: { } error } => $"{name} saved · the change log could not be written: {error}",
            { Backup: null } => $"{name} saved · {changes} changes logged",
            { Backup: var backup } => $"{name} saved · backup from {Format.TimeWithSeconds(backup.Timestamp)} · {changes} changes logged"
        });
    }

    private async Task<SaveResult?> SaveWithWaitCursorAsync(XmlConfigDocument document, bool overwriteExternalChanges)
    {
        UseWaitCursor = true;
        try
        {
            return await _workspace.SaveAsync(document, overwriteExternalChanges);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async Task SaveAllAsync()
    {
        _editor.CommitEdit();
        foreach (var document in _workspace.Store.Dirty.ToList())
        {
            await SaveAsync(document);
        }
    }

    private async Task ChooseFolderAsync()
    {
        var folder = PickConfigFolder(_workspace.Folder);
        if (folder is not null && ConfirmDiscard())
        {
            await OpenFolderAsync(folder);
        }
    }

    private async Task ReloadAsync()
    {
        if (_workspace.IsOpen && ConfirmDiscard())
        {
            await OpenFolderAsync(_workspace.Folder);
        }
    }

    private async Task ToggleSubfoldersAsync()
    {
        var include = _includeSubfolders.Checked;
        if (_workspace.Settings.IncludeSubfolders == include)
        {
            return;
        }

        if (!ConfirmDiscard())
        {
            _includeSubfolders.Checked = !include;
            return;
        }

        _workspace.Settings.IncludeSubfolders = include;
        _workspace.SaveSettings();
        if (_workspace.IsOpen)
        {
            await OpenFolderAsync(_workspace.Folder);
        }
    }

    private async Task RefreshFolderAsync()
    {
        _kinds.Clear();
        await _workspace.RefreshAsync();
        _tree.Populate(_workspace);
        UpdateSummary();
    }

    private bool ConfirmDiscard()
    {
        _editor.CommitEdit();
        return !_workspace.HasUnsavedChanges && !_editor.HasPendingXml || Dialogs.Confirm(this, "Unsaved changes will be lost. Continue?");
    }

    private void FocusFilter()
    {
        _filter.Focus();
        _filter.SelectAll();
    }

    private void ShowSearch()
    {
        if (!_workspace.IsOpen)
        {
            return;
        }

        if (_searchForm is null || _searchForm.IsDisposed)
        {
            _searchForm = new SearchForm(_workspace, hit => _ = RunAsync(() => NavigateAsync(hit.FilePath, hit.CategoryPath, hit.EntryIndex, hit.FieldPath, hit.EntryTitle)));
            _searchForm.Show(this);
        }
        else
        {
            _searchForm.Activate();
        }
    }

    private void ShowBackups()
    {
        if (!_workspace.IsOpen)
        {
            return;
        }

        using var form = new BackupsForm(_workspace);
        form.ShowDialog(this);
        if (form.RestoredAny)
        {
            _ = RunAsync(RefreshFolderAsync);
        }
    }

    private void ShowChanges()
    {
        if (!_workspace.IsOpen)
        {
            return;
        }

        using var form = new ChangesForm(_workspace);
        form.ShowDialog(this);
        if (form.AppliedAny)
        {
            _kinds.Clear();
            _tree.RefreshDirtyMarkers(_workspace);
            UpdateTitle();
            _navigation = RunAsync(() => ShowTargetAsync(_tree.SelectedTarget));
        }
    }

    private void RevealSelected()
    {
        var path = _list.SelectedRow?.Tag switch
        {
            FileTarget f => f.File.FullPath,
            CategoryTarget c => c.File.FullPath,
            _ => _document?.FilePath
        };

        if (path is not null)
        {
            Dialogs.Reveal(path);
        }
    }

    private void ShowOptions()
    {
        _editor.CommitEdit();
        if (_editor.HasPendingXml && (!Dialogs.Confirm(this, "The XML tab has edits that were not applied yet. Apply them before changing the look?") || !_editor.ApplyPendingXml()))
        {
            return;
        }

        using var form = new OptionsForm(_workspace.Settings);
        if (form.ShowDialog(this) != DialogResult.OK || !form.Changed)
        {
            return;
        }

        _workspace.SaveSettings();
        UiTheme.Apply(_workspace.Settings, startup: false);
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RevealFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Dialogs.Reveal(folder);
        }
    }

    private void ViewIn3D() => _viewerToggle.Checked = !_viewerToggle.Checked;

    private void OnViewerToggled()
    {
        if (_viewerToggle.Checked && !_pedViewer.IsAvailable)
        {
            _viewerToggle.Checked = false;
            Dialogs.Info(this, PedViewerHost.IsInstalled
                ? $"The 3D viewer plugin could not be loaded:\n{PedViewerHost.PluginPath}"
                : $"The 3D viewer plugin is not installed.\n\nBuild src/LsrXmlEditor.PedViewer (requires CodeWalker.Core) so that it ends up in:\n{PedViewerHost.PluginPath}");
            return;
        }

        if (_viewerToggle.Checked && !EnsureCodeWalker())
        {
            _viewerToggle.Checked = false;
            return;
        }

        _workspace.Settings.ShowViewer = _viewerToggle.Checked;
        _workspace.SaveSettings();
        if (_viewerToggle.Checked)
        {
            DockSelectedEntry();
        }
        else
        {
            CloseViewerDock();
        }
    }

    private Control BuildViewerDock()
    {
        var header = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        var close = new ToolStripButton("✕") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Close 3D view" };
        close.Click += (_, _) => _viewerToggle.Checked = false;
        UiTheme.Fit(header);
        header.Items.AddRange([new ToolStripLabel("3D view") { Font = _headerFont }, close]);

        var dock = new Panel { Dock = DockStyle.Fill };
        dock.Controls.Add(_viewerContent);
        dock.Controls.Add(header);
        return dock;
    }

    private XElement? SelectedPreviewEntry()
    {
        var selected = SelectedEntryTags();
        return selected.Count == 1 && _pedViewer.CanPreview(XmlEditing.Serialize(selected[0].Element))
            ? selected[0].Element
            : null;
    }

    private void DockGridSelection(GridItem? item)
    {
        if (!_viewerToggle.Checked || _editor.Entry is not { } entry)
        {
            return;
        }

        var element = ElementOf(item);
        if (element is null || (element != entry && !element.Ancestors().Contains(entry)))
        {
            return;
        }

        _ = RunAsync(async () =>
        {
            foreach (var candidate in element.AncestorsAndSelf().TakeWhile(e => e != entry.Parent).Where(e => e.HasElements))
            {
                if (await PreviewSourceAsync(candidate) is not { } target)
                {
                    continue;
                }

                if (!ReferenceEquals(target, _dockedEntry))
                {
                    await DockEntryAsync(target);
                }

                return;
            }
        });
    }

    private async Task<XElement?> PreviewSourceAsync(XElement element)
    {
        if (_pedViewer.CanPreview(XmlEditing.Serialize(element)))
        {
            return element;
        }

        var name = element.Element(ModItemReference)?.Value.Trim();
        if (string.IsNullOrEmpty(name) || _workspace.FindBase(ModItemsFile) is not { } file)
        {
            return null;
        }

        var items = await _workspace.LoadAsync(file.FullPath);
        return items.Root.Descendants()
            .FirstOrDefault(e => e.Element(ModItemName)?.Value.Trim() == name && _pedViewer.CanPreview(XmlEditing.Serialize(e)));
    }

    private static XElement? ElementOf(GridItem? item)
    {
        for (var current = item; current is not null; current = current.Parent)
        {
            if (current.PropertyDescriptor is XmlFieldDescriptor field)
            {
                return field.Element;
            }

            if (current.Value is XmlNodeObject node)
            {
                return node.Element;
            }
        }

        return null;
    }

    private void DockSelectedEntry()
    {
        if (!_viewerToggle.Checked || SelectedPreviewEntry() is not { } entry)
        {
            return;
        }

        if (!_editorSplit.Panel2Collapsed && ReferenceEquals(entry, _dockedEntry))
        {
            return;
        }

        _ = RunAsync(() => DockEntryAsync(entry));
    }

    private async Task DockEntryAsync(XElement entry)
    {
        var gtaFolder = PedViewerHost.FindGtaFolder(_workspace.Folder, _workspace.Settings.GtaFolder) ?? PickGtaFolder();
        if (gtaFolder is null)
        {
            _viewerToggle.Checked = false;
            return;
        }

        if (!_viewerToggle.Checked)
        {
            return;
        }

        if (_editorSplit.Panel2Collapsed)
        {
            _editorSplit.Panel2Collapsed = false;
            ArrangeQuarters();
        }

        if (_viewerPanel is null)
        {
            _viewerPanel = _pedViewer.CreatePanel();
            _viewerPanel.Dock = DockStyle.Fill;
            _viewerContent.Controls.Add(_viewerPanel);
            _pedViewer.SetEntryWriter(_viewerPanel, WriteDockedEntry);
        }

        _dockedEntry = entry;
        var panel = _viewerPanel;
        string? error;
        try
        {
            error = await _pedViewer.ShowEntryAsync(panel, gtaFolder, XmlEditing.Serialize(entry), PreviewTitle(entry));
        }
        catch (Exception) when (panel.IsDisposed)
        {
            return;
        }
        catch (Exception ex) when (ex is MissingMethodException or MissingFieldException or TypeLoadException or FileNotFoundException or FileLoadException)
        {
            _viewerToggle.Checked = false;
            Dialogs.Error(this, $"The 3D view could not use CodeWalker:\n{ex.Message}\n\nThis CodeWalker version may not be compatible. Select a different CodeWalker folder the next time you turn on 3D View.");
            _workspace.Settings.CodeWalkerFolder = null;
            _workspace.SaveSettings();
            return;
        }

        if (error is not null && !panel.IsDisposed)
        {
            Dialogs.Info(this, error);
        }
    }

    private static string PreviewTitle(XElement entry) =>
        PreviewTitleFields.Select(n => entry.Element(n)?.Value).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? EntryIdentity.TitleOf(entry);

    private void WriteDockedEntry(string xml)
    {
        if (_dockedEntry is not { } entry)
        {
            return;
        }

        var replacement = XElement.Parse(xml);
        if (entry.Descendants(replacement.Name).FirstOrDefault() is not { } target)
        {
            Dialogs.Info(this, $"The entry has no <{replacement.Name.LocalName}> element to write to.");
            return;
        }

        XmlEditing.ReplaceFormatted(target, replacement);
        if (_editor.Entry is { } shown && (shown == entry || shown.Ancestors().Contains(entry) || entry.Ancestors().Contains(shown)))
        {
            OnListSelectionChanged();
        }

        SetStatus($"{DisplayNames.Humanize(replacement.Name.LocalName)} written to {PreviewTitle(entry)}");
    }

    private string? PickConfigFolder(string initial)
    {
        while (Dialogs.PickFolder(this, Dialogs.ConfigFolderPrompt, initial) is { } folder)
        {
            if (ConfigFolder.Resolve(folder) is { } resolved)
            {
                return resolved;
            }

            if (!Dialogs.Confirm(this, $"No Los Santos RED XML files were found in:\n{folder}\n\nThey are usually in ...\\Grand Theft Auto V\\Plugins\\LosSantosRED.\nChoose another folder?"))
            {
                return null;
            }

            initial = folder;
        }

        return null;
    }

    private bool EnsureCodeWalker()
    {
        if (_pedViewer.HasCodeWalker)
        {
            return true;
        }

        Dialogs.Info(this, "The 3D view reads the game files with CodeWalker, which is not included.\n\nDownload CodeWalker from github.com/dexyfex/CodeWalker and select its folder in the next step.");
        while (Dialogs.PickFolder(this, Dialogs.CodeWalkerPrompt, _workspace.Settings.CodeWalkerFolder ?? string.Empty) is { } folder)
        {
            if (PedViewerHost.FindCodeWalkerCore(folder) is { } dll)
            {
                _workspace.Settings.CodeWalkerFolder = Path.GetDirectoryName(dll);
                _workspace.SaveSettings();
                _pedViewer.CodeWalkerFolder = _workspace.Settings.CodeWalkerFolder;
                return true;
            }

            if (!Dialogs.Confirm(this, $"CodeWalker.Core.dll was not found in:\n{folder}\n\nChoose another folder?"))
            {
                break;
            }
        }

        return false;
    }

    private string? PickGtaFolder()
    {
        while (Dialogs.PickFolder(this, Dialogs.GtaFolderPrompt, string.Empty) is { } folder)
        {
            if (GameInstall.IsGameFolder(folder))
            {
                _workspace.Settings.GtaFolder = folder;
                _workspace.SaveSettings();
                return folder;
            }

            Dialogs.Error(this, $"{folder} does not contain GTA5.exe and x64v.rpf.\n\nSelect the folder Grand Theft Auto V is installed in.");
        }

        return null;
    }

    private void FitFilter(ToolStrip bar)
    {
        var others = bar.Items.Cast<ToolStripItem>().Where(i => i != _filter).Sum(i => i.GetPreferredSize(Size.Empty).Width + i.Margin.Horizontal);
        var available = bar.DisplayRectangle.Width - others - _filter.Margin.Horizontal - bar.Padding.Horizontal;
        var width = Math.Clamp(available, LogicalToDeviceUnits(MinFilterWidth), LogicalToDeviceUnits(FilterWidth));
        if (_filter.Width != width)
        {
            _filter.Width = width;
        }
    }

    private void ArrangeQuarters()
    {
        var quarter = (_outerSplit.Width - 3 * _outerSplit.SplitterWidth) / 4;
        if (quarter <= 0)
        {
            return;
        }

        _outerSplit.SetDistance(quarter);
        _innerSplit.SetDistance(quarter);
        _editorSplit.SetDistance(_editorSplit.Width - quarter - _editorSplit.SplitterWidth);
    }

    private void CloseViewerDock()
    {
        _editorSplit.Panel2Collapsed = true;
        _dockedEntry = null;
        if (_viewerPanel is null)
        {
            return;
        }

        _viewerContent.Controls.Remove(_viewerPanel);
        _viewerPanel.Dispose();
        _viewerPanel = null;
        _pedViewer.Release();
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }

    private void UpdateRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        foreach (var folder in _workspace.Settings.RecentFolders)
        {
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem(folder, null, (_, _) =>
            {
                var resolved = ConfigFolder.Resolve(folder) ?? PickConfigFolder(folder);
                if (resolved is not null && ConfirmDiscard())
                {
                    _ = RunAsync(() => OpenFolderAsync(resolved));
                }
            }));
        }

        _recentMenu.Enabled = _recentMenu.DropDownItems.Count > 0;
    }

    private void UpdateSummary()
    {
        var files = _workspace.Files;
        _summary.Text = $"{files.Count} files · {Format.Number(files.Where(f => !f.File.IsVariant).Sum(f => f.EntryCount))} entries";
    }

    private void UpdateTitle()
    {
        var dirty = _workspace.HasUnsavedChanges ? " *" : string.Empty;
        Text = _workspace.IsOpen ? $"{Dialogs.AppTitle} – {_workspace.Folder}{dirty}" : Dialogs.AppTitle;
    }

    private IEnumerable<ConfigFile> FilesOf(string group) =>
        _workspace.Files.Where(f => string.Equals(f.Info.Group, group, StringComparison.Ordinal));

    private static string FileTitle(ConfigFile file) => $"{file.Info.Title} ({file.File.FileName})";

    private void SetStatus(string text) => _status.Text = text;

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
            Dialogs.Error(this, ex.Message);
        }
    }

    private Image? GlyphImage(Glyph? glyph) =>
        glyph is { } value ? StockImages.For(value, LogicalToDeviceUnits(StockImages.SmallSize)) : null;

    private ToolStripMenuItem MenuItem(Command command)
    {
        var item = ContextItem(command);
        item.ShortcutKeys = command.Shortcut;
        return item;
    }

    private ToolStripMenuItem ContextItem(Command command) =>
        new(command.Text, GlyphImage(command.Icon), (_, _) => command.Execute()) { ShortcutKeyDisplayString = command.KeyText };

    private ToolStripButton ToolButton(Command command, string text) =>
        new(text, GlyphImage(command.Icon), (_, _) => command.Execute())
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = command.ToolTip
        };

    private sealed record Command(string Text, Action Execute, Keys Shortcut = Keys.None, Glyph? Icon = null, string? ShortcutText = null)
    {
        public string? KeyText => ShortcutText ?? (Shortcut == Keys.None ? null : KeyNames.ConvertToString(Shortcut));

        public string ToolTip
        {
            get
            {
                var name = Text.Replace("&", string.Empty).TrimEnd('.');
                return KeyText is null ? name : $"{name} ({KeyText})";
            }
        }
    }
}
