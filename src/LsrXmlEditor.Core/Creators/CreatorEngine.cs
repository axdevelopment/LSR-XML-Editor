using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Creators;

public sealed record AreaRows(string FileBase, string Element, string KeyField, string OwnerField, IReadOnlyDictionary<string, string> Defaults);

public sealed class CreatorEngine(Func<string, IReadOnlyList<XDocument>> documents, Func<XDocument, string> fileName)
{
    private const string ListItem = "string";
    private const string PriorityField = "Priority";

    public static readonly AreaRows Territories = new(CreatorCatalog.GangTerritoriesFile, "GangTerritory", "ZoneInternalGameName", "GangID", new Dictionary<string, string>());
    public static readonly AreaRows ZoneJurisdictions = new(CreatorCatalog.ZoneJurisdictionsFile, "ZoneJurisdiction", "ZoneInternalGameName", "AgencyID", new Dictionary<string, string> { [PriorityField] = "0" });
    public static readonly AreaRows CountyJurisdictions = new(CreatorCatalog.CountyJurisdictionsFile, "CountyJurisdiction", "CountyID", "AgencyInitials", new Dictionary<string, string> { [PriorityField] = "0" });

    public string FileOf(XElement element) => element.Document is { } document ? fileName(document) : string.Empty;

    public string FileOf(string fileBase) => documents(fileBase).FirstOrDefault() is { } document ? fileName(document) : fileBase + ".xml";

    public IEnumerable<XElement> Entries(EntityKind kind) => Shallow(kind.FileBase, kind.Element);

    public bool EntryExists(EntityKind kind, string id) =>
        kind.IdField is { } field && Entries(kind).Any(e => Same(e.Element(field)?.Value, id));

    public XElement? FindEntry(EntityKind kind, string id) =>
        kind.IdField is { } field ? Entries(kind).FirstOrDefault(e => Same(e.Element(field)?.Value, id)) : null;

    public IEnumerable<XElement> Groups(GroupKind kind) => Shallow(kind.FileBase, kind.Element);

    public XElement? FindGroup(GroupKind kind, string id) =>
        Groups(kind).FirstOrDefault(e => Same(e.Element(kind.IdElement)?.Value, id));

    public bool GroupExists(GroupKind kind, string id) => FindGroup(kind, id) is not null;

    public static int GroupSize(XElement group) =>
        group.Elements().Select(e => e.Elements().Count()).DefaultIfEmpty(0).Max();

    public IReadOnlyList<string> Values(EntityKind kind, string field) =>
        [.. Entries(kind).Select(e => e.Element(field)?.Value.Trim() ?? string.Empty)
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<(string Prefix, string Color)> Colors() =>
        [.. Entries(CreatorCatalog.Gang).Concat(Entries(CreatorCatalog.Agency))
            .Select(e => (Prefix: e.Element(CreatorCatalog.ColorPrefixField)?.Value.Trim() ?? string.Empty, Color: e.Element(CreatorCatalog.ColorField)?.Value.Trim() ?? string.Empty))
            .Where(c => c.Color.Length > 0)
            .DistinctBy(c => c.Color, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c.Color, StringComparer.CurrentCultureIgnoreCase)];

    public ILookup<string, string> Owners(AreaRows rows) =>
        Rows(rows).ToLookup(r => r.Element(rows.KeyField)?.Value.Trim() ?? string.Empty, r => r.Element(rows.OwnerField)?.Value.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Owned(AreaRows rows, string owner) =>
        [.. Rows(rows).Where(r => Same(r.Element(rows.OwnerField)?.Value, owner)).Select(r => r.Element(rows.KeyField)?.Value.Trim() ?? string.Empty).Distinct(StringComparer.OrdinalIgnoreCase)];

    public XElement CopyGroup(GroupKind kind, string sourceId, string newId)
    {
        var source = FindGroup(kind, sourceId) ?? throw new InvalidOperationException($"{sourceId} not found");
        var copy = XmlEditing.CloneAfter(source);
        SetChild(copy, kind.IdElement, newId);
        return copy;
    }

    public static void Insert(XElement template, XElement entry) => XmlEditing.Append(template.Parent!, entry, template.Name);

    public int AddRows(AreaRows rows, string owner, string templateOwner, IEnumerable<string> keys)
    {
        var existing = Rows(rows).ToList();
        var template = existing.FirstOrDefault(e => Same(e.Element(rows.OwnerField)?.Value, templateOwner)) ?? existing.FirstOrDefault();
        if (template is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var key in keys)
        {
            var row = new XElement(template);
            XmlEditing.Append(template.Parent!, row, template.Name);
            SetChild(row, rows.KeyField, key);
            SetChild(row, rows.OwnerField, owner);
            foreach (var (field, value) in rows.Defaults)
            {
                SetChild(row, field, value);
            }

            count++;
        }

        return count;
    }

    public static IReadOnlyList<string> ListValues(XElement entry, string field) =>
        [.. entry.Element(field)?.Elements().Select(e => e.Value.Trim()).Where(v => v.Length > 0) ?? []];

    public static void SetList(XElement entry, string field, IEnumerable<string> values)
    {
        if (entry.Element(field) is not { } list)
        {
            return;
        }

        var wanted = values.ToList();
        if (ListValues(entry, field).SequenceEqual(wanted, StringComparer.Ordinal))
        {
            return;
        }

        var (outer, inner, newline) = Indents(list);
        list.RemoveNodes();
        foreach (var value in wanted)
        {
            list.Add(new XText(newline + inner), new XElement(ListItem, value));
        }

        if (wanted.Count > 0)
        {
            list.Add(new XText(newline + outer));
        }
    }

    public static void AddToList(XElement entry, string field, string value)
    {
        var current = ListValues(entry, field);
        if (!current.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            SetList(entry, field, [.. current, value]);
        }
    }

    public static void SetChild(XElement parent, string name, string value)
    {
        if (parent.Element(name) is { HasElements: false } child)
        {
            XmlEditing.SetValue(child, value);
        }
    }

    private IEnumerable<XElement> Shallow(string fileBase, string element) =>
        documents(fileBase)
            .SelectMany(d => d.Root?.Elements() ?? [])
            .SelectMany(c => c.Name.LocalName == element ? Enumerable.Repeat(c, 1) : c.Elements(element));

    private IEnumerable<XElement> Rows(AreaRows rows) => documents(rows.FileBase).Take(1).SelectMany(d => d.Root?.Elements(rows.Element) ?? []);

    private static (string Outer, string Inner, string Newline) Indents(XElement list)
    {
        var whitespace = list.PreviousNode is XText { Value: var text } && string.IsNullOrWhiteSpace(text) ? text : "\n";
        var newline = whitespace.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var outer = whitespace[(whitespace.LastIndexOf('\n') + 1)..];
        var step = list.Parent?.PreviousNode is XText { Value: var parentText } && string.IsNullOrWhiteSpace(parentText)
            ? outer.Length - parentText[(parentText.LastIndexOf('\n') + 1)..].Length
            : 2;
        return (outer, outer + new string(' ', Math.Max(step, 1)), newline);
    }

    private static bool Same(string? value, string id) => string.Equals(value?.Trim(), id, StringComparison.OrdinalIgnoreCase);
}
