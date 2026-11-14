using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Changes;

internal sealed class EntryIndex
{
    private readonly Dictionary<string, XElement> _byKey = new(StringComparer.Ordinal);

    public EntryIndex(EntryCategory category)
    {
        Category = category;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var position = 0;
        foreach (var entry in category.Entries)
        {
            var key = category.KeyOf(entry);
            if (key == "#")
            {
                key = "#" + position;
            }

            var seen = occurrences.GetValueOrDefault(key);
            occurrences[key] = seen + 1;
            var unique = seen == 0 ? key : $"{key}~{seen}";
            _byKey[unique] = entry;
            Keys.Add(unique);
            position++;
        }
    }

    public EntryCategory Category { get; }

    public List<string> Keys { get; } = [];

    public XElement? Find(string key) => _byKey.GetValueOrDefault(key);

    public bool Contains(string key) => _byKey.ContainsKey(key);
}
