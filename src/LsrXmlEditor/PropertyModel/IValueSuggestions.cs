using System.Xml.Linq;

namespace LsrXmlEditor.PropertyModel;

public interface IValueSuggestions
{
    string Source { get; }

    bool Offers(XElement element);

    IReadOnlyList<string> Values();

    bool Contains(string value);
}
