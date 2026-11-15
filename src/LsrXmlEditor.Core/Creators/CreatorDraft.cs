using System.Xml.Linq;
using LsrXmlEditor.Core.Xml;

namespace LsrXmlEditor.Core.Creators;

public enum CreatorStep
{
    Basics,
    Linked,
    Location,
    Den,
    Menus,
    Relations,
    Area,
    Settings,
    Summary
}

public sealed record CreatorProblem(CreatorStep Step, string Message, bool Blocking);

public sealed record CreatorChange(string File, string Text);

public sealed class LinkChoice(LinkField link, string templateId)
{
    private string _sourceId = templateId;

    public LinkField Link { get; } = link;

    public string TemplateId { get; } = templateId;

    public string SourceId
    {
        get => _sourceId;
        set
        {
            if (!string.Equals(_sourceId, value, StringComparison.OrdinalIgnoreCase))
            {
                _sourceId = value;
                Content = null;
            }
        }
    }

    public XElement? Content { get; set; }

    public bool Copy { get; set; } = link.CopyByDefault && templateId.Length > 0;

    public string NewId { get; set; } = string.Empty;

    public bool NewIdEdited { get; set; }

    public string Value => Copy ? NewId.Trim() : SourceId;
}

public sealed class EntryDraft
{
    private string _appliedOwner;

    public EntryDraft(EntityKind kind, XElement template)
    {
        Kind = kind;
        Template = template;
        Entry = new XElement(template);
        Origin = Relocation.Read(template);
        Placement = Origin;
        AttachedPoints = Relocation.CountAttached(template);
        Links = [.. kind.Links.Where(l => template.Element(l.Field) is { HasElements: false }).Select(l => new LinkChoice(l, Original(l.Field)))];
        HasOwner = template.Element(CreatorCatalog.AssociationField) is { HasElements: false };
        OriginalOwner = Original(CreatorCatalog.AssociationField);
        Owner = OriginalOwner;
        _appliedOwner = OriginalOwner;
    }

    public EntityKind Kind { get; }

    public XElement Template { get; }

    public XElement Entry { get; }

    public IReadOnlyList<LinkChoice> Links { get; }

    public Placement? Origin { get; }

    public Placement? Placement { get; set; }

    public bool MoveAttached { get; set; } = true;

    public int AttachedPoints { get; }

    public bool HasOwner { get; }

    public string OriginalOwner { get; }

    public string Owner { get; set; }

    public bool OwnerChanged => HasOwner && !string.Equals(Owner.Trim(), OriginalOwner, StringComparison.OrdinalIgnoreCase);

    public string Original(string field) => Template.Element(field)?.Value.Trim() ?? string.Empty;

    public string Get(string field) => Entry.Element(field)?.Value ?? string.Empty;

    public bool Has(string field) => Entry.Element(field) is { HasElements: false };

    public void Set(string field, string value) => CreatorEngine.SetChild(Entry, field, value);

    public void Apply()
    {
        foreach (var link in Links)
        {
            Set(link.Link.Field, link.Value);
        }

        if (Placement is { } placement)
        {
            Relocation.Move(Entry, placement, MoveAttached);
        }

        ApplyOwner();
    }

    private void ApplyOwner()
    {
        if (!HasOwner)
        {
            return;
        }

        var owner = Owner.Trim();
        Set(CreatorCatalog.AssociationField, owner);
        if (_appliedOwner.Length == 0 || owner.Length == 0)
        {
            return;
        }

        foreach (var spawn in Entry.Descendants(CreatorCatalog.SpawnAssociationField).Where(e => !e.HasElements && string.Equals(e.Value.Trim(), _appliedOwner, StringComparison.OrdinalIgnoreCase)))
        {
            XmlEditing.SetValue(spawn, owner);
        }

        _appliedOwner = owner;
    }
}

public sealed class CreatorDraft
{
    private const string TrueText = "true";
    private const string FalseText = "false";

    private readonly HashSet<string> _manual = new(StringComparer.Ordinal);
    private bool _denNameEdited;

