using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public sealed class DocumentSnapshot
{
    private const string TempSuffix = ".tmp";

    internal DocumentSnapshot(XmlConfigDocument source)
    {
        Source = source;
        Content = new XDocument(source.Document);
        Version = source.Version;
        LoadedWriteTime = source.LoadedWriteTime;
    }

    public XmlConfigDocument Source { get; }

    public XDocument Content { get; }

    public long Version { get; }

    public DateTime LoadedWriteTime { get; }

    public DateTime? SavedWriteTime { get; private set; }

    public string FilePath => Source.FilePath;

    public bool IsChangedOnDisk => File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != LoadedWriteTime;

    public void Save()
    {
        var temp = FilePath + TempSuffix;
        try
        {
            Source.Format.Write(Content, temp);
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }

        SavedWriteTime = File.GetLastWriteTimeUtc(FilePath);
    }
}
