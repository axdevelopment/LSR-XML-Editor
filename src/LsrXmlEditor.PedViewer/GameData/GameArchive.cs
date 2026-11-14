using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace LsrXmlEditor.PedViewer.GameData;

public sealed class GameArchive
{
    private const long PatchOffset = 100_000_000;
    private const long UpdatePrecedence = 50_000_000;
    private const char Separator = '\\';
    private const char ByteOrderMark = (char)0xFEFF;
    private const long UnknownPack = PatchOffset * 9;

    private static readonly string[] PackMarkers = [@"\dlcpacks\", @"\dlc_patch\"];
    private static readonly SemaphoreSlim OpenGate = new(1, 1);
    private static GameArchive? _shared;
    private static int _generation;

    private readonly Lock _readGate = new();
    private readonly Dictionary<string, long> _packOrder;
    private readonly Lazy<Dictionary<string, string>> _files;

    private GameArchive(string folder, RpfManager manager, Dictionary<string, long> packOrder)
    {
        Folder = folder;
        Manager = manager;
        _packOrder = packOrder;
        _files = new Lazy<Dictionary<string, string>>(IndexFiles);
    }

    public string Folder { get; }

    public RpfManager Manager { get; }

    public IEnumerable<string> Paths => Manager.EntryDict.Keys;

    public static async Task<GameArchive> OpenAsync(string gtaFolder, IProgress<string>? progress)
    {
        await OpenGate.WaitAsync();
        try
        {
            if (_shared is not null && string.Equals(_shared.Folder, gtaFolder, StringComparison.OrdinalIgnoreCase))
            {
                return _shared;
            }

            var generation = Volatile.Read(ref _generation);
            var archive = await Task.Run(() => Open(gtaFolder, progress));
            if (generation == Volatile.Read(ref _generation))
            {
                _shared = archive;
            }

            return archive;
        }
        finally
        {
            OpenGate.Release();
        }
    }

    public static void Release()
    {
        Interlocked.Increment(ref _generation);
        _shared = null;
        JenkIndex.Index = [];
    }

    public static bool LooksLikeGtaFolder(string folder) =>
        File.Exists(Path.Combine(folder, "GTA5.exe")) && File.Exists(Path.Combine(folder, "x64v.rpf"));

    public bool Exists(string path) => Manager.EntryDict.ContainsKey(path);

    public T? Load<T>(string path) where T : class, PackedFile, new()
    {
        lock (_readGate)
        {
            return Manager.GetFile<T>(path);
        }
    }

    public long? PackPosition(string pack) => _packOrder.TryGetValue(pack, out var order) ? order : null;

    public string? FindFile(string fileName) => _files.Value.TryGetValue(fileName, out var path) ? path : null;

    public string? ReadText(string path)
    {
        lock (_readGate)
        {
            return Manager.GetFileUTF8Text(path)?.TrimStart(ByteOrderMark);
        }
    }

    public (long Position, long Precedence) Rank(string path)
    {
        var pack = PackOf(path);
        if (pack is not null)
        {
            var key = _packOrder.TryGetValue(pack, out var order) ? order : UnknownPack;
            return (key, key);
        }

        return path.StartsWith("update", StringComparison.Ordinal) ? (-1, UpdatePrecedence) : (-1, -1);
    }

    private Dictionary<string, string> IndexFiles()
    {
        var best = new Dictionary<string, (string Path, long Precedence)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Manager.EntryDict.Keys)
        {
            var name = FileName(path);
            var precedence = Rank(path).Precedence;
            if (!best.TryGetValue(name, out var current) || precedence > current.Precedence)
            {
                best[name] = (path, precedence);
            }
        }

        return best.ToDictionary(x => x.Key, x => x.Value.Path, StringComparer.OrdinalIgnoreCase);
    }

    private static GameArchive Open(string folder, IProgress<string>? progress)
    {
        progress?.Report("Reading GTA V keys …");
        GTA5Keys.LoadFromPath(folder, false, null);

        progress?.Report("Indexing GTA V archives …");
        var manager = new RpfManager { BuildExtendedJenkIndex = false };
        manager.Init(folder, false, status => progress?.Report(status), _ => { });

        return new GameArchive(folder, manager, ReadPackOrder(manager));
    }

    private static Dictionary<string, long> ReadPackOrder(RpfManager manager)
    {
        var setups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in manager.EntryDict.Keys.Where(k => k.EndsWith(Separator + "setup2.xml", StringComparison.Ordinal)))
        {
            if (PackOf(path) is { } owner)
            {
                setups.TryAdd(owner, path);
            }
        }

        var list = manager.GetFileUTF8Text(@"update\update.rpf\common\data\dlclist.xml") ?? string.Empty;
        var packs = Regex.Matches(list, "dlcpacks:?/([^/<]+)/", RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < packs.Count; index++)
        {
            var pack = packs[index];
            var setupOrder = setups.TryGetValue(pack, out var setup) ? ReadSetupOrder(manager, setup) : 0;
            var patch = pack.StartsWith("patch", StringComparison.Ordinal) ? PatchOffset : 0;
            result[pack] = patch + setupOrder * 1000 + index;
        }

        return result;
    }

    private static long ReadSetupOrder(RpfManager manager, string path)
    {
        var text = manager.GetFileUTF8Text(path)?.TrimStart(ByteOrderMark);
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            var value = XDocument.Parse(text).Root?.Element("order")?.Attribute("value")?.Value;
            return long.TryParse(value, out var order) ? order : 0;
        }
        catch (XmlException)
        {
            return 0;
        }
    }

    public static string FileName(string path) => path[(path.LastIndexOf(Separator) + 1)..];

    private static string? PackOf(string path)
    {
        var start = -1;
        foreach (var marker in PackMarkers)
        {
            var index = path.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && (start < 0 || index + marker.Length < start))
            {
                start = index + marker.Length;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var end = path.IndexOf(Separator, start);
        return end < 0 ? null : path[start..end];
    }
}
