using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Changes;

internal sealed class EntryIndex
{
    private const char OccurrenceMark = '~';
    private const string KeylessBase = "#";
    private const int SignatureLimit = 40;

    private readonly Dictionary<string, List<XElement>> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<XElement, string> _keys = [];

    public EntryIndex(EntryCategory category)
    {
        Category = category;
        foreach (var entry in category.Entries)
        {
            var key = category.KeyOf(entry);
            if (!_groups.TryGetValue(key, out var group))
            {
                group = [];
                _groups[key] = group;
                BaseKeys.Add(key);
            }

            _keys[entry] = Key(key, group.Count);
            group.Add(entry);
            Entries.Add(entry);
        }
    }

    public EntryCategory Category { get; }

    public List<string> BaseKeys { get; } = [];

    public List<XElement> Entries { get; } = [];

    public IReadOnlyList<XElement> Group(string baseKey) => _groups.GetValueOrDefault(baseKey) ?? [];

    public string KeyOf(XElement entry) => _keys[entry];

    public XElement? Find(string key)
    {
        var (baseKey, occurrence) = Parse(key);
        var group = Group(baseKey);
        return occurrence < group.Count ? group[occurrence] : null;
    }

    public XElement? Resolve(string key, IReadOnlyList<string>? signature)
    {
        if (signature is not { Count: > 0 })
        {
            return Find(key);
        }

        var (baseKey, occurrence) = Parse(key);
        var group = Group(baseKey);
        var wanted = signature.ToHashSet(StringComparer.Ordinal);
        XElement? best = null;
        var bestScore = (wanted.Count + 1) / 2 - 1;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < group.Count; i++)
        {
            var score = Signature(group[i]).Count(wanted.Contains);
            var distance = Math.Abs(i - occurrence);
            if (score > bestScore || score == bestScore && best is not null && distance < bestDistance)
            {
                best = group[i];
                bestScore = score;
                bestDistance = distance;
            }
        }

        return best;
    }

    public static IReadOnlyList<string> Signature(XElement entry) =>
        [.. XmlEditing.Leaves(entry).Take(SignatureLimit).Select(l => l.Path + "=" + l.Element.Value)];

    public static bool IsKeyless(string baseKey) => baseKey == KeylessBase;

    public static string Key(string baseKey, int occurrence) =>
        occurrence == 0 ? baseKey : baseKey + OccurrenceMark + occurrence.ToString(CultureInfo.InvariantCulture);

    public static (string Base, int Occurrence) Parse(string key)
    {
        var mark = key.LastIndexOf(OccurrenceMark);
        if (mark > 0 && int.TryParse(key.AsSpan(mark + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var occurrence))
        {
            return (key[..mark], occurrence);
        }

        if (key.Length > 1 && key.StartsWith(KeylessBase, StringComparison.Ordinal) && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
        {
            return (KeylessBase, position);
        }

        return (key, 0);
    }
}
