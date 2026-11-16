using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class LocationPage(IReadOnlyList<SavedPosition> saves)
    : CreatorPage(CreatorStep.Location, "Position", "Where the new place is on the map.")
{
    public const string PositionHint = "Easiest way: walk to the spot in game, save with Los Santos RED and pick that save here. You can also paste coordinates, for example from a trainer or CodeWalker.";

    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        AddHint(PositionHint);
        var editor = new PlacementEditor(saves);
        editor.Bind(draft.Main);
        editor.Changed += (_, _) => OnChanged();
        AddRow("Entrance:", editor);
    }
}

internal sealed class DenPage(CreatorEngine engine, IReadOnlyList<SavedPosition> saves, Func<GroupKind, IReadOnlyList<Choice<string>>> groups)
    : CreatorPage(CreatorStep.Den, "Den", "The gang's hangout with its own blip, spawns, staff and menu.")
{
    private readonly List<Control> _parts = [];

    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        _parts.Clear();
        if (draft.Den is not { } den)
        {
            AddHint("There is no gang den in Locations.xml that could be copied.");
            return;
        }

        AddSpanning(Check("&Create a den for the new gang", draft.DenEnabled, v =>
        {
            draft.DenEnabled = v;
            Enable(v);
        }));

        var dens = engine.Entries(CreatorCatalog.Den).ToList();
        var template = Combo();
        template.Items.AddRange([.. dens.Select(d => new Choice<XElement>(d, engine.SourceLabel(d, CreatorCatalog.LocationsFile) is { } file ? $"{Describe(d)} · {file}" : Describe(d)))]);
        template.SelectedIndex = dens.IndexOf(den.Template);
        template.SelectedIndexChanged += (_, _) =>
        {
            if (!Loading && template.SelectedItem is Choice<XElement> choice)
            {
                draft.UseDenTemplate(choice.Value);
                BeginInvoke(() =>
                {
                    Present(draft);
                    OnChanged();
                });
            }
        };
        _parts.Add(AddRow("Copy &of:", template));
        _parts.Add(AddRow("&Name:", Field(draft.DenName, v => draft.SetDenName(v, true))));

        AddSection("Position", LocationPage.PositionHint);
        var placement = new PlacementEditor(saves);
        placement.Bind(den);
        placement.Changed += (_, _) => OnChanged();
        _parts.Add(AddRow("Entrance:", placement));

        if (den.Links.Count > 0)
        {
            AddSection("Menu & staff", "Which menu the den uses and who stands behind the counter. Take an own copy of the menu to change what it sells and buys in the next step without touching the template gang.");
        }

        foreach (var link in den.Links)
        {
            _parts.Add(AddRow(link.Link.Label + ":", LinksPage.Editor(this, draft, link, engine, groups)));
        }

        Enable(draft.DenEnabled);
    }

    private void Enable(bool enabled)
    {
        foreach (var part in _parts)
        {
            part.Enabled = enabled;
        }
    }

    private static string Describe(XElement den)
    {
        var name = den.Element(CreatorCatalog.NameField)?.Value.Trim() ?? string.Empty;
        var owner = den.Element(CreatorCatalog.AssociationField)?.Value.Trim();
        return string.IsNullOrEmpty(owner) ? name : $"{name} ({owner})";
    }
}
