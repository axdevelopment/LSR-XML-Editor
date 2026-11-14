using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace LsrXmlEditor.PedViewer.GameData;

public enum DecorationZone
{
    Torso,
    Head,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}

public enum DecorationGender
{
    Any,
    Male,
    Female
}

public sealed record Decoration(
    string Collection,
    string Name,
    string Label,
    string TextureDictionary,
    string Texture,
    DecorationZone Zone,
    DecorationGender Gender,
    float X,
    float Y,
    float ScaleX,
    float ScaleY,
    float Rotation,
    double Facing,
    bool IsTattoo,
    bool IsHairOverlay);

public sealed class TattooCatalog
{
    private const string OverlaySuffix = "_overlays.xml";
    private const string ShopFile = "shop_tattoo.meta";
    private const string LabelFile = "global.gxt2";
    private const string LabelFolder = "american";
    private const string HairGroup = "hairOverlay";
    private const string HairMarker = "_hair_";

    private static readonly ConditionalWeakTable<GameArchive, TattooCatalog> Catalogs = new();

    private static readonly string[] ZoneNames = ["ZONE_TORSO", "ZONE_HEAD", "ZONE_LEFT_ARM", "ZONE_RIGHT_ARM", "ZONE_LEFT_LEG", "ZONE_RIGHT_LEG"];

    private static readonly Dictionary<string, double> Facings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TATTOO_FRONT"] = 0,
        ["TATTOO_BACK"] = Math.PI,
        ["TATTOO_LEFT"] = Math.PI / 2,
        ["TATTOO_RIGHT"] = -Math.PI / 2,
        ["TATTOO_FRONT_LEFT"] = Math.PI / 4,
        ["TATTOO_FRONT_RIGHT"] = -Math.PI / 4,
        ["TATTOO_BACK_LEFT"] = 3 * Math.PI / 4,
        ["TATTOO_BACK_RIGHT"] = -3 * Math.PI / 4
    };

    private static readonly double[] ZoneFacings = [0, 0, Math.PI / 2, -Math.PI / 2, 0, 0];

    private readonly Dictionary<AppliedOverlay, Decoration> _byName = new(OverlayComparer.Instance);

    private TattooCatalog(GameArchive archive)
    {
        var shop = ReadShop(archive);
        var labels = ReadLabels(archive);
        foreach (var decoration in ReadDecorations(archive, shop, labels))
        {
            _byName[new AppliedOverlay(decoration.Collection, decoration.Name)] = decoration;
        }

        All = [.. _byName.Values.OrderBy(d => d.Zone).ThenBy(d => d.Label, StringComparer.CurrentCultureIgnoreCase)];
    }

    public IReadOnlyList<Decoration> All { get; }

    public static TattooCatalog For(GameArchive archive) => Catalogs.GetValue(archive, a => new TattooCatalog(a));

    public static DecorationGender GenderOf(string pedModel) =>
        pedModel.StartsWith("mp_f_", StringComparison.OrdinalIgnoreCase) ? DecorationGender.Female
        : pedModel.StartsWith("mp_m_", StringComparison.OrdinalIgnoreCase) ? DecorationGender.Male
        : DecorationGender.Any;

    public Decoration? Find(string collection, string name) =>
        _byName.TryGetValue(new AppliedOverlay(collection, name), out var decoration) ? decoration : null;

    private static IEnumerable<Decoration> ReadDecorations(GameArchive archive, Dictionary<string, ShopEntry> shop, Dictionary<uint, string> labels)
    {
        var files = archive.Paths
            .Where(p => p.EndsWith(OverlaySuffix, StringComparison.OrdinalIgnoreCase))
            .Select(GameArchive.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var collection = Path.GetFileNameWithoutExtension(file);
            foreach (var item in Items(archive, archive.FindFile(file)))
            {
                var zone = Array.IndexOf(ZoneNames, item.Element("zone")?.Value);
                var name = item.Element("nameHash")?.Value.Trim();
                if (zone < 0 || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                shop.TryGetValue(name, out var entry);
                var label = entry is not null && labels.TryGetValue(JenkHash.GenHash(entry.Label.ToLowerInvariant()), out var text) ? text : name;
                var texture = item.Element("txtHash")?.Value.Trim() ?? name;
                yield return new Decoration(
                    string.IsNullOrEmpty(entry?.Collection) ? collection : entry.Collection,
                    name,
                    label,
                    item.Element("txdHash")?.Value.Trim() ?? texture,
                    texture,
                    (DecorationZone)zone,
                    Gender(item.Element("gender")?.Value),
                    Attribute(item, "uvPos", "x"),
                    Attribute(item, "uvPos", "y"),
                    Attribute(item, "scale", "x"),
                    Attribute(item, "scale", "y"),
                    Attribute(item, "rotation", "value"),
                    entry is not null && Facings.TryGetValue(entry.Facing, out var facing) ? facing : ZoneFacings[zone],
                    item.Element("type")?.Value == "TYPE_TATTOO",
                    string.Equals(entry?.Group, HairGroup, StringComparison.OrdinalIgnoreCase) || name.Contains(HairMarker, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    private static Dictionary<string, ShopEntry> ReadShop(GameArchive archive)
    {
        var result = new Dictionary<string, ShopEntry>(StringComparer.OrdinalIgnoreCase);
        var files = archive.Paths
            .Where(p => p.EndsWith(ShopFile, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => archive.Rank(p).Precedence);
        foreach (var path in files)
        {
            foreach (var item in Items(archive, path))
            {
                var preset = item.Element("preset")?.Value.Trim();
                if (!string.IsNullOrEmpty(preset))
                {
                    result[preset] = new ShopEntry(
                        item.Element("collection")?.Value.Trim() ?? string.Empty,
                        item.Element("textLabel")?.Value.Trim() ?? string.Empty,
                        item.Element("eFacing")?.Value.Trim() ?? string.Empty,
                        item.Element("updateGroup")?.Value.Trim() ?? string.Empty);
                }
            }
        }

        return result;
    }

    private static Dictionary<uint, string> ReadLabels(GameArchive archive)
    {
        var result = new Dictionary<uint, string>();
        var files = archive.Paths
            .Where(p => p.EndsWith(LabelFile, StringComparison.OrdinalIgnoreCase) && p.Contains(LabelFolder, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => archive.Rank(p).Precedence);
        foreach (var path in files)
        {
            foreach (var entry in archive.Load<Gxt2File>(path)?.TextEntries ?? [])
            {
                result[entry.Hash] = entry.Text;
            }
        }

        return result;
    }

    private static IEnumerable<XElement> Items(GameArchive archive, string? path)
    {
        var text = path is null ? null : archive.ReadText(path);
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        try
        {
            return XDocument.Parse(text).Descendants("Item").ToList();
        }
        catch (XmlException)
        {
            return [];
        }
    }

    private static DecorationGender Gender(string? value) => value switch
    {
        "GENDER_MALE" => DecorationGender.Male,
        "GENDER_FEMALE" => DecorationGender.Female,
        _ => DecorationGender.Any
    };

    private static float Attribute(XElement item, string element, string attribute) =>
        float.TryParse(item.Element(element)?.Attribute(attribute)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private sealed record ShopEntry(string Collection, string Label, string Facing, string Group);
}
