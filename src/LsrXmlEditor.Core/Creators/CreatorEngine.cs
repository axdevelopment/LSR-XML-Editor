using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Creators;

public sealed record LinkPlan(LinkField Link, string TemplateValue, bool Copy, string NewId);

public sealed record Placement(double X, double Y, double Z, double Heading);

public sealed record EntryPlan(
    XElement Template,
    string? NewId,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<LinkPlan> Links,
    Placement? Placement);

public sealed class CreatorEngine(Func<string, IReadOnlyList<XDocument>> documents)
{
    private const string PositionElement = "EntrancePosition";
    private const string HeadingElement = "EntranceHeading";
    private const string ZoneField = "ZoneInternalGameName";
    private const string GangField = "GangID";
    private const string AgencyField = "AgencyID";
    private const string CountyField = "CountyID";
    private const string AgencyInitialsField = "AgencyInitials";
    private const string PriorityField = "Priority";

    public XElement? FindGroup(GroupKind kind, string id) =>
        documents(kind.FileBase)
            .SelectMany(d => d.Root?.Descendants(kind.Element) ?? [])
            .FirstOrDefault(e => string.Equals(e.Element(kind.IdElement)?.Value.Trim(), id, StringComparison.OrdinalIgnoreCase));

    public bool GroupExists(GroupKind kind, string id) => FindGroup(kind, id) is not null;

    public bool EntryExists(EntityKind kind, string id) =>
        kind.IdField is { } field && documents(kind.FileBase)
            .SelectMany(d => d.Root?.Descendants(kind.Element) ?? [])
            .Any(e => string.Equals(e.Element(field)?.Value.Trim(), id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<XElement> Entries(EntityKind kind) =>
        documents(kind.FileBase).SelectMany(d => d.Root?.Descendants(kind.Element) ?? []);

    public XElement Create(EntityKind kind, EntryPlan plan)
    {
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var link in plan.Links.Where(l => l.Copy && l.TemplateValue.Length > 0))
        {
            var key = link.Link.Kind.Element + "|" + link.TemplateValue;
            if (copies.ContainsKey(key) || FindGroup(link.Link.Kind, link.TemplateValue) is not { } group)
            {
                continue;
            }

            var copy = XmlEditing.CloneAfter(group);
            SetChild(copy, link.Link.Kind.IdElement, link.NewId);
            copies[key] = link.NewId;
        }

        var entry = new XElement(plan.Template);
        XmlEditing.Append(plan.Template.Parent!, entry, plan.Template.Name);
        if (kind.IdField is { } idField && plan.NewId is { } id)
        {
            SetChild(entry, idField, id);
        }

        foreach (var (field, value) in plan.Values)
        {
            SetChild(entry, field, value);
        }

        foreach (var link in plan.Links)
        {
            SetChild(entry, link.Link.Field, link.Copy ? link.NewId : link.TemplateValue);
        }

        if (plan.Placement is { } placement)
        {
            Place(entry, placement);
        }

        return entry;
    }

    public XElement? CreateDen(XElement templateDen, string gangId, IReadOnlyDictionary<string, string> values, Placement placement, LinkPlan? menu)
    {
        var plan = new EntryPlan(templateDen, null, values, menu is null ? [] : [menu], placement);
        var den = Create(CreatorCatalog.Location(CreatorCatalog.GangDenElement), plan);
        SetChild(den, CreatorCatalog.AssociationField, gangId);
        return den;
    }

    public int AddTerritories(string gangId, IEnumerable<string> zones) =>
        AddRows(CreatorCatalog.GangTerritoriesFile, "GangTerritory", zones, row =>
        {
            SetChild(row.Element, ZoneField, row.Key);
            SetChild(row.Element, GangField, gangId);
        }, _ => true);

    public int AddZoneJurisdictions(string agencyId, string templateAgencyId, IEnumerable<string> zones) =>
        AddRows(CreatorCatalog.ZoneJurisdictionsFile, "ZoneJurisdiction", zones, row =>
        {
            SetChild(row.Element, ZoneField, row.Key);
            SetChild(row.Element, AgencyField, agencyId);
            SetChild(row.Element, PriorityField, "0");
        }, e => e.Element(AgencyField)?.Value == templateAgencyId);

    public int AddCountyJurisdictions(string agencyId, string templateAgencyId, IEnumerable<string> counties) =>
        AddRows(CreatorCatalog.CountyJurisdictionsFile, "CountyJurisdiction", counties, row =>
        {
            SetChild(row.Element, CountyField, row.Key);
            SetChild(row.Element, AgencyInitialsField, agencyId);
            SetChild(row.Element, PriorityField, "0");
        }, e => e.Element(AgencyInitialsField)?.Value == templateAgencyId);

    private int AddRows(string fileBase, string element, IEnumerable<string> keys, Action<(XElement Element, string Key)> fill, Func<XElement, bool> preferred)
    {
        var rows = documents(fileBase).FirstOrDefault()?.Root?.Elements(element).ToList() ?? [];
        var template = rows.FirstOrDefault(preferred) ?? rows.FirstOrDefault();
        if (template is null)
        {
            return 0;
        }

        var count = 0;
        foreach (var key in keys)
        {
            var row = new XElement(template);
            XmlEditing.Append(template.Parent!, row, template.Name);
            fill((row, key));
            count++;
        }

        return count;
    }

    private static void Place(XElement entry, Placement placement)
    {
        if (entry.Element(PositionElement) is { } position)
        {
            SetChild(position, "X", Number(placement.X));
            SetChild(position, "Y", Number(placement.Y));
            SetChild(position, "Z", Number(placement.Z));
        }

        SetChild(entry, HeadingElement, Number(placement.Heading));
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static void SetChild(XElement parent, string name, string value)
    {
        if (parent.Element(name) is { } child && !child.HasElements)
        {
            XmlEditing.SetValue(child, value);
        }
    }
}
