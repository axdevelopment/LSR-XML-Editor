using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Changes;

public static class DocumentDiff
{
    public static IReadOnlyList<ChangeOperation> Compute(XElement beforeRoot, XElement afterRoot)
    {
        var after = StructureAnalyzer.Analyze(afterRoot).ToDictionary(c => c.Path, StringComparer.Ordinal);
        var operations = new List<ChangeOperation>();

        foreach (var original in StructureAnalyzer.Analyze(beforeRoot))
        {
            var before = new EntryIndex(original);
            if (after.TryGetValue(original.Path, out var current))
            {
                CompareCategory(before, new EntryIndex(current), operations);
            }
            else
            {
                AddRemovals(before, before.Keys, operations);
            }
        }

        return operations;
    }

    private static void AddRemovals(EntryIndex before, IEnumerable<string> keys, List<ChangeOperation> operations) =>
        operations.AddRange(keys.Select(key => new ChangeOperation(ChangeKind.EntryRemoved, before.Category.Path, key)));

    private static void CompareCategory(EntryIndex before, EntryIndex after, List<ChangeOperation> operations)
    {
        var path = after.Category.Path;
        AddRemovals(before, before.Keys.Where(key => !after.Contains(key)), operations);

        string? previous = null;
        foreach (var key in after.Keys)
        {
            var current = after.Find(key)!;
            var original = before.Find(key);

            if (original is null)
            {
                operations.Add(new ChangeOperation(ChangeKind.EntryAdded, path, key, EntryXml: XmlEditing.Serialize(current), AfterKey: previous));
            }
            else
            {
                CompareEntry(path, key, original, current, operations);
            }

            previous = key;
        }
    }

    private static void CompareEntry(string category, string key, XElement before, XElement after, List<ChangeOperation> operations)
    {
        if (XNode.DeepEquals(before, after))
        {
            return;
        }

        var oldLeaves = XmlEditing.Leaves(before).ToList();
        var newLeaves = XmlEditing.Leaves(after).ToList();

        var sameShape = oldLeaves.Count == newLeaves.Count;
        for (var i = 0; sameShape && i < oldLeaves.Count; i++)
        {
            sameShape = oldLeaves[i].Path == newLeaves[i].Path;
        }

        if (!sameShape)
        {
            operations.Add(new ChangeOperation(ChangeKind.EntryReplaced, category, key, EntryXml: XmlEditing.Serialize(after)));
            return;
        }

        for (var i = 0; i < oldLeaves.Count; i++)
        {
            var oldValue = oldLeaves[i].Element.Value;
            var newValue = newLeaves[i].Element.Value;
            if (oldValue != newValue)
            {
                operations.Add(new ChangeOperation(ChangeKind.FieldChanged, category, key, oldLeaves[i].Path, oldValue, newValue));
            }
        }
    }
}
