using System.Globalization;

namespace LsrXmlEditor.Core.Xml;

public enum ValueKind
{
    Text,
    Boolean,
    Integer,
    Decimal
}

public static class ValueKinds
{
    public static ValueKind Infer(string value)
    {
        if (value is "true" or "false")
        {
            return ValueKind.Boolean;
        }

        if (value.Length == 0 || value.Length > 32)
        {
            return ValueKind.Text;
        }

        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            return ValueKind.Integer;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return ValueKind.Decimal;
        }

        return ValueKind.Text;
    }

    public static bool TryNormalize(ValueKind kind, string input, out string normalized)
    {
        normalized = input;
        switch (kind)
        {
            case ValueKind.Boolean:
                if (bool.TryParse(input.Trim(), out var flag))
                {
                    normalized = flag ? "true" : "false";
                    return true;
                }

                return false;
            case ValueKind.Integer:
                normalized = input.Trim();
                return long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
            case ValueKind.Decimal:
                normalized = input.Trim().Replace(',', '.');
                return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            default:
                return true;
        }
    }
}
