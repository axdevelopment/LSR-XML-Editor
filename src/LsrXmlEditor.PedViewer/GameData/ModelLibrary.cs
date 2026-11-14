using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace LsrXmlEditor.PedViewer.GameData;

public sealed class ModelLibrary
{
    private const int MaxLoadedDictionaries = 48;

    private static readonly ConditionalWeakTable<GameArchive, ModelLibrary> Libraries = new();

    private readonly GameArchive _archive;
    private readonly Lazy<Dictionary<string, string>> _weapons;
    private readonly Lazy<Dictionary<uint, uint>> _txdParents;
    private readonly Lazy<Dictionary<uint, uint>> _archetypeTxds;
    private readonly Lazy<Dictionary<uint, string>> _dictionaries;
    private readonly ConcurrentDictionary<uint, TextureDictionary?> _loaded = new();

    private ModelLibrary(GameArchive archive)
    {
        _archive = archive;
        _weapons = new(ReadWeapons);
        _txdParents = new(ReadTxdParents);
        _archetypeTxds = new(ReadArchetypeTxds);
        _dictionaries = new(IndexDictionaries);
    }

    public static ModelLibrary For(GameArchive archive) => Libraries.GetValue(archive, a => new ModelLibrary(a));

    public string? WeaponModel(string weapon) =>
        _weapons.Value.TryGetValue(weapon, out var model) ? model : null;

    public uint? ArchetypeTextureDictionary(string model) =>
        _archetypeTxds.Value.TryGetValue(JenkHash.GenHash(model.ToLowerInvariant()), out var txd) && txd != 0 ? txd : null;

    public uint? ParentDictionary(uint dictionary) =>
        _txdParents.Value.TryGetValue(dictionary, out var parent) ? parent : null;

    public TextureDictionary? Dictionary(uint hash)
    {
        if (_loaded.TryGetValue(hash, out var cached))
        {
            return cached;
        }

        if (_loaded.Count >= MaxLoadedDictionaries)
        {
            _loaded.Clear();
        }

        var dictionary = _dictionaries.Value.TryGetValue(hash, out var path) ? _archive.Load<YtdFile>(path)?.TextureDict : null;
        _loaded[hash] = dictionary;
        return dictionary;
    }

    private Dictionary<string, string> ReadWeapons()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = _archive.Paths
            .Where(p => p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && GameArchive.FileName(p).StartsWith("weapon", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => _archive.Rank(p).Precedence);
        foreach (var path in files)
        {
            var text = _archive.ReadText(path);
            if (string.IsNullOrEmpty(text) || !text.Contains("CWeaponInfo", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                foreach (var item in XDocument.Parse(text).Descendants("Item").Where(i => (string?)i.Attribute("type") == "CWeaponInfo"))
                {
                    var name = item.Element("Name")?.Value.Trim();
                    var model = item.Element("Model")?.Value.Trim();
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(model))
                    {
                        result[name] = model;
                    }
                }
            }
            catch (XmlException)
            {
            }
        }

        return result;
    }

    private Dictionary<uint, uint> ReadTxdParents()
    {
        var result = new Dictionary<uint, uint>();
        foreach (var path in _archive.Paths)
        {
            var name = GameArchive.FileName(path);
            try
            {
                var relationships = name switch
                {
                    "gtxd.ymt" or "gtxd.meta" or "mph4_gtxd.ymt" => _archive.Load<GtxdFile>(path)?.TxdRelationships,
                    "vehicles.meta" => _archive.Load<VehiclesFile>(path)?.TxdRelationships,
                    _ => null
                };

                foreach (var (child, parent) in relationships ?? [])
                {
                    result.TryAdd(JenkHash.GenHash(child.ToLowerInvariant()), JenkHash.GenHash(parent.ToLowerInvariant()));
                }
            }
            catch (Exception)
            {
            }
        }

        return result;
    }

    private Dictionary<uint, uint> ReadArchetypeTxds()
    {
        var result = new Dictionary<uint, uint>();
        foreach (var path in _archive.Paths.Where(p => p.EndsWith(".ytyp", StringComparison.Ordinal)))
        {
            try
            {
                foreach (var archetype in _archive.Load<YtypFile>(path)?.AllArchetypes ?? [])
                {
                    result[archetype._BaseArchetypeDef.assetName] = archetype._BaseArchetypeDef.textureDictionary;
                }
            }
            catch (Exception)
            {
            }
        }

        return result;
    }

    private Dictionary<uint, string> IndexDictionaries()
    {
        var best = new Dictionary<uint, (string Path, long Precedence)>();
        foreach (var path in _archive.Paths.Where(p => p.EndsWith(".ytd", StringComparison.Ordinal)))
        {
            var hash = JenkHash.GenHash(Path.GetFileNameWithoutExtension(GameArchive.FileName(path)).ToLowerInvariant());
            var precedence = _archive.Rank(path).Precedence;
            if (!best.TryGetValue(hash, out var current) || precedence > current.Precedence)
            {
                best[hash] = (path, precedence);
            }
        }

        return best.ToDictionary(x => x.Key, x => x.Value.Path);
    }
}
