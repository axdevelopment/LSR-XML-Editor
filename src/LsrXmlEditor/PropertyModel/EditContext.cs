using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.PropertyModel;

public sealed class EditContext(IReadOnlyList<XElement> entries, FieldKindIndex kinds, Action<IReadOnlyList<XElement>> changed, Action refresh)
{
    private readonly HashSet<XElement> _unlocked = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<XElement> Entries { get; } = entries;

    public XElement Entry => Entries[0];

    public FieldKindIndex Kinds { get; } = kinds;

    public void NotifyChanged(IReadOnlyList<XElement> elements) => changed(elements);

    public bool IsUnlocked(XElement element) => _unlocked.Contains(element);

    public bool IsUnlocked(IReadOnlyList<XElement> elements) => elements.All(_unlocked.Contains);

    public void Unlock(IReadOnlyList<XElement> elements)
    {
        var added = false;
        foreach (var element in elements)
        {
            added |= _unlocked.Add(element);
        }

        if (added)
        {
            refresh();
        }
    }
}