    public CreatorDraft(CreatorMode mode, CreatorEngine engine, EntityKind kind, XElement template)
    {
        Mode = mode;
        Engine = engine;
        Main = new EntryDraft(kind, template);
        TemplateId = kind.IdField is { } idField ? Main.Original(idField) : string.Empty;
        Main.Set(kind.NameField, string.Empty);
        if (kind.IdField is { } field)
        {
            Main.Set(field, string.Empty);
        }

        if (mode == CreatorMode.Gang)
        {
            Enemies.UnionWith(CreatorEngine.ListValues(template, CreatorCatalog.EnemiesField));
            Friends.UnionWith(CreatorEngine.ListValues(template, CreatorCatalog.FriendsField));
            var dens = engine.Entries(CreatorCatalog.Den).ToList();
            var own = dens.Where(d => Same(d.Element(CreatorCatalog.AssociationField)?.Value, TemplateId)).ToList();
            var den = own.FirstOrDefault(d => d.Element(CreatorCatalog.PrimaryDenField)?.Value == TrueText) ?? own.FirstOrDefault() ?? dens.FirstOrDefault();
            if (den is not null)
            {
                UseDenTemplate(den);
                DenEnabled = own.Count > 0;
            }
        }

        if (mode == CreatorMode.GangDen)
        {
            PrimaryDen = false;
        }
    }

    public CreatorMode Mode { get; }

    public CreatorEngine Engine { get; }

    public EntryDraft Main { get; }

    public EntryDraft? Den { get; private set; }

    public bool DenEnabled { get; set; }

    public string TemplateId { get; }

    public string? OwnerGangId
    {
        get => Main.Owner;
        set => Main.Owner = value ?? string.Empty;
    }

    public bool PrimaryDen { get; set; }

