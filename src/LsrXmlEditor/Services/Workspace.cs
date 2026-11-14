using LsrXmlEditor.Core.Backups;
using LsrXmlEditor.Core.Changes;
using LsrXmlEditor.Core.Files;
using LsrXmlEditor.Core.Storage;
using LsrXmlEditor.Core.Workspace;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Services;

public sealed class ConfigFile(LsrFile file)
{
    public LsrFile File { get; } = file;

    public FileStats? Stats { get; set; }

    public ConfigInfo Info { get; } = KnownConfigs.Describe(file.BaseName);

    public string FullPath => File.FullPath;

    public int EntryCount => Stats?.EntryCount ?? 0;
}

public sealed class Workspace
{
    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private ConfigSaver? _saver;
    private int _scanVersion;

    public Workspace(AppPaths paths)
    {
        _paths = paths;
        Settings = AppSettings.Load(paths.SettingsFile);
        Store.Replaced += (_, path) =>
        {
            RetainHistory();
            DocumentReplaced?.Invoke(this, path);
        };
    }

    public event EventHandler? DirtyChanged;

    public event EventHandler<string>? DocumentReplaced;

    public AppSettings Settings { get; }

    public string Folder { get; private set; } = string.Empty;

    public IReadOnlyList<ConfigFile> Files { get; private set; } = [];

    public DocumentStore Store { get; } = new();

    public UndoHistory History { get; } = new();

    public BackupService? Backups { get; private set; }

    public ChangeLogStore? ChangeLog { get; private set; }

    public string DataRoot => _paths.DataRoot;

    public bool IsOpen => Backups is not null;

    public bool HasUnsavedChanges => Store.Dirty.Any();

    public async Task OpenAsync(string folder)
    {
        Folder = Path.GetFullPath(folder);
        Store.Clear();
        History.Clear();
        Backups = new BackupService(_paths.BackupRoot(Folder), Settings.BackupsPerFile);
        ChangeLog = new ChangeLogStore(_paths.ChangeLogFile(Folder));
        _saver = new ConfigSaver(Folder, Backups, ChangeLog);
        Settings.Remember(Folder);
        SaveSettings();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var version = ++_scanVersion;
        var folder = Folder;
        var includeSubfolders = Settings.IncludeSubfolders;

        var files = await Task.Run(() =>
        {
            var discovered = FileScanner.Discover(folder, includeSubfolders).Select(f => new ConfigFile(f)).ToList();
            Parallel.ForEach(discovered, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
                file => file.Stats = FileScanner.Analyze(file.FullPath));
            return discovered;
        });

        if (version == _scanVersion)
        {
            Files = files;
        }
    }

    public ConfigFile? Find(string path) =>
        Files.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));

    public ConfigFile? FindBase(string baseName) =>
        Files.FirstOrDefault(f => f.File.Kind is LsrFileKind.Base or LsrFileKind.SaveData && f.File.IsInRootFolder
            && string.Equals(f.File.BaseName, baseName, StringComparison.OrdinalIgnoreCase));

    public async Task<XmlConfigDocument> LoadAsync(string path)
    {
        var document = await Store.GetAsync(path);
        History.Track(document.Document, path);
        document.DirtyChanged -= OnDocumentDirtyChanged;
        document.DirtyChanged += OnDocumentDirtyChanged;
        return document;
    }

    public bool IsDirty(string path) => Store.Find(path)?.IsDirty == true;

    public async Task<SaveResult?> SaveAsync(XmlConfigDocument document, bool overwriteExternalChanges = false)
    {
        var saver = _saver ?? throw new InvalidOperationException("No folder is open");
        await _saveGate.WaitAsync();
        try
        {
            if (!document.IsDirty)
            {
                return null;
            }

            var snapshot = document.CreateSnapshot();
            var result = await Task.Run(() => saver.Write(snapshot, overwriteExternalChanges));
            document.AcceptSave(snapshot);

            if (Find(document.FilePath) is { } file)
            {
                file.Stats = await Task.Run(() => FileScanner.Analyze(snapshot.Content));
            }

            return result;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public void Discard(string path)
    {
        Store.Evict(path);
        RetainHistory();
    }

    private void RetainHistory() => History.Retain(Store.Loaded.Select(d => d.Document));

    public void SaveSettings() => Settings.Save(_paths.SettingsFile);

    private void OnDocumentDirtyChanged(object? sender, EventArgs e) => DirtyChanged?.Invoke(sender, e);
}
