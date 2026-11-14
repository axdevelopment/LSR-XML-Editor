using System.Xml;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Files;

public static class FileScanner
{
    private const int SkeletonDepth = 3;
    private const int ReadBufferSize = 1 << 16;
    private static readonly XName Truncated = "__truncated";

    public static IReadOnlyList<LsrFile> Discover(string folder, bool includeSubfolders)
    {
        var root = new DirectoryInfo(folder);
        if (!root.Exists)
        {
            return [];
        }

        var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = root.EnumerateFiles("*.xml", option).ToList();
        var baseNames = new HashSet<string>(KnownConfigs.BaseNames, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var stem = Path.GetFileNameWithoutExtension(file.Name);
            if (stem.IndexOf('_') < 0)
            {
                baseNames.Add(stem);
            }
        }

        return files
            .Select(f => Describe(root.FullName, f, baseNames))
            .OrderBy(f => f.BaseName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Kind)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static (string BaseName, string? Suffix, LsrFileKind Kind) Classify(string fileName, ISet<string> baseNames)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var additive = stem.IndexOf("+_", StringComparison.Ordinal);
        if (additive > 0)
        {
            return (stem[..additive], stem[(additive + 2)..], LsrFileKind.AdditiveVariant);
        }

        var underscore = stem.IndexOf('_');
        if (underscore > 0 && baseNames.Contains(stem[..underscore]))
        {
            return (stem[..underscore], stem[(underscore + 1)..], LsrFileKind.ReplacementVariant);
        }

        return (stem, null, KnownConfigs.IsSaveData(stem) ? LsrFileKind.SaveData : LsrFileKind.Base);
    }

    public static FileStats Analyze(string path)
    {
        try
        {
            return Summarize(BuildSkeleton(path));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return FileStats.Failed(ex.Message);
        }
    }

    public static FileStats Analyze(XDocument document) =>
        Summarize(document.Root is { } root ? CopySkeleton(root, 0) : null);

    private static FileStats Summarize(XElement? root)
    {
        if (root is null)
        {
            return FileStats.Failed("Empty document");
        }

        var categories = StructureAnalyzer.Analyze(root)
            .Select(c => new CategorySummary(c.Path, c.Name, c.Count))
            .ToList();
        return new FileStats(root.Name.LocalName, categories, null);
    }

    private static XElement CopySkeleton(XElement source, int depth)
    {
        var copy = new XElement(source.Name);
        if (depth < SkeletonDepth)
        {
            foreach (var child in source.Elements())
            {
                copy.Add(CopySkeleton(child, depth + 1));
            }
        }
        else if (source.HasElements)
        {
            copy.Add(new XElement(Truncated));
        }

        return copy;
    }

    private static LsrFile Describe(string rootFolder, FileInfo file, ISet<string> baseNames)
    {
        var (baseName, suffix, kind) = Classify(file.Name, baseNames);
        var relative = Path.GetRelativePath(rootFolder, file.FullName);
        var inRoot = string.Equals(file.DirectoryName, rootFolder.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        return new LsrFile(file.FullName, relative, file.Name, baseName, suffix, kind, inRoot, file.Length, file.LastWriteTime);
    }

    private static XElement? BuildSkeleton(string path)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
            DtdProcessing = DtdProcessing.Prohibit
        };

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ReadBufferSize, FileOptions.SequentialScan);
        using var reader = XmlReader.Create(stream, settings);
        var stack = new XElement[SkeletonDepth + 1];
        XElement? root = null;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            var depth = reader.Depth;
            if (depth <= SkeletonDepth)
            {
                var element = new XElement(XName.Get(reader.LocalName, reader.NamespaceURI));
                if (depth == 0)
                {
                    root = element;
                }
                else
                {
                    stack[depth - 1].Add(element);
                }

                stack[depth] = element;
            }
            else if (depth == SkeletonDepth + 1 && !stack[SkeletonDepth].HasElements)
            {
                stack[SkeletonDepth].Add(new XElement(Truncated));
            }
        }

        return root;
    }
}
