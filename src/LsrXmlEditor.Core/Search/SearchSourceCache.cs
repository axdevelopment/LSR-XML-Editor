using System.Collections.Concurrent;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Search;

public sealed class SearchSourceCache
{
    private readonly ConcurrentDictionary<string, (DateTime WriteTime, XElement Root)> _roots = new(StringComparer.OrdinalIgnoreCase);

    public XElement Load(string path)
    {
        var writeTime = File.GetLastWriteTimeUtc(path);
        if (_roots.TryGetValue(path, out var cached) && cached.WriteTime == writeTime)
        {
            return cached.Root;
        }

        var root = XmlConfigDocument.ParseFile(path).Root!;
        _roots[path] = (writeTime, root);
        return root;
    }
}
