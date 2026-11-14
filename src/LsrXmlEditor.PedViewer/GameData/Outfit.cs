using System.Globalization;
using System.Xml.Linq;

namespace LsrXmlEditor.PedViewer.GameData;

public sealed record OutfitComponent(int Component, int Drawable, int Texture);

public sealed record AppliedOverlay(string Collection, string Name);

public sealed class OverlayComparer : IEqualityComparer<AppliedOverlay>
{
    public static OverlayComparer Instance { get; } = new();

    public bool Equals(AppliedOverlay? x, AppliedOverlay? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null
            && StringComparer.OrdinalIgnoreCase.Equals(x.Collection, y.Collection)
            && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name));

    public int GetHashCode(AppliedOverlay obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Collection), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
}

public sealed record HeadOverlay(int Slot, int Index, float Opacity, int ColorType, int Color);

public sealed record HeadBlend(int ShapeFirst, int ShapeSecond, int ShapeThird, int SkinFirst, int SkinSecond, int SkinThird, float ShapeMix, float SkinMix, float ThirdMix)
{
    public static HeadBlend Default { get; } = new(0, 0, 0, 0, 0, 0, 0.5f, 0.5f, 0f);
}

public sealed record Outfit(
    string Title,
    string Model,
    IReadOnlyList<OutfitComponent> Components,
    int HairColor,
    int HairHighlight,
    IReadOnlyList<AppliedOverlay> Decorations,
    IReadOnlyList<HeadOverlay> HeadOverlays,
    HeadBlend? Blend)
{
    public static Outfit? Parse(string xml, string title)
    {
        var root = XElement.Parse(xml);
        var model = root.DescendantsAndSelf().FirstOrDefault(e => e.Name.LocalName == "ModelName")?.Value;
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var components = Named(root, "PedComponent")
            .Select(e => new OutfitComponent(Int(e, "ComponentID"), Int(e, "DrawableID"), Int(e, "TextureID")))
            .Where(c => c.Component is >= 0 and < PedCatalog.ComponentCount)
            .GroupBy(c => c.Component)
            .Select(g => g.Last())
            .ToList();

        var decorations = Named(root, "AppliedOverlay")
            .Select(e => new AppliedOverlay(Text(e, "CollectionName"), Text(e, "OverlayName")))
            .Where(o => o.Collection.Length > 0 && o.Name.Length > 0)
            .Distinct(OverlayComparer.Instance)
            .ToList();

        var headOverlays = Named(root, "HeadOverlayData")
            .Select(e => new HeadOverlay(Int(e, "OverlayID"), Int(e, "Index"), Float(e, "Opacity"), Int(e, "ColorType"), Int(e, "PrimaryColor")))
            .Where(o => o.Index is >= 0 and < 255 && o.Opacity > 0)
            .GroupBy(o => o.Slot)
            .Select(g => g.Last())
            .ToList();

        var hair = Find(root, "PrimaryHairColor");
        var highlight = Find(root, "SecondaryHairColor");
        return new Outfit(title, model.Trim(), components, hair ?? -1, highlight ?? hair ?? -1, decorations, headOverlays, ParseBlend(root));
    }

    private static HeadBlend? ParseBlend(XElement root)
    {
        var blend = Named(root, "HeadBlendData").LastOrDefault();
        if (blend is null || Int(blend, "shapeFirst") < 0 || Int(blend, "shapeSecond") < 0)
        {
            return null;
        }

        return new HeadBlend(
            Int(blend, "shapeFirst"),
            Int(blend, "shapeSecond"),
            Math.Max(0, Int(blend, "shapeThird")),
            Math.Max(0, Int(blend, "skinFirst")),
            Math.Max(0, Int(blend, "skinSecond")),
            Math.Max(0, Int(blend, "skinThird")),
            Math.Clamp(Float(blend, "shapeMix"), 0, 1),
            Math.Clamp(Float(blend, "skinMix"), 0, 1),
            Math.Clamp(Float(blend, "thirdMix"), 0, 1));
    }

    private static IEnumerable<XElement> Named(XElement root, string name) =>
        root.Descendants().Where(e => e.Name.LocalName == name);

    private static int? Find(XElement root, string name) =>
        int.TryParse(Named(root, name).FirstOrDefault()?.Value, out var value) && value >= 0 ? value : null;

    private static string Text(XElement element, string name) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

    private static int Int(XElement element, string name) =>
        int.TryParse(Text(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static float Float(XElement element, string name) =>
        float.TryParse(Text(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
}
