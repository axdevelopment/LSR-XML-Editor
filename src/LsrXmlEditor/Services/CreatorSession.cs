using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Xml;
using LsrXmlEditor.Forms;

namespace LsrXmlEditor.Services;

public sealed record EntryLocation(string FilePath, string? CategoryPath, int Index);

public sealed class CreatorSession
{
    private const string ZoneElement = "Zone";
    private const string ZoneCodeField = "InternalGameName";
    private const string ZoneNameField = "DisplayName";
    private const string CountyField = "CountyID";
    private const string GangDensContainer = "GangDens";

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
        CreatorCatalog.CountyJurisdictionsFile
    ];

    private readonly Dictionary<string, List<XmlConfigDocument>> _documents;

    private CreatorSession(Dictionary<string, List<XmlConfigDocument>> documents)
    {
        _documents = documents;
        Engine = new CreatorEngine(Documents);
        Data = new CreatorData(LocationTypes(), Zones(), Counties());
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

    private IReadOnlyList<LocationType> LocationTypes() =>
        [.. Documents(CreatorCatalog.LocationsFile).Take(1)
            .SelectMany(d => d.Root?.Elements() ?? [])
            .Where(c => c.Name.LocalName != GangDensContainer && c.Elements().Any())
            .Select(c => new LocationType(c.Name.LocalName, c.Elements().First().Name.LocalName, DisplayNames.Humanize(c.Name.LocalName)))
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)];

    private IReadOnlyList<(string Code, string Name)> Zones() =>
        [.. Documents(CreatorCatalog.ZonesFile).Take(1)
            .SelectMany(d => d.Root?.Elements(ZoneElement) ?? [])
            .Select(z => (Code: z.Element(ZoneCodeField)?.Value.Trim() ?? string.Empty, Name: z.Element(ZoneNameField)?.Value.Trim() ?? string.Empty))
            .Where(z => z.Code.Length > 0)
            .OrderBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase)];

    private IReadOnlyList<string> Counties() =>
        [.. Documents(CreatorCatalog.ZonesFile).Take(1)
            .SelectMany(d => d.Root?.Elements(ZoneElement) ?? [])
            .Select(z => z.Element(CountyField)?.Value.Trim() ?? string.Empty)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase)];
}
