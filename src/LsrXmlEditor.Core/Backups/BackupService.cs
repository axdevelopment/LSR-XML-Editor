using System.Globalization;

namespace LsrXmlEditor.Core.Backups;

public sealed record BackupInfo(string BackupPath, string RelativePath, DateTime Timestamp, long SizeBytes)
{
    public string FileName => Path.GetFileName(RelativePath);
}

public sealed class BackupService(string backupRoot, int keepPerFile = BackupService.DefaultKeepPerFile)
{
    public const int DefaultKeepPerFile = 30;

    private const string StampFormat = "yyyyMMdd-HHmmss-fff";

    public string BackupRoot { get; } = backupRoot;

    public BackupInfo? Create(string sourceFolder, string filePath)
    {
        var backup = Copy(sourceFolder, filePath);
        if (backup is not null)
        {
            Prune(backup.RelativePath);
        }

        return backup;
    }

    public IReadOnlyList<BackupInfo> List()
    {
        var root = new DirectoryInfo(BackupRoot);
        if (!root.Exists)
        {
            return [];
        }

        var result = new List<BackupInfo>();
        foreach (var dir in root.EnumerateDirectories())
        {
            if (!DateTime.TryParseExact(dir.Name, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp))
            {
                continue;
            }

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                result.Add(new BackupInfo(file.FullName, Path.GetRelativePath(dir.FullName, file.FullName), stamp, file.Length));
            }
        }

        result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return result;
    }

    public BackupInfo? Restore(BackupInfo backup, string sourceFolder)
    {
        var target = Path.Combine(sourceFolder, backup.RelativePath);
        var safety = Copy(sourceFolder, target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(backup.BackupPath, target, overwrite: true);
        Prune(backup.RelativePath);
        return safety;
    }

    private BackupInfo? Copy(string sourceFolder, string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var relative = Path.GetRelativePath(sourceFolder, filePath);
        var stamp = DateTime.Now;
        var target = Path.Combine(BackupRoot, stamp.ToString(StampFormat, CultureInfo.InvariantCulture), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(filePath, target, overwrite: true);
        return new BackupInfo(target, relative, stamp, new FileInfo(target).Length);
    }

    private void Prune(string relative)
    {
        var expired = List()
            .Where(b => string.Equals(b.RelativePath, relative, StringComparison.OrdinalIgnoreCase))
            .Skip(keepPerFile);

        foreach (var backup in expired)
        {
            File.Delete(backup.BackupPath);
            var dir = Path.GetDirectoryName(backup.BackupPath)!;
            while (!string.Equals(dir, BackupRoot, StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir)!;
            }
        }
    }
}
