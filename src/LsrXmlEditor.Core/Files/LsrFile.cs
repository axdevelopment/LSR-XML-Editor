namespace LsrXmlEditor.Core.Files;

public enum LsrFileKind
{
    Base,
    AdditiveVariant,
    ReplacementVariant,
    SaveData
}

public sealed record LsrFile(
    string FullPath,
    string RelativePath,
    string FileName,
    string BaseName,
    string? VariantSuffix,
    LsrFileKind Kind,
    bool IsInRootFolder,
    long SizeBytes,
    DateTime LastWriteTime)
{
    public bool IsVariant => Kind is LsrFileKind.AdditiveVariant or LsrFileKind.ReplacementVariant;
}

public sealed record CategorySummary(string Path, string Name, int Count);

public sealed record FileStats(string RootName, IReadOnlyList<CategorySummary> Categories, string? Error)
{
    public int EntryCount => Categories.Sum(c => c.Count);

    public static FileStats Failed(string error) => new(string.Empty, [], error);
}
