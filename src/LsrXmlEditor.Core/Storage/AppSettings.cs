using System.Text.Json;
using LsrXmlEditor.Core.Backups;

namespace LsrXmlEditor.Core.Storage;

public sealed class AppSettings
{
    public const string DefaultFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Grand Theft Auto V\Plugins\LosSantosRED";

    private const int MaxRecentFolders = 8;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Folder { get; set; } = DefaultFolder;

    public bool IncludeSubfolders { get; set; } = true;

    public int BackupsPerFile { get; set; } = BackupService.DefaultKeepPerFile;

    public string? GtaFolder { get; set; }

    public bool ShowViewer { get; set; }

    public string? CodeWalkerFolder { get; set; }

    public List<string> RecentFolders { get; set; } = [];

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public void Remember(string folder)
    {
        Folder = folder;
        RecentFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, folder);
        if (RecentFolders.Count > MaxRecentFolders)
        {
            RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);
        }
    }
}
