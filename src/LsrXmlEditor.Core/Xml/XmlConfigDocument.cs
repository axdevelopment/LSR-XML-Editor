using System.Xml;
using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public sealed class XmlConfigDocument
{
    private const int ReadBufferSize = 1 << 16;

    private IReadOnlyList<EntryCategory>? _categories;

    private XmlConfigDocument(string filePath, XDocument document, XmlFileFormat format, DateTime loadedWriteTime)
    {
        FilePath = filePath;
        Document = document;
        Format = format;
        LoadedWriteTime = loadedWriteTime;
        Document.Changed += OnChanged;
    }

    public event EventHandler? DirtyChanged;

    public string FilePath { get; }

    public XDocument Document { get; }

    public XmlFileFormat Format { get; }

    public DateTime LoadedWriteTime { get; private set; }

    public long Version { get; private set; }

    public bool IsDirty { get; private set; }

    public XElement Root => Document.Root ?? throw new InvalidOperationException("Document has no root element");

    public IReadOnlyList<EntryCategory> Categories => _categories ??= StructureAnalyzer.Analyze(Root);

    public static XmlConfigDocument Load(string path)
    {
        using var stream = OpenRead(path);
        var format = XmlFileFormat.Detect(stream);
        var document = Parse(stream);
        return new XmlConfigDocument(path, document, format, File.GetLastWriteTimeUtc(path));
    }

    public static XDocument Parse(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = false };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public static XDocument ParseFile(string path)
    {
        using var stream = OpenRead(path);
        return Parse(stream);
    }

    public EntryCategory? FindCategory(string path) =>
        Categories.FirstOrDefault(c => string.Equals(c.Path, path, StringComparison.Ordinal));

    public DocumentSnapshot CreateSnapshot() => new(this);

    public void AcceptSave(DocumentSnapshot snapshot)
    {
        if (!ReferenceEquals(snapshot.Source, this) || snapshot.SavedWriteTime is not { } writeTime)
        {
            throw new ArgumentException("The snapshot was not saved from this document.", nameof(snapshot));
        }

        LoadedWriteTime = writeTime;
        if (snapshot.Version == Version)
        {
            SetDirty(false);
        }
    }

    public void WriteTo(string path) => Format.Write(Document, path);

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ReadBufferSize, FileOptions.SequentialScan);

    private void OnChanged(object? sender, XObjectChangeEventArgs e)
    {
        if (e.ObjectChange is XObjectChange.Add or XObjectChange.Remove && sender is XElement)
        {
            _categories = null;
        }

        Version++;
        SetDirty(true);
    }

    private void SetDirty(bool value)
    {
        if (IsDirty == value)
        {
            return;
        }

        IsDirty = value;
        DirtyChanged?.Invoke(this, EventArgs.Empty);
    }
}
