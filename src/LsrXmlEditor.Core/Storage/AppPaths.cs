using System.Security.Cryptography;
using System.Text;

namespace LsrXmlEditor.Core.Storage;

public sealed class AppPaths(string dataRoot)
{
    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LsrXmlEditor"));

    public string DataRoot { get; } = dataRoot;

    public string SettingsFile => Path.Combine(DataRoot, "settings.json");

    public string BackupRoot(string folder) => Path.Combine(DataRoot, "Backups", FolderKey(folder));

    public string ChangeLogFile(string folder) => Path.Combine(DataRoot, "Changes", FolderKey(folder) + ".json");

    public static string FolderKey(string folder)
    {
        var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..10];
        var name = new string(Path.GetFileName(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return $"{name}-{hash}";
    }
}
