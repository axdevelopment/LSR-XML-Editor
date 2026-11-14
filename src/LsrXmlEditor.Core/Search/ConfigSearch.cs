using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Search;

public sealed record SearchHit(
    string FilePath,
    string RelativeFile,
    string CategoryPath,
    string CategoryName,
    int EntryIndex,
    string EntryTitle,
    string FieldPath,
    string Value);

public sealed record SearchQuery(string Text, bool MatchCase = false, bool IncludeFieldNames = true);

public static class ConfigSearch
{
    public static void Run(XElement root, string filePath, string relativeFile, SearchQuery query, Action<SearchHit> onHit, CancellationToken token)
    {
        var comparison = query.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var category in StructureAnalyzer.Analyze(root))
        {
            var index = 0;
            foreach (var entry in category.Entries)
            {
                token.ThrowIfCancellationRequested();
                string? title = null;

                foreach (var leaf in entry.DescendantsAndSelf())
                {
                    if (leaf.HasElements)
                    {
                        continue;
                    }

                    var value = leaf.Value;
                    if (value.Contains(query.Text, comparison) || (query.IncludeFieldNames && leaf.Name.LocalName.Contains(query.Text, comparison)))
                    {
                        title ??= EntryIdentity.TitleOf(entry);
                        onHit(new SearchHit(filePath, relativeFile, category.Path, category.Name, index, title, XmlEditing.PathOf(leaf, entry), value));
                    }
                }

                index++;
            }
        }
    }
}
