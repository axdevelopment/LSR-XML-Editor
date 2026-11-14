using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.PropertyModel;

[TypeConverter(typeof(XmlNodeConverter))]
public sealed class XmlNodeObject(XElement element, EditContext context) : CustomTypeDescriptor
{
    private PropertyDescriptorCollection? _properties;

    public XElement Element { get; } = element;

    public EditContext Context { get; } = context;

    public bool IsCollection => StructureAnalyzer.IsCollection(Element);

    public string Summary
    {
        get
        {
            var count = Element.Elements().Count();
            if (IsCollection)
            {
                return count == 1 ? "1 item" : $"{count} items";
            }

            var (key, value) = EntryIdentity.Identify(Element);
            return key is not null ? value : count == 1 ? "1 field" : $"{count} fields";
        }
    }

    public override PropertyDescriptorCollection GetProperties() => _properties ??= Build();

    public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();

    public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;

    public override TypeConverter GetConverter() => XmlNodeConverter.Instance;

    public override string? GetClassName() => Element.Name.LocalName;

    public override string? GetComponentName() => Element.Name.LocalName;

    public override string ToString() => Summary;

    private PropertyDescriptorCollection Build()
    {
        if (!Element.HasElements)
        {
            return new PropertyDescriptorCollection([new XmlFieldDescriptor(this, Element, Element.Name.LocalName, null)], true);
        }

        var collection = IsCollection;
        var descriptors = XmlEditing.ChildSegments(Element)
            .Select(c => (PropertyDescriptor)new XmlFieldDescriptor(this, c.Element, c.Segment, collection ? c.Index : null))
            .ToArray();
        return new PropertyDescriptorCollection(descriptors, true);
    }
}

public sealed class XmlNodeConverter : ExpandableObjectConverter
{
    public static XmlNodeConverter Instance { get; } = new();

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is XmlNodeObject node ? node.Summary : base.ConvertTo(context, culture, value, destinationType);

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
        value is XmlNodeObject node ? node.GetProperties() : base.GetProperties(context, value, attributes)!;
}
