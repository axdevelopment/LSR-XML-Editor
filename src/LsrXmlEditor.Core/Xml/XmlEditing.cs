using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public readonly record struct ChildSegment(string Segment, XElement Element, int Index);

public static class XmlEditing
{
    public static void SetValue(XElement element, string value)
    {
        if (element.HasElements || element.Value == value)
        {
            return;
        }

        if (value.Length == 0)
        {
            element.RemoveNodes();
        }
        else
        {
            element.Value = value;
        }
    }

    public static XElement CloneAfter(XElement source)
    {
        var clone = new XElement(source);
        InsertAfter(source, clone);
        return clone;
    }

    public static void InsertAfter(XElement anchor, XElement element)
    {
        if (anchor.PreviousNode is XText { Value: var indent } && string.IsNullOrWhiteSpace(indent))
        {
            anchor.AddAfterSelf(new XText(indent), element);
        }
        else
        {
            anchor.AddAfterSelf(element);
        }
    }

    public static void Append(XElement container, XElement element, XName? itemName)
    {
        var last = itemName is null ? container.Elements().LastOrDefault() : container.Elements(itemName).LastOrDefault();
        if (last is not null)
        {
            InsertAfter(last, element);
            return;
        }

        container.Add(element);
    }

    public static XElement? ItemTemplate(XElement list)
    {
        if (StructureAnalyzer.IsCollection(list))
        {
            return list.Elements().Last();
        }

        var parent = list.Parent?.Name;
        var sample = list.Document?.Root?
            .Descendants(list.Name)
            .FirstOrDefault(e => e.Parent?.Name == parent && StructureAnalyzer.IsCollection(e));
        if (sample is null)
        {
            return null;
        }

        var item = sample.Elements().First();
        return list.HasElements
            ? list.Elements().All(e => e.Name == item.Name) ? list.Elements().Last() : null
            : list.IsEmpty || string.IsNullOrWhiteSpace(list.Value) ? item : null;
    }

    public static XElement AddItem(XElement list, XElement template)
    {
        var item = new XElement(template);
        if (list.Elements().LastOrDefault() is { } last)
        {
            InsertAfter(last, item);
            return item;
        }

        var before = template.PreviousNode is XText { Value: var lead } && string.IsNullOrWhiteSpace(lead) ? lead : null;
        var after = template.Parent?.LastNode is XText { Value: var tail } && string.IsNullOrWhiteSpace(tail) ? tail : null;
        list.RemoveNodes();
        list.Add(before is null ? null : new XText(before), item, after is null ? null : new XText(after));
        return item;
    }

    public static void Remove(XElement element)
    {
        if (element.PreviousNode is XText text && string.IsNullOrWhiteSpace(text.Value))
        {
            text.Remove();
        }

        element.Remove();
    }

    public static XElement ReplaceFormatted(XElement target, XElement replacement)
    {
        var whitespace = target.PreviousNode is XText { Value: var text } && string.IsNullOrWhiteSpace(text) ? text : string.Empty;
        var indent = whitespace[(whitespace.LastIndexOf('\n') + 1)..];
        var newline = whitespace.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = replacement.ToString().Split(["\r\n", "\n"], StringSplitOptions.None);
        var formatted = Deserialize(string.Join(newline + indent, lines));
        target.ReplaceWith(formatted);
        return formatted;
    }

    public static string Serialize(XElement element) => element.ToString(SaveOptions.DisableFormatting);

    public static XElement Deserialize(string xml) => XElement.Parse(xml, LoadOptions.PreserveWhitespace);

    public static IReadOnlyList<ChildSegment> ChildSegments(XElement parent)
    {
        var totals = new Dictionary<XName, int>();
        foreach (var child in parent.Elements())
        {
            CollectionsMarshal.GetValueRefOrAddDefault(totals, child.Name, out _)++;
        }

        var seen = new Dictionary<XName, int>();
        var segments = new List<ChildSegment>();
        foreach (var child in parent.Elements())
        {
            ref var index = ref CollectionsMarshal.GetValueRefOrAddDefault(seen, child.Name, out _);
            var name = child.Name.LocalName;
            segments.Add(new ChildSegment(totals[child.Name] > 1 ? $"{name}[{index}]" : name, child, index));
            index++;
        }

        return segments;
    }

    public static XElement? Resolve(XElement entry, string path)
    {
        if (path.Length == 0)
        {
            return entry;
        }

        var current = entry;
        foreach (var segment in path.Split('/'))
        {
            var bracket = segment.IndexOf('[');
            var name = bracket < 0 ? segment : segment[..bracket];
            var index = bracket < 0 ? 0 : int.Parse(segment.AsSpan(bracket + 1, segment.Length - bracket - 2));
            current = current.Elements(name).ElementAtOrDefault(index);
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    public static IEnumerable<(string Path, XElement Element)> Leaves(XElement entry)
    {
        if (!entry.HasElements)
        {
            return [(string.Empty, entry)];
        }

        var result = new List<(string, XElement)>();
        CollectLeaves(entry, string.Empty, result);
        return result;
    }

    public static string PathOf(XElement element, XElement entry)
    {
        var segments = new Stack<string>();
        for (var current = element; current != entry; current = current.Parent!)
        {
            segments.Push(SegmentOf(current));
        }

        return string.Join('/', segments);
    }

    private static string SegmentOf(XElement element)
    {
        var name = element.Name;
        var index = element.ElementsBeforeSelf(name).Count();
        return index > 0 || element.ElementsAfterSelf(name).Any() ? $"{name.LocalName}[{index}]" : name.LocalName;
    }

    private static void CollectLeaves(XElement parent, string prefix, List<(string, XElement)> result)
    {
        foreach (var (segment, child, _) in ChildSegments(parent))
        {
            var path = prefix.Length == 0 ? segment : prefix + "/" + segment;
            if (child.HasElements)
            {
                CollectLeaves(child, path, result);
            }
            else
            {
                result.Add((path, child));
            }
        }
    }
}
