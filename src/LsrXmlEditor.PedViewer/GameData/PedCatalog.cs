using CodeWalker.GameFiles;

namespace LsrXmlEditor.PedViewer.GameData;

public sealed record PedCollection(string Name, IReadOnlyList<string> YmtPaths, MCPedVariationInfo Variation, int[] Start, int[] Count);

public sealed record ComponentPart(
    int Component,
    int GlobalDrawable,
    int Texture,
    string Collection,
    int LocalDrawable,
    string DrawableName,
    string TextureName,
    int TextureCount,
    Drawable? Drawable,
    Texture? DiffuseTexture);

public sealed class PedCatalog
{
    public const int ComponentCount = 12;

    private const string RepackagedPack = @"\mppatchesng\";

    public static readonly string[] ComponentNames =
    [
        "Head", "Mask", "Hair", "Torso", "Legs", "Bag / Parachute", "Shoes", "Accessories", "Undershirt", "Body Armor", "Decals", "Tops"
    ];

    private readonly GameArchive _archive;
    private readonly IReadOnlyList<PedCollection> _collections;
    private readonly Lazy<YddFile?> _modelDrawables;
    private readonly Lazy<YtdFile?> _modelTextures;

    private PedCatalog(GameArchive archive, string model, IReadOnlyList<PedCollection> collections, string? modelDrawables, string? modelTextures)
    {
        _archive = archive;
        Model = model;
        _collections = collections;
        _modelDrawables = new(() => modelDrawables is null ? null : archive.Load<YddFile>(modelDrawables));
        _modelTextures = new(() => modelTextures is null ? null : archive.Load<YtdFile>(modelTextures));
        Totals = Enumerable.Range(0, ComponentCount).Select(c => collections.Sum(x => x.Count[c])).ToArray();
    }

    public string Model { get; }

    public int[] Totals { get; }

    public int CollectionCount => _collections.Count;

    public static PedCatalog? Build(GameArchive archive, string model)
    {
        model = model.ToLowerInvariant();
        var prefix = model + "_";
        var versions = archive.Paths
            .Where(p => p.EndsWith(".ymt", StringComparison.Ordinal))
            .Select(p => (Path: p, Name: Path.GetFileNameWithoutExtension(p)))
            .Where(x => x.Name == model || (x.Name.StartsWith(prefix, StringComparison.Ordinal) && !x.Name.EndsWith("_p", StringComparison.Ordinal)))
            .GroupBy(x => x.Name)
            .Select(g => (Name: g.Key, Paths: g.Select(x => x.Path).OrderByDescending(p => archive.Rank(p).Precedence).ToList()))
            .OrderBy(g => Position(archive, g.Name, g.Paths, prefix))
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToList();

        var collections = new List<PedCollection>();
        var totals = new int[ComponentCount];
        foreach (var (name, paths) in versions)
        {
            var variation = paths.Select(p => archive.Load<PedFile>(p)?.VariationInfo).FirstOrDefault(v => v is not null);
            if (variation is null)
            {
                continue;
            }

            var counts = Enumerable.Range(0, ComponentCount).Select(c => variation.GetComponentData(c)?.DrawblData3?.Length ?? 0).ToArray();
            collections.Add(new PedCollection(name, paths, variation, (int[])totals.Clone(), counts));
            for (var c = 0; c < ComponentCount; c++)
            {
                totals[c] += counts[c];
            }
        }

        if (collections.Count == 0)
        {
            return null;
        }

        return new PedCatalog(archive, model, collections, archive.FindFile(model + ".ydd"), archive.FindFile(model + ".ytd"));
    }

    public ComponentPart Resolve(int component, int drawable, int texture)
    {
        var collection = _collections.LastOrDefault(c => drawable >= c.Start[component] && drawable < c.Start[component] + c.Count[component]);
        if (collection is null)
        {
            return new ComponentPart(component, drawable, texture, "-", -1, string.Empty, string.Empty, 0, null, null);
        }

        var local = drawable - collection.Start[component];
        var item = collection.Variation.GetComponentData(component)!.DrawblData3[local];
        item.ComponentType = component;
        var textureCount = Math.Max(1, item.TexData?.Length ?? 1);
        texture = Math.Clamp(texture, 0, textureCount - 1);
        var drawableName = item.GetDrawableName(0);
        var textureName = item.GetTextureName(texture);

        return new ComponentPart(
            component,
            drawable,
            texture,
            collection.Name,
            local,
            drawableName,
            textureName,
            textureCount,
            LoadDrawable(collection, drawableName),
            LoadTexture(collection, textureName));
    }

    private Drawable? LoadDrawable(PedCollection collection, string name)
    {
        foreach (var path in CandidatePaths(collection, name + ".ydd"))
        {
            var ydd = _archive.Load<YddFile>(path);
            if (ydd?.Drawables?.Length > 0)
            {
                return ydd.Drawables[0];
            }
        }

        return _modelDrawables.Value?.Dict is { } dictionary && dictionary.TryGetValue(JenkHash.GenHash(name), out var drawable)
            ? drawable
            : null;
    }

    private Texture? LoadTexture(PedCollection collection, string name)
    {
        foreach (var path in CandidatePaths(collection, name + ".ytd"))
        {
            var ytd = _archive.Load<YtdFile>(path);
            var textures = ytd?.TextureDict?.Textures?.data_items;
            if (textures?.Length > 0)
            {
                return textures[0];
            }
        }

        return _modelTextures.Value?.TextureDict?.Dict is { } dictionary && dictionary.TryGetValue(JenkHash.GenHash(name), out var texture)
            ? texture
            : null;
    }

    private IEnumerable<string> CandidatePaths(PedCollection collection, string file)
    {
        foreach (var ymt in collection.YmtPaths)
        {
            var folder = Path.GetDirectoryName(ymt);
            var path = $"{folder}\\{collection.Name}\\{file}";
            if (_archive.Exists(path))
            {
                yield return path;
            }
        }
    }

    private static long Position(GameArchive archive, string name, IReadOnlyList<string> paths, string prefix)
    {
        var first = paths.MinBy(p => archive.Rank(p).Position)!;
        var own = archive.Rank(first).Position;
        var repackaged = first.Contains(RepackagedPack, StringComparison.Ordinal);
        return repackaged && name.Length > prefix.Length && OriginPosition(archive, name[prefix.Length..]) is { } origin ? origin : own;
    }

    private static long? OriginPosition(GameArchive archive, string collection)
    {
        var token = collection;
        foreach (var prefix in new[] { "female_", "male_", "mp_f_", "mp_m_", "freemode_" })
        {
            if (token.StartsWith(prefix, StringComparison.Ordinal))
            {
                token = token[prefix.Length..];
            }
        }

        token = token.Replace("xmas", "christmas", StringComparison.Ordinal);
        var candidates = new[] { token, token.TrimEnd('_', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9') };
        foreach (var candidate in candidates.Where(c => c.Length > 0).Distinct())
        {
            var pack = candidate.StartsWith("mp", StringComparison.Ordinal) ? candidate : "mp" + candidate;
            if (archive.PackPosition(pack) is { } position)
            {
                return position;
            }
        }

        return null;
    }
}
