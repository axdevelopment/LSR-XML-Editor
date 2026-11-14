using System.ComponentModel;
using System.Drawing.Design;
using System.Xml.Linq;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.PropertyModel;

public interface ILockableField
{
    string DisplayName { get; }

    string? GameReference { get; }

    bool IsLocked { get; }

    string LockedValue { get; }

    void Unlock();
}

public sealed class XmlFieldDescriptor : PropertyDescriptor, ILockableField
{
    private readonly XmlNodeObject _owner;
    private readonly ValueKind _kind;
    private readonly string? _gameReference;
    private XmlNodeObject? _child;

    public XmlFieldDescriptor(XmlNodeObject owner, XElement element, string segment, int? itemIndex)
        : base(segment, null)
    {
        _owner = owner;
        Element = element;
        Segment = segment;
        IsLeaf = !element.HasElements;
        _kind = IsLeaf ? owner.Context.Kinds.KindOf(element, owner.Context.Entry) : ValueKind.Text;
        _gameReference = IsLeaf ? GameReferences.Describe(element) : null;
        ItemIndex = itemIndex;
    }

    public XElement Element { get; }

    public string Segment { get; }

    public bool IsLeaf { get; }

    public int? ItemIndex { get; }

    public bool IsCollectionItem => ItemIndex is not null;

    public string? GameReference => _gameReference;

    public bool IsLocked => _gameReference is not null && !_owner.Context.IsUnlocked(Element);

    public override string DisplayName => ItemIndex is { } index
        ? IsLeaf ? $"[{index}]" : $"[{index}] {Child.Summary}"
        : DisplayNames.Humanize(Element.Name.LocalName);

    public override string Category => IsCollectionItem
        ? FieldCategories.Items
        : IsLeaf ? FieldCategories.Values : Child.IsCollection ? FieldCategories.Lists : FieldCategories.Details;

    public override string Description => !IsLeaf
        ? $"XML element <{Element.Name.LocalName}> · {Child.Summary}"
        : IsLocked
            ? $"Locked · {_gameReference} in <{Element.Name.LocalName}>. This name comes from GTA V itself – changing it usually breaks LSR. Click the … button on the right to unlock."
            : _gameReference is not null
                ? $"Unlocked · {_gameReference} in <{Element.Name.LocalName}> · Type: {KindLabel}. Only change it if you know the exact game name."
                : $"XML field <{Element.Name.LocalName}> · Type: {KindLabel}";

    public override Type ComponentType => typeof(XmlNodeObject);

    public override bool IsReadOnly => !IsLeaf;

    public override Type PropertyType => !IsLeaf
        ? typeof(XmlNodeObject)
        : IsLocked ? typeof(string) : FieldValues.TypeFor(_kind);

    public override TypeConverter Converter => !IsLeaf
        ? XmlNodeConverter.Instance
        : IsLocked ? LockedValueConverter.Instance : TypeDescriptor.GetConverter(PropertyType);

    public override object? GetEditor(Type editorBaseType) =>
        IsLocked && editorBaseType == typeof(UITypeEditor) ? GameReferenceEditor.Instance : base.GetEditor(editorBaseType);

    public string LockedValue => Element.Value;

    public void Unlock() => _owner.Context.Unlock([Element]);

    private XmlNodeObject Child => _child ??= new XmlNodeObject(Element, _owner.Context);

    private string KindLabel => FieldValues.LabelFor(_kind);

    public override bool CanResetValue(object component) => false;

    public override void ResetValue(object component)
    {
    }

    public override bool ShouldSerializeValue(object component) => false;

    public override object? GetValue(object? component)
    {
        if (!IsLeaf)
        {
            return Child;
        }

        var text = Element.Value;
        return IsLocked ? text : FieldValues.ToTyped(text, _kind);
    }

    public override void SetValue(object? component, object? value)
    {
        if (!IsLeaf || IsLocked)
        {
            return;
        }

        var text = FieldValues.ToText(value);
        if (Element.Value == text)
        {
            return;
        }

        XmlEditing.SetValue(Element, text);
        _owner.Context.NotifyChanged([Element]);
        OnValueChanged(component, EventArgs.Empty);
    }
}
