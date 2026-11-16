using LsrXmlEditor.Core.Storage;
using LsrXmlEditor.Core.Themes;

namespace LsrXmlEditor.Infrastructure;

internal static class UiTheme
{
    public const float BaseFontSize = 9f;

    public static IReadOnlyList<int> TextSizes { get; } = [100, 115, 130, 150, 175, 200];

    public static float Scale { get; private set; } = 1f;

    public static ThemePalette? Palette { get; private set; }

    public static Font Font { get; private set; } = Control.DefaultFont;

    public static Color Window => Palette?.Window ?? SystemColors.Window;

    public static Color Surface => Palette?.Surface ?? SystemColors.Control;

    public static Color Text => Palette?.Text ?? SystemColors.WindowText;

    public static Color Muted => Palette?.Muted ?? SystemColors.GrayText;

    public static Color Border => Palette?.Border ?? SystemColors.ControlDark;

    public static Color Selection => Palette?.Selection ?? SystemColors.Highlight;

    public static Color SelectionText => Palette?.SelectionText ?? SystemColors.HighlightText;

    public static Color Error => Palette?.Error ?? (SystemInformation.HighContrast ? SystemColors.ControlText : Color.Firebrick);

    public static void Apply(AppSettings settings, bool startup)
    {
        Scale = Math.Clamp(settings.TextSize, AppSettings.MinTextSize, AppSettings.MaxTextSize) / 100f;
        Palette = Resolve(settings.Theme);
        Application.SetColorMode(Palette?.Dark == true ? SystemColorMode.Dark : SystemColorMode.Classic);

        var name = Palette?.FontName is { } wanted && IsInstalled(wanted) ? wanted : ThemeCatalog.DefaultFont;
        Font = new Font(name, BaseFontSize * Scale);
        if (startup)
        {
            Application.SetDefaultFont(Font);
        }

        if (Palette is { } palette)
        {
            ToolStripManager.Renderer = new PaletteRenderer(palette);
        }
        else
        {
            ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
        }
    }

    public static void Attach(Form form)
    {
        if (Palette is { } palette)
        {
            ControlStyler.Attach(form, palette);
        }
    }

    public static int Grow(int logical) => (int)Math.Round(logical * Scale);

    public static void Fit(params ToolStrip[] strips)
    {
        foreach (var strip in strips)
        {
            strip.Font = Font;
        }
    }

    public static ThemePalette? Resolve(string? id) =>
        string.Equals(id, ThemeCatalog.SystemId, StringComparison.OrdinalIgnoreCase)
            ? Application.SystemColorMode == SystemColorMode.Dark ? ThemeCatalog.Dark : null
            : ThemeCatalog.Find(id);

    public static bool IsInstalled(string fontName)
    {
        try
        {
            using var family = new FontFamily(fontName);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
