using System.Globalization;
using System.Xml.Linq;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Creators;

public sealed record MenuChoice(XElement Menu, string Title);

public sealed record ModItemInfo(string Name, string Category);

public static class MenuEditing
{
    public const string ItemField = "ModItemName";
    public const string BuyField = "PurchasePrice";
    public const string SellField = "SalesPrice";
    public const string StockField = "NumberOfItemsToSellToPlayer";
    public const string DemandField = "NumberOfItemsToPurchaseFromPlayer";
    public const string IllicitField = "IsIllicilt";

    private const string NameField = "Name";
    private const string CategorySuffix = "Items";
    private const string MenuElement = "ShopMenu";
    private const string ItemsElement = "Items";
    private const string ItemElement = "MenuItem";
    private const string ExtrasElement = "Extras";
    private const string ChanceField = "SelectChance";
    private const string Unlimited = "-1";

    public static IReadOnlyList<ModItemInfo> ListModItems(IEnumerable<XDocument> documents) =>
        [.. documents
            .SelectMany(d => d.Root?.Elements() ?? [])
            .SelectMany(c => c.Elements().Select(i => new ModItemInfo(i.Element(NameField)?.Value.Trim() ?? string.Empty, Category(c.Name.LocalName))))
            .Where(i => i.Name.Length > 0)
            .DistinctBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)];

    private static string Category(string container) =>
        DisplayNames.Humanize(container.EndsWith(CategorySuffix, StringComparison.Ordinal) && container.Length > CategorySuffix.Length ? container[..^CategorySuffix.Length] : container);

    private static readonly string[] Counters = ["NumberOfItemsSoldToPlayer", "NumberOfItemsPurchasedByPlayer"];

    private static readonly (string Field, string Value)[] BlankItem =
    [
        ("NumberOfItemsSoldToPlayer", "0"),
        ("NumberOfItemsPurchasedByPlayer", "0"),
        (ItemField, string.Empty),
        (BuyField, "0"),
        (SellField, "0"),
        (IllicitField, "false"),
        (ExtrasElement, string.Empty),
        ("SubPrice", "1"),
        ("SubAmount", "1"),
        ("MinimumPurchaseAmount", "1"),
        ("MaximumPurchaseAmount", "10"),
        ("PurchaseIncrement", "1"),
        (StockField, Unlimited),
        (DemandField, Unlimited),
        ("IsFree", "false")
    ];

    public static IReadOnlyList<MenuChoice> Menus(XElement group)
    {
        if (group.Name.LocalName == MenuElement)
        {
            return [new(group, Title(group))];
        }

        return [.. group.Descendants(MenuElement).Select(m => new MenuChoice(m, m.Parent?.Element(ChanceField)?.Value.Trim() is { Length: > 0 } chance ? $"{Title(m)} ({chance} % chance)" : Title(m)))];
    }

    public static IEnumerable<XElement> Items(XElement menu) => menu.Element(ItemsElement)?.Elements(ItemElement) ?? [];

    public static int ItemCount(XElement group) => Menus(group).Sum(m => Items(m.Menu).Count());

    public static IEnumerable<string> ItemNames(XElement group) =>
        Menus(group).SelectMany(m => Items(m.Menu)).Select(i => Text(i, ItemField)).Where(n => n.Length > 0);

    public static XElement Add(XElement menu, string modItem, XElement? pattern)
    {
        var item = pattern is null ? Blank() : new XElement(pattern);
        foreach (var counter in Counters)
        {
            CreatorEngine.SetChild(item, counter, "0");
        }

        CreatorEngine.SetChild(item, ItemField, modItem);
        var items = menu.Element(ItemsElement);
        if (items is null)
        {
            menu.Add(new XElement(ItemsElement, item));
        }
        else if (items.Elements(ItemElement).Any())
        {
            XmlEditing.Append(items, item, ItemElement);
        }
        else
        {
            items.Add(item);
        }

        return item;
    }

    public static void Remove(XElement item) => XmlEditing.Remove(item);

    public static string Text(XElement item, string field) => item.Element(field)?.Value.Trim() ?? string.Empty;

    public static int Number(XElement item, string field) =>
        int.TryParse(Text(item, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value
        : double.TryParse(Text(item, field), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? (int)Math.Round(number) : 0;

    public static bool Flag(XElement item, string field) => string.Equals(Text(item, field), "true", StringComparison.OrdinalIgnoreCase);

    public static void SetNumber(XElement item, string field, int value) => CreatorEngine.SetChild(item, field, value.ToString(CultureInfo.InvariantCulture));

    public static void SetFlag(XElement item, string field, bool value) => CreatorEngine.SetChild(item, field, value ? "true" : "false");

    private static string Title(XElement menu) => Text(menu, "Name") is { Length: > 0 } name ? name : Text(menu, "ID");

    private static XElement Blank() => new(ItemElement, BlankItem.Select(f => new XElement(f.Field, f.Value.Length > 0 ? f.Value : null)));
}
