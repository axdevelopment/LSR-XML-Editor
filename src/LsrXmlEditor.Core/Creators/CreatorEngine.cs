using System.Xml.Linq;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Creators;

public sealed record AreaRows(string FileBase, string Element, string KeyField, string OwnerField, IReadOnlyDictionary<string, string> Defaults);

public sealed class CreatorEngine(Func<string, IReadOnlyList<XDocument>> documents, Func<XDocument, string> fileName)
{
    private const string ListItem = "string";
    private const string PriorityField = "Priority";
    private const string MenuItemElement = "MenuItem";
    private const string ItemsSuffix = "Items";
    private const string GroupNameField = "GroupName";
    private const string ContainerGroupField = "ShopMenuGroupID";

    private IReadOnlyList<ModItemInfo>? _items;
    private HashSet<string>? _itemNames;

    public static readonly AreaRows Territories = new(CreatorCatalog.GangTerritoriesFile, "GangTerritory", "ZoneInternalGameName", "GangID", new Dictionary<string, string>());
    public static readonly AreaRows ZoneJurisdictions = new(CreatorCatalog.ZoneJurisdictionsFile, "ZoneJurisdiction", "ZoneInternalGameName", "AgencyID", new Dictionary<string, string> { [PriorityField] = "0" });
    public static readonly AreaRows CountyJurisdictions = new(CreatorCatalog.CountyJurisdictionsFile, "CountyJurisdiction", "CountyID", "AgencyInitials", new Dictionary<string, string> { [PriorityField] = "0" });

    public string FileOf(XElement element) => element.Document is { } document ? fileName(document) : string.Empty;

    public string FileOf(string fileBase) => documents(fileBase).FirstOrDefault() is { } document ? fileName(document) : fileBase + ".xml";

    public string? SourceLabel(XElement element, string fileBase) =>
        FileOf(element) is { Length: > 0 } file && !string.Equals(file, FileOf(fileBase), StringComparison.OrdinalIgnoreCase) ? file : null;

    public IEnumerable<XElement> Entries(EntityKind kind) => Shallow(kind.FileBase, kind.Element);

    public bool EntryExists(EntityKind kind, string id) =>
        kind.IdField is { } field && Entries(kind).Any(e => Same(e.Element(field)?.Value, id));

    public XElement? FindEntry(EntityKind kind, string id) =>
        kind.IdField is { } field ? Entries(kind).LastOrDefault(e => Same(e.Element(field)?.Value, id)) : null;

    public IEnumerable<XElement> Groups(GroupKind kind) => Shallow(kind.FileBase, kind.Element);

    public XElement? FindGroup(GroupKind kind, string id) =>
        Groups(kind).LastOrDefault(e => Same(e.Element(kind.IdElement)?.Value, id));

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

    public XElement CopyGroup(GroupKind kind, string sourceId, string newId, XElement? content = null)
    {
        var source = FindGroup(kind, sourceId) ?? throw new InvalidOperationException($"{sourceId} not found");
        var copy = new XElement(content ?? source);
        SetChild(copy, kind.IdElement, newId);
        foreach (var name in copy.Descendants(GroupNameField).Where(e => !e.HasElements && Same(e.Value, sourceId)))
        {
            XmlEditing.SetValue(name, newId);
        }

        XmlEditing.InsertAfter(source, copy);
        return copy;
    }

