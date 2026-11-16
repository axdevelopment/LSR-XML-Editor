using System.Drawing.Text;

namespace LsrXmlEditor.Infrastructure;

public enum Glyph
{
    Open = 0xE838,
    Save = 0xE74E,
    SaveAll = 0xEA35,
    Reload = 0xE72C,
    Search = 0xE721,
    Backups = 0xE81C,
    ChangeLog = 0xF0E3,
    Duplicate = 0xE8C8,
    Delete = 0xE74D,
    View3D = 0xE890,
    Undo = 0xE7A7,
    Redo = 0xE7A6,
    Add = 0xE710
}

public static class StockImages
{
    public const string Root = "root";

    private const string AppIconResource = "app.ico";

    public static Icon AppIcon { get; } = new(typeof(StockImages).Assembly.GetManifestResourceStream(AppIconResource)!);

    public const string Folder = "folder";
    public const string FolderOpen = "folder-open";
    public const string Document = "document";
    public const string Variant = "variant";
    public const string Category = "category";
    public const string Settings = "settings";

    public const int SmallSize = 16;

    private static readonly string[] GlyphFonts = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];
    private static readonly Lazy<FontFamily?> GlyphFamily = new(FindGlyphFamily);
    private static readonly Dictionary<(Glyph, int, int), Image> Glyphs = [];
    private static readonly StringFormat Centered = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center
    };

    public static ImageList CreateTreeImages(int size)
    {
        var images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(size, size) };
        var options = size > SmallSize ? StockIconOptions.ShellIconSize : StockIconOptions.SmallIcon;
        Add(images, Root, StockIconId.DriveFixed, options);
        Add(images, Folder, StockIconId.Folder, options);
        Add(images, FolderOpen, StockIconId.FolderOpen, options);
        Add(images, Document, StockIconId.DocumentNoAssociation, options);
        Add(images, Variant, StockIconId.DocumentWithAssociation, options);
        Add(images, Category, StockIconId.Stack, options);
        Add(images, Settings, StockIconId.Settings, options);
        return images;
    }

    public static Image Small(StockIconId id)
    {
        using var icon = SystemIcons.GetStockIcon(id, StockIconOptions.SmallIcon);
        return icon.ToBitmap();
    }

    public static Image? For(Glyph glyph, int size)
    {
        if (GlyphFamily.Value is not { } family)
        {
            return null;
        }

        var color = UiTheme.Palette?.Text ?? SystemColors.ControlText;
        if (!Glyphs.TryGetValue((glyph, size, color.ToArgb()), out var image))
        {
            image = Render(family, glyph, size, color);
            Glyphs[(glyph, size, color.ToArgb())] = image;
        }

        return image;
    }

    private static Bitmap Render(FontFamily family, Glyph glyph, int size, Color color)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font(family, size, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(((char)glyph).ToString(), font, brush, new RectangleF(0, 0, size, size), Centered);
        return bitmap;
    }

    private static FontFamily? FindGlyphFamily()
    {
        using var installed = new InstalledFontCollection();
        var name = GlyphFonts.FirstOrDefault(n => installed.Families.Any(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)));
        return name is null ? null : new FontFamily(name);
    }

    private static void Add(ImageList images, string key, StockIconId id, StockIconOptions options)
    {
        using var icon = SystemIcons.GetStockIcon(id, options);
        images.Images.Add(key, icon.ToBitmap());
    }
}