    public HashSet<string> Enemies { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Friends { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool MutualRelations { get; set; } = true;

    public HashSet<string> Zones { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Counties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public EntityKind Kind => Main.Kind;

    public string Name => Main.Get(Kind.NameField).Trim();

    public string Id => Kind.IdField is { } idField ? Main.Get(idField).Trim() : string.Empty;

    public string DenName => Den?.Get(CreatorCatalog.NameField) ?? string.Empty;

    public IEnumerable<LinkChoice> AllLinks =>
        DenEnabled && Den is not null ? Main.Links.Concat(Den.Links) : Main.Links;

    public IEnumerable<(LinkChoice Link, bool Den)> MenuLinks =>
        Main.Links.Select(l => (Link: l, Den: false))
            .Concat(DenEnabled && Den is not null ? Den.Links.Select(l => (Link: l, Den: true)) : [])
            .Where(l => CreatorCatalog.IsMenu(l.Link.Link.Kind));

    public bool HasMenus => Main.Links.Concat(Den?.Links ?? []).Any(l => CreatorCatalog.IsMenu(l.Link.Kind));

    public bool IsManual(string field) => _manual.Contains(field);

    public XElement? Group(LinkChoice link) =>
        link.Copy && link.Content is { } content ? content : Engine.FindGroup(link.Link.Kind, link.SourceId);

    public XElement? EditableGroup(LinkChoice link)
    {
        if (!link.Copy)
        {
            return null;
        }

        if (link.Content is null && Engine.FindGroup(link.Link.Kind, link.SourceId) is { } source)
        {
            link.Content = new XElement(source);
        }

        return link.Content;
    }

    public void UseDenTemplate(XElement template)
    {
        var name = Den?.Get(CreatorCatalog.NameField);
        Den = new EntryDraft(CreatorCatalog.Den, template);
        if (_denNameEdited && name is not null)
        {
            SetDenName(name, true);
        }

        Refresh();
    }

    public void SetName(string name)
    {
        Main.Set(Kind.NameField, name);
        Refresh();
    }

    public void SetField(string field, string value)
    {
        Main.Set(field, value);
        _manual.Add(field);
        Refresh();
    }

    public void MarkManual(string field) => _manual.Add(field);

    public void SetDenName(string name, bool manual)
    {
        if (Den is null)
        {
            return;
        }

        _denNameEdited |= manual;
        Den.Set(CreatorCatalog.NameField, name);
        if (Den.Original(CreatorCatalog.FullNameField) == Den.Original(CreatorCatalog.NameField) || manual)
        {
            Den.Set(CreatorCatalog.FullNameField, name);
        }
    }

    public void SetLinkId(LinkChoice link, string id)
    {
        link.NewId = id;
        link.NewIdEdited = true;
    }

    public void Refresh()
    {
        var name = Name;
        if (name.Length > 0)
        {
            switch (Mode)
            {
                case CreatorMode.Gang:
                case CreatorMode.Agency:
                    RefreshOrganization(name);
                    break;
                default:
                    RefreshLocation(Main, name);
                    break;
            }
        }

        var token = NameRules.Token(name.Length > 0 ? name : Id);
        foreach (var link in Main.Links.Concat(Den?.Links ?? []).Where(l => !l.NewIdEdited))
        {
            link.NewId = token.Length == 0 ? string.Empty : token + link.Link.Suffix;
        }

        if (Den is not null && !_denNameEdited && name.Length > 0)
        {
            SetDenName($"{name} {Main.Get(CreatorCatalog.DenNameField)}".Trim(), false);
            RefreshLocation(Den, DenName, false);
        }
    }

    public IReadOnlyList<CreatorProblem> Problems()
    {
        var problems = new List<CreatorProblem>();
        var name = Name;
        if (name.Length == 0)
        {
            problems.Add(new(CreatorStep.Basics, "Enter a name.", true));
        }

        if (Kind.IdField is not null)
        {
            var id = Id;
            if (id.Length == 0)
            {
                problems.Add(new(CreatorStep.Basics, "Enter an ID.", true));
            }
            else if (!NameRules.IsValidId(id))
            {
                problems.Add(new(CreatorStep.Basics, "The ID must not contain spaces or <>&\" characters.", true));
            }
            else if (Engine.EntryExists(Kind, id))
            {
                problems.Add(new(CreatorStep.Basics, $"The ID \"{id}\" is already taken by another {Kind.Title}.", true));
            }
        }
        else if (name.Length > 0 && Engine.Entries(Kind).Any(e => Same(e.Element(Kind.NameField)?.Value, name)))
        {
            problems.Add(new(CreatorStep.Basics, $"Another {Kind.Title} is already called \"{name}\".", false));
        }

        if (Mode == CreatorMode.GangDen && string.IsNullOrEmpty(OwnerGangId))
        {
            problems.Add(new(CreatorStep.Basics, "Choose the gang that owns the den.", true));
        }

        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in AllLinks.Where(l => l.Copy))
        {
            var id = link.NewId.Trim();
            var label = link.Link.Label.ToLowerInvariant();
            if (link.SourceId.Length == 0)
            {
                problems.Add(new(CreatorStep.Linked, $"Choose which group to copy for {label}.", true));
            }
            else if (id.Length == 0)
            {
                problems.Add(new(CreatorStep.Linked, $"Enter a new ID for {label}.", true));
            }
            else if (!NameRules.IsValidId(id))
            {
                problems.Add(new(CreatorStep.Linked, $"The new ID for {label} must not contain spaces.", true));
            }
            else if (Engine.GroupExists(link.Link.Kind, id) || !planned.Add(link.Link.Kind.Element + "|" + id))
            {
                problems.Add(new(CreatorStep.Linked, $"The ID \"{id}\" for {label} is already taken.", true));
            }
        }

        foreach (var (link, den) in MenuLinks)
        {
            if (Group(link) is not { } group)
            {
                continue;
            }

            var missing = MenuEditing.ItemNames(group).Where(n => !Engine.ItemExists(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
            {
                var more = missing.Count > 5 ? " …" : string.Empty;
                problems.Add(new(CreatorStep.Menus, $"{MenuTitle(link, den)}: {string.Join(", ", missing.Take(5))}{more} not found in ModItems.xml.", false));
            }
        }

        AddInteriorProblem(problems, Main, CreatorStep.Basics);
        if (Mode == CreatorMode.Gang && DenEnabled && Den is not null)
        {
            AddInteriorProblem(problems, Den, CreatorStep.Den);
        }

        if (Main.Placement is { } placement && Main.Origin is { } origin && placement.SameSpot(origin))
        {
            problems.Add(new(CreatorStep.Location, $"The position is still the one of \"{Main.Original(CreatorCatalog.NameField)}\" – the new {Kind.Title} would sit on top of it.", false));
        }

        if (Mode == CreatorMode.Gang && DenEnabled && Den is not null)
        {
            if (DenName.Trim().Length == 0)
            {
                problems.Add(new(CreatorStep.Den, "Enter a name for the den.", true));
            }

            if (Den.Placement is { } denPlacement && Den.Origin is { } denOrigin && denPlacement.SameSpot(denOrigin))
            {
                problems.Add(new(CreatorStep.Den, $"The den is still at the position of \"{Den.Original(CreatorCatalog.NameField)}\".", false));
            }
        }

        return problems;
    }

    public IReadOnlyList<CreatorChange> Changes(Func<string, string>? label = null)
    {
        var changes = new List<CreatorChange>();
        var mainFile = Engine.FileOf(Main.Template);
        var title = Kind.Title;
        var entry = Kind.IdField is not null ? $"{Id} ({Name})" : $"\"{Name}\"";
        changes.Add(new(mainFile, $"New {title} {entry}, based on {Describe(Main)}"));
        AddLinkChanges(changes, Main, mainFile);
        AddPlacementChange(changes, Main, mainFile, title);

        switch (Mode)
        {
            case CreatorMode.GangDen:
                changes.Add(new(mainFile, $"Owned by {OwnerGangId}{(PrimaryDen ? " as its main den" : string.Empty)}"));
                break;
            case CreatorMode.Location when Main.OwnerChanged:
                var owner = Main.Owner.Trim();
                changes.Add(new(mainFile, owner.Length > 0 ? $"Run by {owner} instead of {Nobody(Main.OriginalOwner)}, spawns inside switch over too" : $"No owner (was {Main.OriginalOwner})"));
                break;
            case CreatorMode.Gang:
                AddGangChanges(changes, mainFile, label);
                break;
            case CreatorMode.Agency:
                AddArea(changes, CreatorEngine.ZoneJurisdictions, Zones, "zone", label);
                AddArea(changes, CreatorEngine.CountyJurisdictions, Counties, "county", null);
                break;
        }

        return changes;
    }

    public XElement Commit()
    {
        var copies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in AllLinks.Where(l => l.Copy))
        {
            if (copies.Add(link.Link.Kind.Element + "|" + link.NewId.Trim()))
            {
                Engine.CopyGroup(link.Link.Kind, link.SourceId, link.NewId.Trim(), link.Content);
            }
        }

        Main.Apply();
        var id = Id;
        switch (Mode)
        {
            case CreatorMode.GangDen:
                Main.Set(CreatorCatalog.PrimaryDenField, PrimaryDen ? TrueText : FalseText);
                break;
            case CreatorMode.Gang:
                CreatorEngine.SetList(Main.Entry, CreatorCatalog.EnemiesField, Enemies.Where(e => !Same(e, id)));
                CreatorEngine.SetList(Main.Entry, CreatorCatalog.FriendsField, Friends.Where(f => !Same(f, id) && !Enemies.Contains(f)));
                break;
        }

        CreatorEngine.Insert(Main.Template, Main.Entry);

        if (Mode == CreatorMode.Gang)
        {
            if (DenEnabled && Den is not null)
            {
                Den.Owner = id;
                Den.Apply();
                Den.Set(CreatorCatalog.PrimaryDenField, TrueText);
                CreatorEngine.Insert(Den.Template, Den.Entry);
            }

            Engine.AddRows(CreatorEngine.Territories, id, TemplateId, Zones);
            if (MutualRelations)
            {
                foreach (var (gang, field) in Enemies.Select(e => (e, CreatorCatalog.EnemiesField)).Concat(Friends.Except(Enemies).Select(f => (f, CreatorCatalog.FriendsField))))
                {
                    if (Engine.FindEntry(CreatorCatalog.Gang, gang) is { } other)
                    {
                        CreatorEngine.AddToList(other, field, id);
                    }
                }
            }
        }

        if (Mode == CreatorMode.Agency)
        {
            Engine.AddRows(CreatorEngine.ZoneJurisdictions, id, TemplateId, Zones);
            Engine.AddRows(CreatorEngine.CountyJurisdictions, id, TemplateId, Counties);
        }

        return Main.Entry;
    }

    private void RefreshOrganization(string name)
    {
        var agency = Mode == CreatorMode.Agency;
        Auto(Main, CreatorCatalog.IdField, agency ? NameRules.Initials(name) : NameRules.GangId(name));
        Auto(Main, CreatorCatalog.ShortNameField, agency ? Id : NameRules.ShortName(name));
        var shortName = Main.Get(CreatorCatalog.ShortNameField).Trim();
        var memberWord = NameRules.LastWord(Main.Original(CreatorCatalog.MemberNameField));
        Auto(Main, CreatorCatalog.MemberNameField, $"{shortName} {memberWord}".Trim());
        Auto(Main, CreatorCatalog.ContactNameField, shortName);
        Auto(Main, CreatorCatalog.PlatePrefixField, NameRules.PlatePrefix(name));
    }

    private void RefreshLocation(EntryDraft draft, string name, bool full = true)
    {
        var templateName = draft.Original(CreatorCatalog.NameField);
        if (full)
        {
            var templateFull = draft.Original(CreatorCatalog.FullNameField);
            Auto(draft, CreatorCatalog.FullNameField, templateFull == templateName ? name : NameRules.Replace(templateFull, templateName, name));
        }

        Auto(draft, CreatorCatalog.PromptField, NameRules.Replace(draft.Original(CreatorCatalog.PromptField), templateName, name));
    }

    private void Auto(EntryDraft draft, string field, string value)
    {
        if (draft == Main && _manual.Contains(field) || !draft.Has(field))
        {
            return;
        }

        draft.Set(field, value);
    }

    private void AddGangChanges(List<CreatorChange> changes, string mainFile, Func<string, string>? label)
    {
        var enemies = Enemies.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var friends = Friends.Except(Enemies, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var mutual = MutualRelations ? " (they get this gang in their list too)" : string.Empty;
        if (enemies.Count > 0)
        {
            changes.Add(new(mainFile, $"Enemies: {string.Join(", ", enemies)}{mutual}"));
        }

        if (friends.Count > 0)
        {
            changes.Add(new(mainFile, $"Friends: {string.Join(", ", friends)}{mutual}"));
        }

        if (DenEnabled && Den is not null)
        {
            var denFile = Engine.FileOf(Den.Template);
            changes.Add(new(denFile, $"New gang den \"{DenName}\", based on {Describe(Den)}"));
            AddLinkChanges(changes, Den, denFile);
            AddPlacementChange(changes, Den, denFile, "den");
        }

        AddArea(changes, CreatorEngine.Territories, Zones, "zone", label);
    }

    private void AddLinkChanges(List<CreatorChange> changes, EntryDraft draft, string file)
    {
        foreach (var link in draft.Links)
        {
            if (link.Copy)
            {
                var group = Group(link);
                var menu = CreatorCatalog.IsMenu(link.Link.Kind);
                var size = group is null ? 0 : menu ? MenuEditing.ItemCount(group) : CreatorEngine.GroupSize(group);
                var noun = menu ? size == 1 ? "item" : "items" : size == 1 ? "entry" : "entries";
                var edited = menu && link.Content is not null ? ", items edited" : string.Empty;
                changes.Add(new(Engine.FileOf(link.Link.Kind.FileBase), $"New group {link.NewId.Trim()} for {link.Link.Label.ToLowerInvariant()} – copy of {link.SourceId} ({size} {noun}{edited})"));
            }
            else
            {
                changes.Add(new(file, $"{link.Link.Label}: uses {(link.SourceId.Length > 0 ? link.SourceId : "nothing")}"));
            }
        }
    }

    private static void AddPlacementChange(List<CreatorChange> changes, EntryDraft draft, string file, string title)
    {
        if (draft.Placement is not { } placement)
        {
            return;
        }

        var moved = draft.MoveAttached && draft.AttachedPoints > 0 && draft.Origin is { } origin && !origin.Equals(placement)
            ? $", {draft.AttachedPoints} spawn / camera points moved along"
            : string.Empty;
        changes.Add(new(file, $"Placed at {placement}{moved}"));
    }

    private void AddArea(List<CreatorChange> changes, AreaRows rows, HashSet<string> keys, string noun, Func<string, string>? label)
    {
        if (keys.Count > 0)
        {
            var list = string.Join(", ", keys.Select(k => label?.Invoke(k) ?? k).Order(StringComparer.CurrentCultureIgnoreCase));
            changes.Add(new(Engine.FileOf(rows.FileBase), $"{keys.Count} {noun}{(keys.Count == 1 ? string.Empty : "s")}: {list}"));
        }
    }

    public static string MenuTitle(LinkChoice link, bool den) => den ? "Den " + link.Link.Label.ToLowerInvariant() : link.Link.Label;

    private void AddInteriorProblem(List<CreatorProblem> problems, EntryDraft draft, CreatorStep step)
    {
        if (!draft.OwnerChanged || draft.OriginalOwner.Length == 0)
        {
            return;
        }

        var interior = draft.Get(CreatorCatalog.InteriorField).Trim();
        if (interior.Length > 0 && interior != "-1" && Engine.InteriorAssociations(interior).Contains(draft.OriginalOwner, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add(new(step, $"Interior {interior} still spawns {draft.OriginalOwner} people inside – it is shared in Interiors.xml and stays as it is.", false));
        }
    }

    private static string Nobody(string owner) => owner.Length > 0 ? owner : "nobody";

    private static string Describe(EntryDraft draft) =>
        draft.Kind.IdField is { } field ? draft.Original(field) : $"\"{draft.Original(CreatorCatalog.NameField)}\"";

    private static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
