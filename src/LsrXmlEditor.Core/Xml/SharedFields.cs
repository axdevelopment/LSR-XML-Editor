using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public sealed record SharedField(string Segment, XName Name, IReadOnlyList<XElement> Elements, bool IsLeaf);

public static class SharedFields
{
    public static IReadOnlyList<SharedField> Of(IReadOnlyList<XElement> parents)
    {
        if (parents.Count == 0)
        {
            return [];
        }

        var maps = parents.Select(p => XmlEditing.ChildSegments(p).ToDictionary(s => s.Segment, s => s.Element, StringComparer.Ordinal)).ToList();
        var result = new List<SharedField>();
        foreach (var (segment, first, _) in XmlEditing.ChildSegments(parents[0]))
        {
            var elements = new List<XElement>(parents.Count) { first };
            for (var i = 1; i < maps.Count && elements.Count == i; i++)
            {
                if (maps[i].TryGetValue(segment, out var match))
                {
                    elements.Add(match);
                }
            }

            if (elements.Count != parents.Count)
            {
                continue;
            }

            if (elements.All(e => !e.HasElements))
            {
                result.Add(new SharedField(segment, first.Name, elements, true));
            }
            else if (elements.All(e => e.HasElements && !StructureAnalyzer.IsCollection(e)))
            {
                result.Add(new SharedField(segment, first.Name, elements, false));
            }
        }

        return result;
    }

    public static string? CommonValue(IReadOnlyList<XElement> elements)
    {
        if (elements.Count == 0)
        {
            return null;
        }

        var value = elements[0].Value;
        for (var i = 1; i < elements.Count; i++)
        {
            if (!string.Equals(elements[i].Value, value, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return value;
    }

    public static int DistinctValueCount(IReadOnlyList<XElement> elements) =>
        elements.Select(e => e.Value).Distinct(StringComparer.Ordinal).Count();
}
