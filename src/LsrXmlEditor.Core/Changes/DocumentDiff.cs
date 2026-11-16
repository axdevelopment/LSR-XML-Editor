using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Changes;

public static class DocumentDiff
{
    private const long AlignmentLimit = 250_000;

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
                AddRemovals(before, before.Entries, operations);
            }
        }

        return operations;
    }

    private static void AddRemovals(EntryIndex before, IEnumerable<XElement> entries, List<ChangeOperation> operations) =>
        operations.AddRange(entries.Reverse().Select(entry =>
            new ChangeOperation(ChangeKind.EntryRemoved, before.Category.Path, before.KeyOf(entry), Match: Match(before, entry))));

    private static void CompareCategory(EntryIndex before, EntryIndex after, List<ChangeOperation> operations)
    {
        var pairs = new Dictionary<XElement, XElement>();
        var removed = new HashSet<XElement>();
        foreach (var key in before.BaseKeys.Union(after.BaseKeys, StringComparer.Ordinal))
        {
            foreach (var (old, current) in Align(before.Group(key), after.Group(key)))
            {
                if (current is null)
                {
                    removed.Add(old!);
                }
                else if (old is not null)
                {
                    pairs[current] = old;
                }
            }
        }

        AddRemovals(before, before.Entries.Where(removed.Contains), operations);

        var path = after.Category.Path;
        string? previous = null;
        foreach (var current in after.Entries)
        {
            var key = after.KeyOf(current);
            if (pairs.TryGetValue(current, out var original))
            {
                CompareEntry(path, before.KeyOf(original), Match(before, original), original, current, operations);
            }
            else
            {
                operations.Add(new ChangeOperation(ChangeKind.EntryAdded, path, key, EntryXml: XmlEditing.Serialize(current), AfterKey: previous));
            }

            previous = key;
        }
    }

    private static IReadOnlyList<string>? Match(EntryIndex index, XElement entry) =>
        index.Group(index.Category.KeyOf(entry)).Count > 1 ? EntryIndex.Signature(entry) : null;

    private static IEnumerable<(XElement? Before, XElement? After)> Align(IReadOnlyList<XElement> before, IReadOnlyList<XElement> after)
    {
        var start = 0;
        while (start < before.Count && start < after.Count && XNode.DeepEquals(before[start], after[start]))
        {
            start++;
        }

        var endBefore = before.Count;
        var endAfter = after.Count;
        while (endBefore > start && endAfter > start && XNode.DeepEquals(before[endBefore - 1], after[endAfter - 1]))
        {
            endBefore--;
            endAfter--;
        }

        var result = new List<(XElement?, XElement?)>(Math.Max(before.Count, after.Count));
        for (var i = 0; i < start; i++)
        {
            result.Add((before[i], after[i]));
        }

        var anchors = (long)(endBefore - start) * (endAfter - start) <= AlignmentLimit
            ? CommonRun(before, start, endBefore, after, start, endAfter)
            : [];
        var b = start;
        var a = start;
        foreach (var (anchorBefore, anchorAfter) in anchors.Append((endBefore, endAfter)))
        {
            while (b < anchorBefore && a < anchorAfter)
            {
                result.Add((before[b++], after[a++]));
            }

            while (b < anchorBefore)
            {
                result.Add((before[b++], null));
            }

            while (a < anchorAfter)
            {
                result.Add((null, after[a++]));
            }

            if (anchorBefore < endBefore)
            {
                result.Add((before[b++], after[a++]));
            }
        }

        for (var i = 0; i < before.Count - endBefore; i++)
        {
            result.Add((before[endBefore + i], after[endAfter + i]));
        }

        return result;
    }

    private static List<(int Before, int After)> CommonRun(IReadOnlyList<XElement> before, int fromBefore, int toBefore, IReadOnlyList<XElement> after, int fromAfter, int toAfter)
    {
        var rows = toBefore - fromBefore;
        var columns = toAfter - fromAfter;
        var lengths = new int[rows + 1, columns + 1];
        for (var i = rows - 1; i >= 0; i--)
        {
            for (var j = columns - 1; j >= 0; j--)
            {
                lengths[i, j] = XNode.DeepEquals(before[fromBefore + i], after[fromAfter + j])
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var anchors = new List<(int, int)>();
        for (int i = 0, j = 0; i < rows && j < columns;)
        {
            if (XNode.DeepEquals(before[fromBefore + i], after[fromAfter + j]))
            {
                anchors.Add((fromBefore + i, fromAfter + j));
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return anchors;
    }

    private static void CompareEntry(string category, string key, IReadOnlyList<string>? match, XElement before, XElement after, List<ChangeOperation> operations)
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
            operations.Add(new ChangeOperation(ChangeKind.EntryReplaced, category, key, EntryXml: XmlEditing.Serialize(after), Match: match));
            return;
        }

        for (var i = 0; i < oldLeaves.Count; i++)
        {
            var oldValue = oldLeaves[i].Element.Value;
            var newValue = newLeaves[i].Element.Value;
            if (oldValue != newValue)
            {
                operations.Add(new ChangeOperation(ChangeKind.FieldChanged, category, key, oldLeaves[i].Path, oldValue, newValue, Match: match));
            }
        }
    }
}
