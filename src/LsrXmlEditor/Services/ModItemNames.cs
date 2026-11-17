using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Files;
using LsrXmlEditor.Core.Xml;
using LsrXmlEditor.PropertyModel;

namespace LsrXmlEditor.Services;

public sealed class ModItemNames(Workspace workspace) : IValueSuggestions
{
    private const string FileBase = "ModItems";
    private const string NameField = "Name";

    public string Source => FileBase + ".xml";

    public bool Offers(XElement element) => element.Name.LocalName == MenuEditing.ItemField;

    public IReadOnlyList<string> Values() => [.. MenuEditing.ListModItems(Documents()).Select(i => i.Name)];

    public bool Contains(string value) =>
        Documents()
            .SelectMany(d => d.Root?.Elements() ?? [])
            .SelectMany(c => c.Elements())
            .Any(i => string.Equals(i.Element(NameField)?.Value.Trim(), value, StringComparison.OrdinalIgnoreCase));

    public void Warm()
    {
        foreach (var path in Paths())
        {
            _ = workspace.Store.GetAsync(path);
        }
    }

    private IEnumerable<string> Paths() =>
        workspace.Files
            .Where(f => f.File.IsInRootFolder && f.File.Kind is LsrFileKind.Base or LsrFileKind.AdditiveVariant
                && string.Equals(f.File.BaseName, FileBase, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.FullPath);

    private List<XDocument> Documents()
    {
        var documents = new List<XDocument>();
        foreach (var path in Paths())
        {
            try
            {
                documents.Add(workspace.Store.GetAsync(path).GetAwaiter().GetResult().Document);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
            }
        }

        return documents;
    }
}
