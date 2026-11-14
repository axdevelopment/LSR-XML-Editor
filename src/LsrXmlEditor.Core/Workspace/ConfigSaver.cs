using System.Xml;
using LsrXmlEditor.Core.Backups;
using LsrXmlEditor.Core.Changes;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Workspace;

public sealed record SaveResult(BackupInfo? Backup, ChangeSet? Changes);

public sealed class ExternalChangeException(string filePath)
    : IOException($"{Path.GetFileName(filePath)} was changed by another program after it was opened.")
{
    public string FilePath { get; } = filePath;
}

public sealed class ConfigSaver(string folder, BackupService backups, ChangeLogStore changeLog)
{
    public SaveResult Save(XmlConfigDocument document, bool overwriteExternalChanges = false)
    {
        var snapshot = document.CreateSnapshot();
        var result = Write(snapshot, overwriteExternalChanges);
        document.AcceptSave(snapshot);
        return result;
    }

    public SaveResult Write(DocumentSnapshot snapshot, bool overwriteExternalChanges)
    {
        if (!overwriteExternalChanges && snapshot.IsChangedOnDisk)
        {
            throw new ExternalChangeException(snapshot.FilePath);
        }

        var changeSet = Diff(snapshot);
        var backup = backups.Create(folder, snapshot.FilePath);
        snapshot.Save();

        if (changeSet is not null)
        {
            changeLog.Append(changeSet);
        }

        return new SaveResult(backup, changeSet);
    }

    private ChangeSet? Diff(DocumentSnapshot snapshot)
    {
        var path = snapshot.FilePath;
        if (!File.Exists(path) || snapshot.Content.Root is not { } after)
        {
            return null;
        }

        try
        {
            var operations = DocumentDiff.Compute(XmlConfigDocument.ParseFile(path).Root!, after);
            return operations.Count == 0 ? null : new ChangeSet(Guid.NewGuid(), DateTime.Now, Path.GetRelativePath(folder, path), operations);
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
