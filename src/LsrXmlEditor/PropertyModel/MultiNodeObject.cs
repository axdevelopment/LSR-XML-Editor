using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.PropertyModel;

[TypeConverter(typeof(MultiNodeConverter))]
public sealed class MultiNodeObject(IReadOnlyList<XElement> elements, EditContext context) : CustomTypeDescriptor
{
    private PropertyDescriptorCollection? _properties;

    public IReadOnlyList<XElement> Elements { get; } = elements;

    public EditContext Context { get; } = context;

    public string Summary
    {
        get
        {
            var count = GetProperties().Count;
            return count == 1 ? "1 shared field" : $"{count} shared fields";
        }
    }

    public override PropertyDescriptorCollection GetProperties() => _properties ??= Build();

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();

    public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;

    public override TypeConverter GetConverter() => MultiNodeConverter.Instance;

    public override string? GetClassName() => Elements[0].Name.LocalName;

    public override string? GetComponentName() => Elements[0].Name.LocalName;

    public override string ToString() => Summary;

    private PropertyDescriptorCollection Build()
    {
        if (Elements.All(e => !e.HasElements))
        {
            var self = new SharedField(Elements[0].Name.LocalName, Elements[0].Name, Elements, true);
            return new PropertyDescriptorCollection([new MultiFieldDescriptor(this, self)], true);
        }

        var descriptors = SharedFields.Of(Elements).Select(f => (PropertyDescriptor)new MultiFieldDescriptor(this, f)).ToArray();
        return new PropertyDescriptorCollection(descriptors, true);
    }
}

public sealed class MultiNodeConverter : ExpandableObjectConverter
{
    public static MultiNodeConverter Instance { get; } = new();

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is MultiNodeObject node ? node.Summary : base.ConvertTo(context, culture, value, destinationType);

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
        value is MultiNodeObject node ? node.GetProperties() : base.GetProperties(context, value, attributes)!;
}

public sealed class MultiFieldDescriptor : PropertyDescriptor, ILockableField
{
    private readonly MultiNodeObject _owner;
    private readonly SharedField _field;
    private readonly ValueKind _kind;
    private readonly string? _gameReference;
    private MultiNodeObject? _child;

    public MultiFieldDescriptor(MultiNodeObject owner, SharedField field)
        : base(field.Segment, null)
    {
        _owner = owner;
        _field = field;
        _kind = field.IsLeaf ? owner.Context.Kinds.KindOf(field.Elements[0], owner.Context.Entry) : ValueKind.Text;
        _gameReference = field.IsLeaf ? GameReferences.Describe(field.Elements[0]) : null;
    }

    public IReadOnlyList<XElement> Elements => _field.Elements;

    public string Segment => _field.Segment;

    public bool IsLeaf => _field.IsLeaf;

    public bool IsMixed => IsLeaf && SharedFields.CommonValue(Elements) is null;

    public string? GameReference => _gameReference;

    public bool IsLocked => _gameReference is not null && !_owner.Context.IsUnlocked(Elements);

    public string LockedValue => SharedFields.CommonValue(Elements) ?? $"({SharedFields.DistinctValueCount(Elements)} different values)";

    public override string DisplayName
    {
        get
        {
            var name = DisplayNames.Humanize(_field.Name.LocalName);
            var bracket = Segment.IndexOf('[');
            return bracket < 0 ? name : name + " " + Segment[bracket..];
        }
    }

    public override string Category => IsLeaf ? FieldCategories.Values : FieldCategories.Details;

    public override string Description
    {
        get
        {
            var count = Elements.Count;
            if (!IsLeaf)
            {
                return $"XML element <{_field.Name.LocalName}> · shared by all {count} selected entries";
            }

            var values = IsMixed
                ? $"{SharedFields.DistinctValueCount(Elements)} different values across {count} entries – a new value is written to all of them"
                : $"Same value in all {count} entries";
            return IsLocked
                ? $"Locked · {_gameReference} in <{_field.Name.LocalName}> · {values}. Click the … button on the right to unlock."
                : $"XML field <{_field.Name.LocalName}> · Type: {FieldValues.LabelFor(_kind)} · {values}";
        }
    }

    public override Type ComponentType => typeof(MultiNodeObject);

    public override bool IsReadOnly => !IsLeaf;

    public override Type PropertyType => !IsLeaf ? typeof(MultiNodeObject) : IsLocked ? typeof(string) : FieldValues.TypeFor(_kind);

    public override TypeConverter Converter => !IsLeaf
        ? MultiNodeConverter.Instance
        : IsLocked ? LockedValueConverter.Instance : TypeDescriptor.GetConverter(PropertyType);

    public override object? GetEditor(Type editorBaseType) =>
        IsLocked && editorBaseType == typeof(UITypeEditor) ? GameReferenceEditor.Instance : base.GetEditor(editorBaseType);

    public void Unlock() => _owner.Context.Unlock(Elements);

    private MultiNodeObject Child => _child ??= new MultiNodeObject(Elements, _owner.Context);

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

        var common = SharedFields.CommonValue(Elements);
        if (common is null)
        {
            return null;
        }

        return IsLocked ? common : FieldValues.ToTyped(common, _kind);
    }

    public override void SetValue(object? component, object? value)
    {
        if (!IsLeaf || IsLocked)
        {
            return;
        }

        if (IsMixed && value is null or "")
        {
            return;
        }

        var text = FieldValues.ToText(value);
        var changed = new List<XElement>();
        foreach (var element in Elements)
        {
            if (element.Value != text)
            {
                XmlEditing.SetValue(element, text);
                changed.Add(element);
            }
        }

        if (changed.Count == 0)
        {
            return;
        }

        _owner.Context.NotifyChanged(changed);
        OnValueChanged(component, EventArgs.Empty);
    }
}
