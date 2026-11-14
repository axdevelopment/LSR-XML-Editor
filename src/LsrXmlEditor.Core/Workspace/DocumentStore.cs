using System.Collections.Concurrent;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Workspace;

public sealed class DocumentStore
{
    private readonly ConcurrentDictionary<string, Lazy<Task<XmlConfigDocument>>> _documents = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<string>? Replaced;

    public IEnumerable<XmlConfigDocument> Loaded =>
        _documents.Values
            .Where(l => l.IsValueCreated && l.Value.IsCompletedSuccessfully)
            .Select(l => l.Value.Result);

    public IEnumerable<XmlConfigDocument> Dirty => Loaded.Where(d => d.IsDirty);

    public Task<XmlConfigDocument> GetAsync(string path)
    {
        var lazy = _documents.GetOrAdd(path, Create);
        var task = lazy.Value;

        if (task.IsFaulted)
        {
            _documents.TryRemove(new KeyValuePair<string, Lazy<Task<XmlConfigDocument>>>(path, lazy));
            return task;
        }

        if (!task.IsCompletedSuccessfully || !IsStale(task.Result))
        {
            return task;
        }

        var fresh = Create(path);
        if (!_documents.TryUpdate(path, fresh, lazy))
        {
            return GetAsync(path);
        }

        Replaced?.Invoke(this, path);
        return fresh.Value;
    }

    public XmlConfigDocument? Find(string path) =>
        _documents.TryGetValue(path, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully ? lazy.Value.Result : null;

    public void Evict(string path) => _documents.TryRemove(path, out _);

    public void Clear() => _documents.Clear();

    private static Lazy<Task<XmlConfigDocument>> Create(string path) =>
        new(() => Task.Run(() => XmlConfigDocument.Load(path)));

    private static bool IsStale(XmlConfigDocument document) =>
        !document.IsDirty
        && File.Exists(document.FilePath)
        && File.GetLastWriteTimeUtc(document.FilePath) != document.LoadedWriteTime;
}
