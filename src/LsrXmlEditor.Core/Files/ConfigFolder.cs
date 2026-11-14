namespace LsrXmlEditor.Core.Files;

public static class ConfigFolder
{
    public const string PluginsFolder = "Plugins";
    public const string LsrFolder = "LosSantosRED";
    private const string XmlPattern = "*.xml";

    public static string? Resolve(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        string[] candidates =
        [
            folder,
            Path.Combine(folder, LsrFolder),
            Path.Combine(folder, PluginsFolder, LsrFolder)
        ];

        return candidates.FirstOrDefault(HasConfigs);
    }

    public static string? FromGameFolders(IEnumerable<string> gameFolders) =>
        gameFolders.Select(g => Resolve(Path.Combine(g, PluginsFolder, LsrFolder))).FirstOrDefault(f => f is not null);

    public static bool HasConfigs(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return false;
        }

        var known = KnownConfigs.BaseNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(folder, XmlPattern).Any(f => known.Contains(Path.GetFileNameWithoutExtension(f)));
    }
}
