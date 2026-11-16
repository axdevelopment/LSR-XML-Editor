using LsrXmlEditor.Core.Creators;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class RelationsPage(CreatorEngine engine)
    : CreatorPage(CreatorStep.Relations, "Friends & enemies", "Which gangs the new gang fights and which it gets along with.")
{
    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        var gangs = engine.Entries(CreatorCatalog.Gang)
            .Select(g => (Key: g.Element(CreatorCatalog.IdField)?.Value.Trim() ?? string.Empty, Name: g.Element(CreatorCatalog.ShortNameField)?.Value.Trim() ?? string.Empty))
            .Where(g => g.Key.Length > 0)
            .Select(g => (g.Key, Text: $"{g.Name} ({g.Key})"))
            .ToList();

        AddHint($"Taken over from {draft.TemplateId}. A gang can't be friend and enemy at once – checking it on one side removes it from the other.");
        var enemies = new CheckList();
        var friends = new CheckList();
        enemies.Bind(gangs, draft.Enemies);
        friends.Bind(gangs, draft.Friends);
        enemies.Toggled += (_, key) => Exclusive(draft.Enemies, draft.Friends, friends, key);
        friends.Toggled += (_, key) => Exclusive(draft.Friends, draft.Enemies, enemies, key);
        AddRow("&Enemies:", enemies);
        AddRow("&Friends:", friends);
        AddSpanning(Check("Also add the new gang to the lists of these gangs (so the feeling is &mutual)", draft.MutualRelations, v => draft.MutualRelations = v));
    }

    private void Exclusive(HashSet<string> changed, HashSet<string> other, CheckList otherList, string key)
    {
        if (changed.Contains(key) && other.Remove(key))
        {
            BeginInvoke(() => otherList.Rebind(other));
        }

        OnChanged();
    }
}

internal sealed class AreaPage(CreatorMode mode, CreatorEngine engine, CreatorData data)
    : CreatorPage(CreatorStep.Area, mode == CreatorMode.Gang ? "Territory" : "Jurisdiction", mode == CreatorMode.Gang ? "The zones where the gang hangs around and spawns." : "Where the agency patrols and responds.")
{
    protected override void Bind(CreatorDraft draft)
    {
        Clear();
        if (mode == CreatorMode.Gang)
        {
            AddHint("Zones can belong to several gangs. The names behind a zone show who is already there.");
            AddArea(draft, "&Zones:", CreatorEngine.Territories, draft.Zones, ZoneItems(CreatorEngine.Territories));
            return;
        }

        AddHint("Zones and counties can be shared with other agencies. The names behind an entry show who already works there.");
        AddArea(draft, "&Zones:", CreatorEngine.ZoneJurisdictions, draft.Zones, ZoneItems(CreatorEngine.ZoneJurisdictions));
        var owners = engine.Owners(CreatorEngine.CountyJurisdictions);
        AddArea(draft, "&Counties:", CreatorEngine.CountyJurisdictions, draft.Counties, [.. data.Counties.Select(c => (c, WithOwners(c, owners[c])))]);
    }

    private void AddArea(CreatorDraft draft, string caption, AreaRows rows, HashSet<string> selected, IReadOnlyList<(string Key, string Text)> items)
    {
        var list = new CheckList();
        list.Bind(items, selected);
        list.Toggled += (_, _) => OnChanged();
        var same = new Button { Text = $"Same as {draft.TemplateId}", AutoSize = true, UseMnemonic = false };
        same.Click += (_, _) =>
        {
            selected.UnionWith(engine.Owned(rows, draft.TemplateId));
            list.Rebind(selected);
            OnChanged();
        };
        var clear = new Button { Text = "Clear", AutoSize = true };
        clear.Click += (_, _) =>
        {
            selected.Clear();
            list.Rebind(selected);
            OnChanged();
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.AddRange([same, clear]);
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
        panel.Controls.AddRange([list, buttons]);
        AddRow(caption, panel);
    }

    private IReadOnlyList<(string Key, string Text)> ZoneItems(AreaRows rows)
    {
        var owners = engine.Owners(rows);
        return [.. data.Zones.Select(z => (z.Code, WithOwners($"{z.Name} ({z.Code})", owners[z.Code])))];
    }

    private static string WithOwners(string text, IEnumerable<string> owners)
    {
        var list = owners.Where(o => o.Length > 0).Select(o => o.Replace(NameRules.GangPrefix, string.Empty, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        return list.Count == 0 ? text : $"{text}  –  {string.Join(", ", list)}";
    }
}
