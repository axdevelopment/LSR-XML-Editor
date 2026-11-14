using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public sealed class EntryCategory
{
    private readonly XName? _itemName;
    private readonly IReadOnlySet<XName>? _recordNames;

    private EntryCategory(string path, string name, XElement container, XName? itemName, IReadOnlySet<XName>? recordNames)
    {
        Path = path;
        Name = name;
        Container = container;
        _itemName = itemName;
        _recordNames = recordNames;
    }

    public string Path { get; }

    public string Name { get; }

    public XElement Container { get; }

    public bool IsRecordSet => _recordNames is not null;

    public XName? ItemName => _itemName;

    public IEnumerable<XElement> Entries => _itemName is not null
        ? Container.Elements(_itemName)
        : Container.Elements().Where(e => _recordNames!.Contains(e.Name));

    public int Count => Entries.Count();

    public static EntryCategory Collection(XElement container, XName itemName) =>
        new(PathOf(container), DisplayName(container, itemName), container, itemName, null);

    public static EntryCategory Records(XElement container, IReadOnlySet<XName> names) =>
        new(PathOf(container), "General", container, null, names);

    public string KeyOf(XElement entry) => EntryIdentity.KeyOf(entry, IsRecordSet);

    private static string DisplayName(XElement container, XName itemName)
    {
        if (container.Parent is not null)
        {
            return container.Name.LocalName;
        }

        var root = container.Name.LocalName;
        return root.StartsWith("ArrayOf", StringComparison.Ordinal) ? itemName.LocalName : root;
    }

    private static string PathOf(XElement element)
    {
        var parts = element.AncestorsAndSelf().Select(e => e.Name.LocalName).Reverse();
        return string.Join('/', parts);
    }
}
