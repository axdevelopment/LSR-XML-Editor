using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class LinksPage(string title, string description, Func<GroupKind, IReadOnlyList<Choice<string>>> groups)
    : CreatorPage(CreatorStep.Linked, title, description)
{
    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        AddHint("""
            Own copy – the group is duplicated under a new ID, so you can change it later without touching the original.
            Share – the new entry simply points to an existing group (any group can be picked, not only the template's).
            The number in brackets is how many entries a group has.
            """);
        foreach (var link in draft.Main.Links)
        {
            AddRow(link.Link.Label + ":", Editor(draft, link));
        }
    }

    private LinkEditor Editor(CreatorDraft draft, LinkChoice link)
    {
        var editor = new LinkEditor(link, groups(link.Link.Kind), id => draft.SetLinkId(link, id));
        editor.Changed += (_, _) => OnChanged();
        return editor;
    }
}
