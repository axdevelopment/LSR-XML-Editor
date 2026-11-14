using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;
using LsrXmlEditor.PedViewer.GameData;

namespace LsrXmlEditor.PedViewer.Rendering;

public sealed record HeadLayer(TextureData Mask, float Opacity, (byte R, byte G, byte B)? Tint);

public sealed class OverlaySet
{
    public const int ZoneCount = 6;
    public const int TorsoZone = (int)DecorationZone.Torso;
    public const int HeadZone = (int)DecorationZone.Head;
    public const int LeftArmZone = (int)DecorationZone.LeftArm;
    public const int RightArmZone = (int)DecorationZone.RightArm;
    public const int LeftLegZone = (int)DecorationZone.LeftLeg;
    public const int RightLegZone = (int)DecorationZone.RightLeg;
    private const int ZoneSize = 512;
    private const float HeadTop = 3;
    private const string FacialOverlayFile = "facial_overlays.meta";

    private static readonly int[] WrapOffsets = [-1, 0, 1];
    private static readonly ConditionalWeakTable<GameArchive, IReadOnlyList<string[]>> FacialOverlays = new();

    private OverlaySet(IReadOnlyDictionary<int, TextureData> zones, IReadOnlyList<HeadLayer> headLayers, int decorationCount)
    {
        Zones = zones;
        HeadLayers = headLayers;
        Summary = decorationCount == 0 && headLayers.Count == 0
            ? "no tattoos or face overlays"
            : $"{decorationCount} tattoos · {headLayers.Count} face overlays";
    }

    public static OverlaySet Empty { get; } = new(new Dictionary<int, TextureData>(), [], 0);

    public IReadOnlyDictionary<int, TextureData> Zones { get; }

    public IReadOnlyList<HeadLayer> HeadLayers { get; }

    public string Summary { get; }

    public static OverlaySet Build(GameArchive archive, IEnumerable<AppliedOverlay> decorations, IReadOnlyList<HeadOverlay> headOverlays)
    {
        var catalog = TattooCatalog.For(archive);
        var canvases = new Dictionary<int, Bitmap>();
        var applied = 0;
        try
        {
            foreach (var overlay in decorations)
            {
                if (catalog.Find(overlay.Collection, overlay.Name) is not { IsHairOverlay: false } decoration)
                {
                    continue;
                }

                using var texture = LoadBitmap(archive, decoration.TextureDictionary, decoration.Texture);
                if (texture is null)
                {
                    continue;
                }

                var zone = (int)decoration.Zone;
                if (!canvases.TryGetValue(zone, out var canvas))
                {
                    canvas = new Bitmap(ZoneSize, ZoneSize, PixelFormat.Format32bppArgb);
                    canvases[zone] = canvas;
                }

                Draw(canvas, texture, decoration);
                applied++;
            }

            var zones = canvases.ToDictionary(c => c.Key, c => ToTextureData(c.Value));
            return new OverlaySet(zones, BuildHeadLayers(archive, headOverlays), applied);
        }
        finally
        {
            foreach (var canvas in canvases.Values)
            {
                canvas.Dispose();
            }
        }
    }

    public static int ZoneOf(float v)
    {
        var zone = (int)MathF.Floor((0.5f - v) / 2);
        return zone is >= 0 and < ZoneCount ? zone : -1;
    }

    public static float ZoneY(float v, int zone) => zone == HeadZone ? v + HeadTop : (v + 2 * zone + 1.5f) / 2;

    public static float ZoneX(float u, float tileU) => u - tileU;

    private static void Draw(Bitmap canvas, Bitmap texture, Decoration decoration)
    {
        using var graphics = Graphics.FromImage(canvas);
        graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        var width = decoration.ScaleX * ZoneSize;
        var height = decoration.ScaleY * ZoneSize;
        foreach (var wrap in WrapOffsets)
        {
            graphics.ResetTransform();
            graphics.TranslateTransform((decoration.X + wrap) * ZoneSize, (1 - decoration.Y) * ZoneSize);
            graphics.RotateTransform(-decoration.Rotation);
            graphics.DrawImage(texture, new RectangleF(-width / 2, -height / 2, width, height));
        }
    }

    private static List<HeadLayer> BuildHeadLayers(GameArchive archive, IReadOnlyList<HeadOverlay> headOverlays)
    {
        var slots = FacialOverlays.GetValue(archive, ReadFacialOverlays);
        var layers = new List<HeadLayer>();
        foreach (var overlay in headOverlays.OrderBy(o => o.Slot))
        {
            if (overlay.Slot < 0 || overlay.Slot >= slots.Count || overlay.Index >= slots[overlay.Slot].Length)
            {
                continue;
            }

            var name = slots[overlay.Slot][overlay.Index];
            if (LoadTexture(archive, name, name) is not { } mask)
            {
                continue;
            }

            (byte R, byte G, byte B)? tint = overlay.ColorType switch
            {
                1 => HairPalette.Get(overlay.Color),
                2 => MakeupPalette.Get(overlay.Color),
                _ => null
            };
            layers.Add(new HeadLayer(mask, Math.Clamp(overlay.Opacity, 0, 1), tint));
        }

        return layers;
    }

    private static IReadOnlyList<string[]> ReadFacialOverlays(GameArchive archive)
    {
        var path = archive.FindFile(FacialOverlayFile);
        var text = path is null ? null : archive.ReadText(path);
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        try
        {
            return XDocument.Parse(text).Root!.Elements()
                .Select(slot => slot.Elements("Item").Select(i => i.Value.Trim()).ToArray())
                .ToList();
        }
        catch (XmlException)
        {
            return [];
        }
    }

    private static TextureData? LoadTexture(GameArchive archive, string dictionary, string texture)
    {
        var path = archive.FindFile(dictionary.ToLowerInvariant() + ".ytd");
        var textures = path is null ? null : archive.Load<YtdFile>(path)?.TextureDict;
        if (textures is null)
        {
            return null;
        }

        Texture? found = null;
        textures.Dict?.TryGetValue(JenkHash.GenHash(texture.ToLowerInvariant()), out found);
        found ??= textures.Textures?.data_items?.FirstOrDefault();
        return found is null ? null : MeshExtractor.ExtractTexture(found);
    }

    private static Bitmap? LoadBitmap(GameArchive archive, string dictionary, string texture)
    {
        if (LoadTexture(archive, dictionary, texture) is not { } data)
        {
            return null;
        }

        var bitmap = new Bitmap(data.Width, data.Height, PixelFormat.Format32bppArgb);
        var locked = bitmap.LockBits(new Rectangle(0, 0, data.Width, data.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < data.Height; y++)
            {
                Marshal.Copy(data.Bgra, y * data.Width * 4, locked.Scan0 + y * locked.Stride, data.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }

        return bitmap;
    }

    private static TextureData ToTextureData(Bitmap bitmap)
    {
        var bytes = new byte[bitmap.Width * bitmap.Height * 4];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new TextureData(bitmap.Width, bitmap.Height, bytes);
    }
}
