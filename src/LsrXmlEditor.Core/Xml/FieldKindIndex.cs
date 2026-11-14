using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public sealed class FieldKindIndex
{
    private const int SampleSize = 400;

    private readonly Dictionary<string, ValueKind> _kinds;

    private FieldKindIndex(Dictionary<string, ValueKind> kinds) => _kinds = kinds;

    public static FieldKindIndex Build(IEnumerable<XElement> entries)
    {
        var kinds = new Dictionary<string, ValueKind>(StringComparer.Ordinal);
        foreach (var entry in entries.Take(SampleSize))
        {
            Collect(entry, string.Empty, kinds);
        }

        return new FieldKindIndex(kinds);
    }

    private static void Collect(XElement element, string shape, Dictionary<string, ValueKind> kinds)
    {
        if (!element.HasElements)
        {
            var value = element.Value;
            if (value.Length > 0)
            {
                var kind = ValueKinds.Infer(value);
                ref var known = ref CollectionsMarshal.GetValueRefOrAddDefault(kinds, shape, out var exists);
                known = exists ? Merge(known, kind) : kind;
            }

            return;
        }

        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            Collect(child, shape.Length == 0 ? name : string.Concat(shape, "/", name), kinds);
        }
    }

    public ValueKind KindOf(XElement leaf, XElement entry)
    {
        var observed = ValueKinds.Infer(leaf.Value);
        return _kinds.TryGetValue(ShapeOf(leaf, entry), out var kind) ? Merge(kind, observed) : observed;
    }

    public static string ShapeOf(XElement leaf, XElement entry)
    {
        if (leaf == entry)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var element in leaf.AncestorsAndSelf().TakeWhile(e => e != entry).Reverse())
        {
            if (builder.Length > 0)
            {
                builder.Append('/');
            }

            builder.Append(element.Name.LocalName);
        }

        return builder.ToString();
    }

    private static ValueKind Merge(ValueKind a, ValueKind b)
    {
        if (a == b)
        {
            return a;
        }

        return (a, b) switch
        {
            (ValueKind.Integer, ValueKind.Decimal) or (ValueKind.Decimal, ValueKind.Integer) => ValueKind.Decimal,
            _ => ValueKind.Text
        };
    }
}
