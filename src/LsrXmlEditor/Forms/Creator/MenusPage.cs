using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class MenusPage(CreatorEngine engine)
    : CreatorPage(CreatorStep.Menus, "Menus", "What the player can buy here and what the place buys from the player.")
{
    private const int ListedUsers = 6;

    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        AddHint("""
            Player buys for – the price in the menu, 0 or -1 means it is not for sale.
            Player sells for – what the player gets for it, 0 or -1 means it is not bought here.
            Stock – how many are for sale, -1 means unlimited. A new item takes its settings from a shop that already sells it.
            """);

        var links = draft.MenuLinks.ToList();
        if (links.Count == 0)
        {
            AddHint(draft.Mode == CreatorMode.Gang && !draft.DenEnabled ? "Turn on the den to set up its menu." : "There is no menu to set up.");
            return;
        }

        foreach (var (link, den) in links)
        {
            AddSection(CreatorDraft.MenuTitle(link, den));
            if (link.SourceId.Length == 0)
            {
                AddHint("No menu chosen.");
                continue;
            }

            var users = engine.Users(link.Link.Kind, link.SourceId);
            if (link.Copy)
            {
                AddHint($"Own copy of {link.SourceId}, saved as {Id(link)}. Only the new {(den ? "den" : draft.Kind.Title)} uses it, {link.SourceId} stays as it is.");
                if (draft.EditableGroup(link) is { } group)
                {
                    AddEditor(group, true);
                }

                continue;
            }

            AddHint($"Shared: {link.SourceId} is used by {List(users)}. Changing it here would change all of them, so it is read-only.");
            var copy = new Button { Text = "Make an own &copy to edit", AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            copy.Click += (_, _) =>
            {
                link.Copy = true;
                BeginInvoke(() =>
                {
                    Present(draft);
                    OnChanged();
                });
            };
            AddSpanning(copy);
            if (engine.FindGroup(link.Link.Kind, link.SourceId) is { } shared)
            {
                AddEditor(shared, false);
            }
        }
    }

    private void AddEditor(XElement group, bool editable)
    {
        var editor = new MenuEditor(MenuEditing.Menus(group), editable, engine);
        editor.Changed += (_, _) => OnChanged();
        AddSpanning(editor);
    }

    private static string Id(LinkChoice link) => link.NewId.Trim() is { Length: > 0 } id ? id : "(no ID yet)";

    private static string List(IReadOnlyList<string> users) => users.Count switch
    {
        0 => "nobody else",
        <= ListedUsers => string.Join(", ", users),
        _ => $"{string.Join(", ", users.Take(ListedUsers))} and {users.Count - ListedUsers} more"
    };
}
