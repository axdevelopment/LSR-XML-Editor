using System.Globalization;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.PropertyModel;

public static class FieldCategories
{
    public const string Values = "General";
    public const string Details = "Objects";
    public const string Lists = "Lists";
    public const string Items = "Items";
}

public static class FieldValues
{
    public static object ToTyped(string text, ValueKind kind) => kind switch
    {
        ValueKind.Boolean => text == "true",
        ValueKind.Integer when long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) => number,
        ValueKind.Decimal when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
        ValueKind.Integer => 0L,
        ValueKind.Decimal => 0d,
        _ => text
    };

    public static string ToText(object? value) => value switch
    {
        bool flag => flag ? "true" : "false",
        long number => number.ToString(CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        null => string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    public static Type TypeFor(ValueKind kind) => kind switch
    {
        ValueKind.Boolean => typeof(bool),
        ValueKind.Integer => typeof(long),
        ValueKind.Decimal => typeof(double),
        _ => typeof(string)
    };

    public static string LabelFor(ValueKind kind) => kind switch
    {
        ValueKind.Boolean => "Yes/No",
        ValueKind.Integer => "Integer",
        ValueKind.Decimal => "Decimal",
        _ => "Text"
    };
}
