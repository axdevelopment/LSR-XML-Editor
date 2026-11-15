using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Services;

public sealed record EntryLocation(string FilePath, string? CategoryPath, int Index);

public sealed class CreatorSession
{
    private const string ZoneElement = "Zone";
    private const string ZoneCodeField = "InternalGameName";
    private const string ZoneNameField = "DisplayName";
    private const string CountyField = "CountyID";
    private const string SaveElement = "GameSave";

    private static readonly string[] Files =
    [
        CreatorCatalog.Gang.FileBase,
        CreatorCatalog.Agency.FileBase,
        CreatorCatalog.LocationsFile,
        CreatorCatalog.ShopMenu.FileBase,
        CreatorCatalog.Peds.FileBase,
        CreatorCatalog.Vehicles.FileBase,
        CreatorCatalog.Weapons.FileBase,
        CreatorCatalog.Heads.FileBase,
        CreatorCatalog.ZonesFile,
        CreatorCatalog.GangTerritoriesFile,
        CreatorCatalog.ZoneJurisdictionsFile,
        CreatorCatalog.CountyJurisdictionsFile,
        CreatorCatalog.SaveGamesFile,
        CreatorCatalog.ModItemsFile,
        CreatorCatalog.InteriorsFile,
        CreatorCatalog.Organization.FileBase
    ];

    private readonly Dictionary<string, List<XmlConfigDocument>> _documents;
    private readonly Dictionary<XDocument, string> _names = [];

    private CreatorSession(Dictionary<string, List<XmlConfigDocument>> documents)
    {
        _documents = documents;
        foreach (var document in documents.Values.SelectMany(d => d))
        {
            _names[document.Document] = Path.GetFileName(document.FilePath);
        }

        Engine = new CreatorEngine(Documents, d => _names.GetValueOrDefault(d, string.Empty));
        Data = new CreatorData(LocationTypes(), Zones(), SavedPositions());
    }

    public CreatorEngine Engine { get; }

    public CreatorData Data { get; }

    public static async Task<CreatorSession> LoadAsync(Workspace workspace)
    {
        var documents = new Dictionary<string, List<XmlConfigDocument>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Files)
        {
            var files = workspace.Files
                .Where(f => string.Equals(f.File.BaseName, name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.File.IsVariant)
                .ThenBy(f => !f.File.IsInRootFolder)
                .ToList();
            var loaded = new List<XmlConfigDocument>();
            foreach (var file in files)
            {
                loaded.Add(await workspace.LoadAsync(file.FullPath));
            }

            documents[name] = loaded;
        }

        return new CreatorSession(documents);
    }

    public EntryLocation? Locate(XElement entry)
    {
        var document = _documents.Values.SelectMany(d => d).FirstOrDefault(d => ReferenceEquals(d.Document, entry.Document));
        if (document is null)
        {
            return null;
        }

        var categories = document.Categories;
        foreach (var category in categories)
        {
            var index = category.Entries.TakeWhile(e => !ReferenceEquals(e, entry)).Count();
            if (index < category.Count)
            {
                return new EntryLocation(document.FilePath, categories.Count > 1 ? category.Path : null, index);
            }
        }

        return new EntryLocation(document.FilePath, null, -1);
    }

    private IReadOnlyList<XDocument> Documents(string fileBase) =>
        _documents.TryGetValue(fileBase, out var list) ? [.. list.Select(d => d.Document)] : [];

    private IEnumerable<XElement> FirstRoot(string fileBase) =>
        Documents(fileBase).Take(1).SelectMany(d => d.Root?.Elements() ?? []);

    private IReadOnlyList<LocationType> LocationTypes() =>
        [.. FirstRoot(CreatorCatalog.LocationsFile)
            .Where(c => c.Name.LocalName != CreatorCatalog.GangDensContainer && c.Elements().Any(e => e.Element(CreatorCatalog.NameField) is not null))
            .Select(c => new LocationType(c.Name.LocalName, c.Elements().First().Name.LocalName, DisplayNames.Humanize(c.Name.LocalName), c.Elements().Count()))
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)];

    private IReadOnlyList<ZoneInfo> Zones() =>
        [.. FirstRoot(CreatorCatalog.ZonesFile)
            .Where(z => z.Name.LocalName == ZoneElement)
            .Select(z => new ZoneInfo(Text(z, ZoneCodeField), Text(z, ZoneNameField), Text(z, CountyField)))
            .Where(z => z.Code.Length > 0)
            .DistinctBy(z => z.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase)];

    private IReadOnlyList<SavedPosition> SavedPositions() =>
        [.. FirstRoot(CreatorCatalog.SaveGamesFile)
            .Where(s => s.Name.LocalName == SaveElement)
            .Select(Saved)
            .OfType<SavedPosition>()];

    private static SavedPosition? Saved(XElement save)
    {
        var position = save.Element("PlayerPosition");
        if (position is null || !TryNumber(position, "X", out var x) || !TryNumber(position, "Y", out var y) || !TryNumber(position, "Z", out var z))
        {
            return null;
        }

        TryNumber(save, "PlayerHeading", out var heading);
        var title = $"Save {Text(save, "SaveNumber")}";
        if (DateTime.TryParse(Text(save, "SaveDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            title += $" ({date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)})";
        }

        return new SavedPosition(title, new Placement(x, y, z, heading));
    }

    private static bool TryNumber(XElement parent, string name, out double value) =>
        double.TryParse(parent.Element(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Text(XElement parent, string name) => parent.Element(name)?.Value.Trim() ?? string.Empty;
}
