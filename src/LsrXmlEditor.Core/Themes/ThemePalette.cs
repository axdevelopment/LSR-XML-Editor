using System.Drawing;

namespace LsrXmlEditor.Core.Themes;

public sealed record ThemePalette(
    string Id,
    string Name,
    bool Dark,
    string FontName,
    Color Window,
    Color Surface,
    Color Input,
    Color Text,
    Color Muted,
    Color Border,
    Color Accent,
    Color Selection,
    Color SelectionText,
    Color InactiveSelection,
    Color Hover,
    Color Header,
    Color Error)
{
    public IEnumerable<(string Pair, Color Foreground, Color Background)> TextPairs =>
    [
        ("text on window", Text, Window),
        ("text on surface", Text, Surface),
        ("text on input", Text, Input),
        ("text on header", Text, Header),
        ("text on hover", Text, Hover),
        ("text on inactive selection", Text, InactiveSelection),
        ("muted on window", Muted, Window),
        ("muted on surface", Muted, Surface),
        ("selection text", SelectionText, Selection),
        ("error on window", Error, Window)
    ];
}

public static class Contrast
{
    public static double Ratio(Color a, Color b)
    {
        var (light, dark) = (Luminance(a), Luminance(b)) is var (x, y) && x > y ? (x, y) : (y, x);
        return (light + 0.05) / (dark + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static double Channel(byte value)
    {
        var c = value / 255d;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
