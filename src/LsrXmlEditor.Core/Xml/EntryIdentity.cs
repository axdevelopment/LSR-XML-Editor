using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public static class EntryIdentity
{
    private const int MaxSubtitleLength = 120;
    private const string Ellipsis = "...";

    private static readonly string[] KeyFields =
    [
        "ID", "DispatchableVehicleGroupID", "DispatchablePersonGroupID", "HeadDataGroupID", "InternalGameName",
        "ModItemName", "ModelName", "Name", "PlayerName", "FullName", "DisplayName"
    ];

    private static readonly string[] SubtitleFields = ["FullName", "Name", "DisplayName", "Description", "ItemType", "TypeName"];

    public static (string? Field, string Value) Identify(XElement entry)
    {
        if (!entry.HasElements)
        {
            return (null, entry.Value);
        }

        foreach (var field in KeyFields)
        {
            var child = entry.Element(field);
            if (child is { HasElements: false } && child.Value.Length > 0)
            {
                return (field, child.Value);
            }
        }

        foreach (var child in entry.Elements())
        {
            if (!child.HasElements && child.Value.Length > 0 && child.Name.LocalName.EndsWith("ID", StringComparison.Ordinal))
            {
                return (child.Name.LocalName, child.Value);
            }
        }

        return (null, string.Empty);
    }

    public static string KeyOf(XElement entry, bool isRecord)
    {
        if (isRecord)
        {
            return "<" + entry.Name.LocalName;
        }

        var (field, value) = Identify(entry);
        if (field is not null)
        {
            return field + "=" + value;
        }

        return entry.HasElements ? "#" : "=" + value;
    }

    public static string TitleOf(XElement entry)
    {
        var (field, value) = Identify(entry);
        if (field is not null || !entry.HasElements)
        {
            return value.Length > 0 ? value : "(empty)";
        }

        return entry.Name.LocalName;
    }

    public static string SubtitleOf(XElement entry, string title)
    {
        if (!entry.HasElements)
        {
            return entry.Name.LocalName;
        }

        foreach (var field in SubtitleFields)
        {
            var value = entry.Element(field)?.Value;
            if (!string.IsNullOrEmpty(value) && value != title)
            {
                return value.Length > MaxSubtitleLength ? value[..(MaxSubtitleLength - Ellipsis.Length)] + Ellipsis : value;
            }
        }

        return entry.Name.LocalName;
    }
}