    public IReadOnlyList<string> Users(GroupKind kind, string id)
    {
        if (id.Length == 0)
        {
            return [];
        }

        var fields = CreatorCatalog.FieldsFor(kind);
        var organizations = Entries(CreatorCatalog.Gang).Concat(Entries(CreatorCatalog.Agency))
            .Where(e => Uses(e, fields, id))
            .Select(e => e.Element(CreatorCatalog.IdField)?.Value.Trim() ?? string.Empty);
        var places = Locations()
            .Where(e => Uses(e, fields, id))
            .Select(e => e.Element(CreatorCatalog.NameField)?.Value.Trim() ?? string.Empty);
        var containers = kind == CreatorCatalog.ShopMenuGroup
            ? Groups(CreatorCatalog.ShopMenuGroupContainer)
                .Where(c => c.Descendants(ContainerGroupField).Any(g => Same(g.Value, id)))
                .Select(c => $"{c.Element(CreatorCatalog.IdField)?.Value.Trim()} (area dealers)")
            : [];
        return [.. organizations.Concat(places).Concat(containers).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<(string Id, string Title)> OwnerChoices() =>
        [.. CreatorCatalog.Owners.SelectMany(kind => Entries(kind).Select(e => (
                Id: e.Element(CreatorCatalog.IdField)?.Value.Trim() ?? string.Empty,
                Title: $"{e.Element(CreatorCatalog.IdField)?.Value.Trim()} – {e.Element(CreatorCatalog.FullNameField)?.Value.Trim()} ({kind.Title})")))
            .Where(o => o.Id.Length > 0)
            .DistinctBy(o => o.Id, StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<ModItemInfo> ModItems() =>
        _items ??= [.. documents(CreatorCatalog.ModItemsFile)
            .SelectMany(d => d.Root?.Elements() ?? [])
            .SelectMany(c => c.Elements().Select(i => new ModItemInfo(i.Element(CreatorCatalog.NameField)?.Value.Trim() ?? string.Empty, Category(c.Name.LocalName))))
            .Where(i => i.Name.Length > 0)
            .DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)];

    public bool ItemExists(string name) =>
        (_itemNames ??= new HashSet<string>(ModItems().Select(i => i.Name), StringComparer.OrdinalIgnoreCase)).Count == 0 || _itemNames.Contains(name);

    public XElement? MenuItemPattern(string modItem) =>
        documents(CreatorCatalog.ShopMenu.FileBase)
            .SelectMany(d => d.Descendants(MenuItemElement))
            .FirstOrDefault(i => Same(i.Element(MenuEditing.ItemField)?.Value, modItem));

    public IReadOnlyList<string> InteriorAssociations(string interiorId) =>
        [.. documents(CreatorCatalog.InteriorsFile)
            .SelectMany(d => d.Root?.Elements() ?? [])
            .SelectMany(c => c.Elements())
            .Where(i => Same(i.Element(CreatorCatalog.InteriorIdField)?.Value, interiorId))
            .SelectMany(i => i.Descendants(CreatorCatalog.SpawnAssociationField))
            .Select(a => a.Value.Trim())
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    public static void Insert(XElement template, XElement entry) => XmlEditing.Append(template.Parent!, entry, template.Name);

    public string RowsFile(AreaRows rows, string templateOwner) =>
        RowTemplate(rows, templateOwner) is { } template ? FileOf(template) : FileOf(rows.FileBase);

    public int AddRows(AreaRows rows, string owner, string templateOwner, IEnumerable<string> keys)
    {
        if (RowTemplate(rows, templateOwner) is not { } template)
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

    private XElement? RowTemplate(AreaRows rows, string templateOwner)
    {
        var existing = Rows(rows).ToList();
        return existing.FirstOrDefault(e => Same(e.Element(rows.OwnerField)?.Value, templateOwner)) ?? existing.FirstOrDefault();
    }

    private IEnumerable<XElement> Locations() =>
        documents(CreatorCatalog.LocationsFile).SelectMany(d => d.Root?.Elements() ?? []).SelectMany(c => c.Elements());

    private static bool Uses(XElement entry, IReadOnlyList<string> fields, string id) =>
        fields.Any(f => Same(entry.Element(f)?.Value, id));

    private static string Category(string container) =>
        DisplayNames.Humanize(container.EndsWith(ItemsSuffix, StringComparison.Ordinal) && container.Length > ItemsSuffix.Length ? container[..^ItemsSuffix.Length] : container);

    private IEnumerable<XElement> Rows(AreaRows rows) => documents(rows.FileBase).SelectMany(d => d.Root?.Elements(rows.Element) ?? []);

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
