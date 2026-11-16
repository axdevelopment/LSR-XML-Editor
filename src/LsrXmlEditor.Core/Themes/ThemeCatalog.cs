using System.Drawing;

namespace LsrXmlEditor.Core.Themes;

public static class ThemeCatalog
{
    public const string LightId = "light";
    public const string DarkId = "dark";
    public const string SystemId = "system";
    public const string DefaultFont = "Segoe UI";

    public static ThemePalette Dark { get; } = new(
        DarkId, "Dark", true, DefaultFont,
        Window: Hex(0x1F1F1F), Surface: Hex(0x181818), Input: Hex(0x2B2B2B),
        Text: Hex(0xE6E6E6), Muted: Hex(0xA8A8A8), Border: Hex(0x3C3C3C), Accent: Hex(0x3794FF),
        Selection: Hex(0x0B5CAD), SelectionText: Hex(0xFFFFFF), InactiveSelection: Hex(0x37373D),
        Hover: Hex(0x2A2D2E), Header: Hex(0x252526), Error: Hex(0xF48771));

    public static ThemePalette MidnightPurple { get; } = new(
        "purple", "Midnight purple", true, "Segoe UI Variable Text",
        Window: Hex(0x282A36), Surface: Hex(0x21222C), Input: Hex(0x343746),
        Text: Hex(0xF8F8F2), Muted: Hex(0xB4B9D6), Border: Hex(0x4B4F66), Accent: Hex(0xBD93F9),
        Selection: Hex(0x7046C4), SelectionText: Hex(0xFFFFFF), InactiveSelection: Hex(0x44475A),
        Hover: Hex(0x353849), Header: Hex(0x2E3040), Error: Hex(0xFF7B7B));

    public static ThemePalette DeepTeal { get; } = new(
        "teal", "Deep teal", true, "Bahnschrift",
        Window: Hex(0x0E2629), Surface: Hex(0x0A1D20), Input: Hex(0x15363A),
        Text: Hex(0xDDF3F0), Muted: Hex(0x97C2BD), Border: Hex(0x245459), Accent: Hex(0x2DD4BF),
        Selection: Hex(0x0F766E), SelectionText: Hex(0xFFFFFF), InactiveSelection: Hex(0x1C4448),
        Hover: Hex(0x163C40), Header: Hex(0x12302F), Error: Hex(0xFF8A80));

    public static ThemePalette Sand { get; } = new(
        "sand", "Sand", false, DefaultFont,
        Window: Hex(0xFDF6E3), Surface: Hex(0xEEE8D5), Input: Hex(0xFFFDF6),
        Text: Hex(0x1F2F36), Muted: Hex(0x4F6066), Border: Hex(0xC9BFA5), Accent: Hex(0x1F6FB2),
        Selection: Hex(0x1F6FB2), SelectionText: Hex(0xFFFFFF), InactiveSelection: Hex(0xE0D6BA),
        Hover: Hex(0xF2EAD2), Header: Hex(0xE6DFC8), Error: Hex(0xB3261E));

    public static ThemePalette HighContrast { get; } = new(
        "contrast", "High contrast", true, "Verdana",
        Window: Hex(0x000000), Surface: Hex(0x0A0A0A), Input: Hex(0x141414),
        Text: Hex(0xFFFFFF), Muted: Hex(0xD9D9D9), Border: Hex(0xA0A0A0), Accent: Hex(0xFFD60A),
        Selection: Hex(0xFFD60A), SelectionText: Hex(0x000000), InactiveSelection: Hex(0x3D3D3D),
        Hover: Hex(0x262626), Header: Hex(0x1A1A1A), Error: Hex(0xFF9C9C));

    public static IReadOnlyList<ThemePalette> Custom { get; } = [Dark, MidnightPurple, DeepTeal, Sand, HighContrast];

    public static IReadOnlyList<(string Id, string Name)> Choices { get; } =
        [(LightId, "Light (Windows default)"), .. Custom.Select(p => (p.Id, p.Name)), (SystemId, "Same as Windows (light or dark)")];

    public static ThemePalette? Find(string? id) => Custom.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
