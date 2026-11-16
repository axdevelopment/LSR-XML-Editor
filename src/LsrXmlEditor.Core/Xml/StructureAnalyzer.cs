using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public static class StructureAnalyzer
{
    private static readonly HashSet<string> ScalarItemNames = new(StringComparer.Ordinal)
    {
        "string", "int", "long", "short", "uint", "float", "double", "decimal", "bool", "Vector3"
    };

    public static IReadOnlyList<EntryCategory> Analyze(XElement root)
    {
        if (TryGetItemName(root, out var rootItem) && !IsWrapper(root))
        {
            return [EntryCategory.Collection(root, rootItem)];
        }

        var result = new List<EntryCategory>();
        var records = new HashSet<XName>();

        foreach (var child in root.Elements())
        {
            if (TryGetItemName(child, out var itemName))
            {
                result.Add(EntryCategory.Collection(child, itemName));
            }
            else if (IsContainer(child))
            {
                foreach (var nested in child.Elements())
                {
                    if (TryGetItemName(nested, out var nestedItem))
                    {
                        result.Add(EntryCategory.Collection(nested, nestedItem));
                    }
                }
            }
            else if (!IsBlank(child))
            {
                records.Add(child.Name);
            }
        }

        if (records.Count > 0)
        {
            result.Insert(0, EntryCategory.Records(root, records));
        }

        return result;
    }

    public static bool TryGetItemName(XElement element, out XName itemName)
    {
        itemName = null!;
        XName? name = null;
        var count = 0;
        var complex = false;

        foreach (var child in element.Elements())
        {
            if (name is null)
            {
                name = child.Name;
            }
            else if (child.Name != name)
            {
                return false;
            }

            count++;
            complex |= child.HasElements;
        }

        if (name is null || !(count > 1 || complex || ScalarItemNames.Contains(name.LocalName)))
        {
            return false;
        }

        itemName = name;
        return true;
    }

    public static bool IsCollection(XElement element) =>
        TryGetItemName(element, out _) && element.Elements().Skip(1).Any();

    private static bool IsWrapper(XElement root)
    {
        if (root.Name.LocalName.StartsWith("ArrayOf", StringComparison.Ordinal))
        {
            return false;
        }

        var single = root.Elements().Take(2).ToList();
        return single.Count == 1 && (TryGetItemName(single[0], out _) || IsContainer(single[0]));
    }

    private static bool IsContainer(XElement element)
    {
        var any = false;
        foreach (var child in element.Elements())
        {
            if (TryGetItemName(child, out _))
            {
                any = true;
            }
            else if (!IsBlank(child))
            {
                return false;
            }
        }

        return any;
    }

    private static bool IsBlank(XElement element) => !element.HasElements && string.IsNullOrWhiteSpace(element.Value);
}
