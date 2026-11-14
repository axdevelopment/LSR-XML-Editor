using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace LsrXmlEditor.PedViewer.GameData;

public abstract record PreviewRequest;

public sealed record PedPreview(string Xml) : PreviewRequest;

public sealed record ModelPreview(string Model, int? PrimaryColor, int? SecondaryColor, string Xml) : PreviewRequest;

public sealed record AnimationPreview(string Dictionary, string Clip) : PreviewRequest;

public static class PreviewRequests
{
    private const string DictionaryElement = "AnimationDictionary";

    private static readonly string[] ModelElements = ["ModelName", "ModelItemID"];
    private static readonly string[] ClipElements = ["AnimationIdle", "AnimationName", "AnimationEnter", "AnimationExit"];

    public static bool CanPreview(string xml) => Parse(xml) is not null;

    public static PreviewRequest? Parse(string xml)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return null;
        }

        var clip = ClipElements.Select(name => Child(root, name)).FirstOrDefault(value => value.Length > 0);
        if (clip is not null && root.Elements().Any(e => e.Name.LocalName == DictionaryElement))
        {
            return new AnimationPreview(Child(root, DictionaryElement), clip);
        }

        var model = ModelElements.Select(name => Child(root, name)).FirstOrDefault(value => value.Length > 0)
            ?? root.Descendants().FirstOrDefault(e => ModelElements.Contains(e.Name.LocalName) && e.Value.Trim().Length > 0)?.Value.Trim();
        if (string.IsNullOrEmpty(model))
        {
            return null;
        }

        if (root.Descendants().Any(e => e.Name.LocalName is "PedComponent" or "HeadBlendData"))
        {
            return new PedPreview(xml);
        }

        return new ModelPreview(model, Color(root, "RequiredPrimaryColorID"), Color(root, "RequiredSecondaryColorID"), xml);
    }

    private static string Child(XElement root, string name) =>
        root.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

    private static int? Color(XElement root, string name) =>
        int.TryParse(Child(root, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : null;
}
