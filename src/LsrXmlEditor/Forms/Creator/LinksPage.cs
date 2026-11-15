using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class LinksPage(string title, string description, CreatorEngine engine, Func<GroupKind, IReadOnlyList<Choice<string>>> groups)
    : CreatorPage(CreatorStep.Linked, title, description)
{
    public const string Hint = """
        Own copy – the group is duplicated under a new ID, so you can change it later without touching the original.
        Share – the new entry simply points to an existing group (any group can be picked, not only the template's).
        The number in brackets is how many entries a group has. Hover over "used by" to see who else uses a group.
        """;

    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        AddHint(Hint);
        foreach (var link in draft.Main.Links)
        {
            AddRow(link.Link.Label + ":", Editor(this, draft, link, engine, groups));
        }
    }

    public static LinkEditor Editor(CreatorPage page, CreatorDraft draft, LinkChoice link, CreatorEngine engine, Func<GroupKind, IReadOnlyList<Choice<string>>> groups)
    {
        var editor = new LinkEditor(link, groups(link.Link.Kind), engine, id => draft.SetLinkId(link, id));
        editor.Changed += (_, _) => page.OnChanged();
        return editor;
    }
}
