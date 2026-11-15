using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Creators;

public sealed record Placement(double X, double Y, double Z, double Heading)
{
    public bool SameSpot(Placement other) =>
        Math.Abs(X - other.X) < 0.01 && Math.Abs(Y - other.Y) < 0.01 && Math.Abs(Z - other.Z) < 0.01;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{X:0.##}, {Y:0.##}, {Z:0.##} · heading {Heading:0.#}°");
}

public static class Relocation
{
    private const string EntranceElement = "EntrancePosition";
    private const string EntranceHeading = "EntranceHeading";
    private const string HeadingElement = "Heading";
    private const string YawElement = "Yaw";
    private const string DirectionMarker = "Direction";
    private static readonly string[] Axes = ["X", "Y", "Z"];

    public static Placement? Read(XElement entry)
    {
        if (entry.Element(EntranceElement) is not { } position || Vector(position) is not { } point)
        {
            return null;
        }

        return new Placement(point[0], point[1], point[2], Parse(entry.Element(EntranceHeading)?.Value) ?? 0);
    }

    public static int CountAttached(XElement entry) => Points(entry).Count(p => !p.IsDirection);

    public static void Move(XElement entry, Placement target, bool attached)
    {
        if (Read(entry) is not { } origin)
        {
            return;
        }

        var turn = target.Heading - origin.Heading;
        if (attached)
        {
            var radians = turn * Math.PI / 180;
            var (sin, cos) = Math.SinCos(radians);
            foreach (var point in Points(entry).ToList())
            {
                var v = Vector(point.Element)!;
                if (point.IsDirection)
                {
                    Write(point.Element, v[0] * cos - v[1] * sin, v[0] * sin + v[1] * cos, v[2]);
                    continue;
                }

                var dx = v[0] - origin.X;
                var dy = v[1] - origin.Y;
                Write(point.Element, target.X + dx * cos - dy * sin, target.Y + dx * sin + dy * cos, v[2] + target.Z - origin.Z);
                if (point.Element.Parent?.Element(HeadingElement) is { HasElements: false } heading && Parse(heading.Value) is { } value)
                {
                    XmlEditing.SetValue(heading, Number(Normalize(value + turn)));
                }
            }

            foreach (var yaw in entry.Descendants(YawElement).Where(y => !y.HasElements && Parse(y.Value) is not (null or 0)).ToList())
            {
                XmlEditing.SetValue(yaw, Number(Normalize(Parse(yaw.Value)!.Value + turn)));
            }
        }

        Write(entry.Element(EntranceElement)!, target.X, target.Y, target.Z);
        if (entry.Element(EntranceHeading) is { } entranceHeading)
        {
            XmlEditing.SetValue(entranceHeading, Number(Normalize(target.Heading)));
        }
    }

    public static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static IEnumerable<(XElement Element, bool IsDirection)> Points(XElement entry) =>
        entry.Descendants()
            .Where(e => e.Name.LocalName != EntranceElement && Vector(e) is { } v && (v[0] != 0 || v[1] != 0 || v[2] != 0))
            .Select(e => (e, e.Name.LocalName.Contains(DirectionMarker, StringComparison.Ordinal)));

    private static double[]? Vector(XElement element)
    {
        if (!element.HasElements || element.Elements().Count() != Axes.Length)
        {
            return null;
        }

        var values = new double[Axes.Length];
        for (var i = 0; i < Axes.Length; i++)
        {
            if (Parse(element.Element(Axes[i])?.Value) is not { } value)
            {
                return null;
            }

            values[i] = value;
        }

        return values;
    }

    private static void Write(XElement element, double x, double y, double z)
    {
        XmlEditing.SetValue(element.Element(Axes[0])!, Number(x));
        XmlEditing.SetValue(element.Element(Axes[1])!, Number(y));
        XmlEditing.SetValue(element.Element(Axes[2])!, Number(z));
    }

    private static double Normalize(double heading)
    {
        var value = heading % 360;
        return value < 0 ? value + 360 : value;
    }

    private static double? Parse(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
