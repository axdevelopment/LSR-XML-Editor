using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Changes;

public enum ReplayStatus
{
    Applied,
    AlreadyApplied,
    AppliedOverDifferentValue,
    NotFound
}

public sealed record ReplayResult(ChangeOperation Operation, ReplayStatus Status);

public static class ChangeReplayer
{
    public static IReadOnlyList<ReplayResult> Apply(XmlConfigDocument document, IEnumerable<ChangeOperation> operations)
    {
        var results = new List<ReplayResult>();
        foreach (var operation in operations)
        {
            var category = document.FindCategory(operation.Category);
            var status = category is null ? ReplayStatus.NotFound : ApplyOne(new EntryIndex(category), operation);
            results.Add(new ReplayResult(operation, status));
        }

        return results;
    }

    private static ReplayStatus ApplyOne(EntryIndex index, ChangeOperation operation)
    {
        var entry = index.Find(operation.EntryKey);
        switch (operation.Kind)
        {
            case ChangeKind.FieldChanged:
                var field = entry is null ? null : XmlEditing.Resolve(entry, operation.FieldPath ?? string.Empty);
                if (field is null || field.HasElements)
                {
                    return ReplayStatus.NotFound;
                }

                var current = field.Value;
                if (current == operation.NewValue)
                {
                    return ReplayStatus.AlreadyApplied;
                }

                XmlEditing.SetValue(field, operation.NewValue ?? string.Empty);
                return current == operation.OldValue ? ReplayStatus.Applied : ReplayStatus.AppliedOverDifferentValue;

            case ChangeKind.EntryRemoved:
                if (entry is null)
                {
                    return ReplayStatus.AlreadyApplied;
                }

                XmlEditing.Remove(entry);
                return ReplayStatus.Applied;

            case ChangeKind.EntryAdded:
                if (entry is not null)
                {
                    return ReplayStatus.AlreadyApplied;
                }

                var added = XmlEditing.Deserialize(operation.EntryXml!);
                var anchor = operation.AfterKey is null ? null : index.Find(operation.AfterKey);
                if (anchor is not null)
                {
                    XmlEditing.InsertAfter(anchor, added);
                }
                else
                {
                    XmlEditing.Append(index.Category.Container, added, index.Category.ItemName);
                }

                return ReplayStatus.Applied;

            case ChangeKind.EntryReplaced:
                var replacement = XmlEditing.Deserialize(operation.EntryXml!);
                if (entry is null)
                {
                    XmlEditing.Append(index.Category.Container, replacement, index.Category.ItemName);
                    return ReplayStatus.Applied;
                }

                if (XmlEditing.Serialize(entry) == operation.EntryXml)
                {
                    return ReplayStatus.AlreadyApplied;
                }

                entry.ReplaceWith(replacement);
                return ReplayStatus.Applied;

            default:
                return ReplayStatus.NotFound;
        }
    }
}
