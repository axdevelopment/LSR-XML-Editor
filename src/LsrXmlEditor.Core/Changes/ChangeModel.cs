namespace LsrXmlEditor.Core.Changes;

public enum ChangeKind
{
    FieldChanged,
    EntryAdded,
    EntryRemoved,
    EntryReplaced
}

public sealed record ChangeOperation(
    ChangeKind Kind,
    string Category,
    string EntryKey,
    string? FieldPath = null,
    string? OldValue = null,
    string? NewValue = null,
    string? EntryXml = null,
    string? AfterKey = null)
{
    public string Describe() => Kind switch
    {
        ChangeKind.FieldChanged => $"{EntryKey} · {FieldPath}: \"{OldValue}\" → \"{NewValue}\"",
        ChangeKind.EntryAdded => $"{EntryKey} added",
        ChangeKind.EntryRemoved => $"{EntryKey} deleted",
        _ => $"{EntryKey} restructured"
    };
}

public sealed record ChangeSet(Guid Id, DateTime Timestamp, string RelativeFile, IReadOnlyList<ChangeOperation> Operations);
