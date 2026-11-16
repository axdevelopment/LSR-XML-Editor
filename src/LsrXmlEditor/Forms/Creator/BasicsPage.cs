using System.Xml.Linq;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Infrastructure;

namespace LsrXmlEditor.Forms.Creator;

internal sealed class BasicsPage : CreatorPage
{
    private static readonly Dictionary<string, string> Captions = new()
    {
        [CreatorCatalog.ShortNameField] = "Short name",
        [CreatorCatalog.MemberNameField] = "Member name",
        [CreatorCatalog.ContactNameField] = "Phone contact",
        [CreatorCatalog.DenNameField] = "Den is called",
        [CreatorCatalog.PlatePrefixField] = "Plate prefix",
        [CreatorCatalog.FullNameField] = "Full name",
        [CreatorCatalog.PromptField] = "Prompt text"
    };

    private readonly CreatorMode _mode;
    private readonly CreatorEngine _engine;
    private readonly CreatorData _data;
    private readonly Func<XElement, EntityKind, CreatorDraft> _rebase;
    private readonly Dictionary<string, TextBox> _boxes = [];
    private LocationType? _type;
    private CreatorDraft? _draft;
    private TextBox? _name;
    private Control? _refocus;
    private string? _refocusKey;

    public BasicsPage(CreatorMode mode, CreatorEngine engine, CreatorData data, Func<XElement, EntityKind, CreatorDraft> rebase)
        : base(CreatorStep.Basics, "Basics", Intro(mode))
    {
        _mode = mode;
        _engine = engine;
        _data = data;
        _rebase = rebase;
        _type = data.LocationTypes.FirstOrDefault();
    }

    public EntityKind InitialKind => _mode switch
    {
        CreatorMode.Gang => CreatorCatalog.Gang,
        CreatorMode.Agency => CreatorCatalog.Agency,
        CreatorMode.GangDen => CreatorCatalog.Den,
        _ => LocationKind(_type)
    };

    public void FocusInitial()
    {
        if (_refocus is { IsDisposed: false } control)
        {
            control.Focus();
        }
        else
        {
            _name?.Focus();
        }

        _refocus = null;
    }

    protected override void Bind(CreatorDraft draft)
    {
        _draft = draft;
        _refocus = null;
        Clear();
        _boxes.Clear();
        var kind = draft.Kind;

        AddSection("Start from");
        if (_mode == CreatorMode.Location)
        {
            var types = Combo();
            types.Items.AddRange([.. _data.LocationTypes.Select(t => new Choice<LocationType>(t, $"{t.Title} ({t.Count})"))]);
            types.SelectedIndex = Math.Max(0, _data.LocationTypes.ToList().FindIndex(t => t.Element == kind.Element));
            types.SelectedIndexChanged += (_, _) =>
            {
                if (!Loading && types.SelectedItem is Choice<LocationType> choice && _engine.Entries(LocationKind(choice.Value)).FirstOrDefault() is { } first)
                {
                    _type = choice.Value;
                    _refocusKey = "type";
                    _rebase(first, LocationKind(choice.Value));
                }
            };
            AddRow("&Type:", types);
            Remember("type", types);
        }

        if (_mode == CreatorMode.GangDen)
        {
            AddOwner(draft);
        }

        var template = Combo();
        var entries = _engine.Entries(kind).ToList();
        template.Items.AddRange([.. entries.Select(e => new Choice<XElement>(e, Describe(e, kind)))]);
        template.SelectedIndex = entries.IndexOf(draft.Main.Template);
        template.SelectedIndexChanged += (_, _) =>
        {
            if (!Loading && template.SelectedItem is Choice<XElement> choice)
            {
                _refocusKey = "template";
                _rebase(choice.Value, kind);
            }
        };
        AddRow("&Copy of:", template);
        Remember("template", template);
        AddHint($"Every setting you don't change here is taken over from this {kind.Title}.");
        if (_mode == CreatorMode.Location && draft.Main.HasOwner)
        {
            AddRow("&Run by:", OwnerPicker(draft));
            AddHint("Who the place belongs to. Spawns inside it that belonged to the old owner switch over too.");
        }

        AddSection("Name", kind.IdField is not null ? "Type the name – the ID and the other names fill in by themselves. You can still overwrite each of them." : null);
        _name = AddRow("&Name:", Field(draft.Name, v => { draft.SetName(v); Sync(); }));
        if (kind.IdField is { } idField)
        {
            AddText(draft, idField, "&ID");
        }

        foreach (var field in kind.TextFields.Where(f => f != kind.NameField && draft.Main.Has(f)))
        {
            AddText(draft, field, Captions.GetValueOrDefault(field) ?? DisplayNames.Humanize(field));
        }

        if (kind.ClassField is { } classField && draft.Main.Has(classField))
        {
            AddSection("Look");
            var classes = Combo(true);
            classes.Items.AddRange([.. _engine.Values(kind, classField)]);
            classes.Text = draft.Main.Get(classField);
            classes.TextChanged += (_, _) =>
            {
                if (!Loading)
                {
                    draft.SetField(classField, classes.Text.Trim());
                    OnChanged();
                }
            };
            AddRow("C&lassification:", classes);
        }

        if (draft.Main.Has(CreatorCatalog.ColorField))
        {
            AddRow("C&olor:", ColorPicker(draft));
        }

        if (_mode == CreatorMode.GangDen)
        {
            AddSpanning(Check("This is the gang's &main den (used for the map blip and gang jobs)", draft.PrimaryDen, v => draft.PrimaryDen = v));
        }
    }

    private void AddOwner(CreatorDraft draft)
    {
        var owner = Combo();
        var gangs = _engine.Entries(CreatorCatalog.Gang).ToList();
        owner.Items.AddRange([.. gangs.Select(g => new Choice<XElement>(g, Describe(g, CreatorCatalog.Gang)))]);
        owner.SelectedIndex = gangs.FindIndex(g => g.Element(CreatorCatalog.IdField)?.Value.Trim() == draft.OwnerGangId);
        owner.SelectedIndexChanged += (_, _) =>
        {
            if (Loading || owner.SelectedItem is not Choice<XElement> choice)
            {
                return;
            }

            var gangId = choice.Value.Element(CreatorCatalog.IdField)?.Value.Trim() ?? string.Empty;
            var dens = _engine.Entries(CreatorCatalog.Den).Where(d => d.Element(CreatorCatalog.AssociationField)?.Value.Trim() == gangId).ToList();
            _refocusKey = "owner";
            var current = dens.FirstOrDefault() is { } den && den != draft.Main.Template ? _rebase(den, CreatorCatalog.Den) : draft;
            current.OwnerGangId = gangId;
            current.PrimaryDen = !dens.Any(d => d.Element(CreatorCatalog.PrimaryDenField)?.Value == "true");
            if (current == draft)
            {
                BeginInvoke(() =>
                {
                    Present(current);
                    FocusInitial();
                });
            }

            OnChanged();
        };
        AddRow("&Gang:", owner);
        Remember("owner", owner);
    }

    private void Remember(string key, Control control)
    {
        if (_refocusKey == key)
        {
            _refocus = control;
            _refocusKey = null;
        }
    }

    private ComboBox OwnerPicker(CreatorDraft draft)
    {
        var picker = Combo();
        var owner = draft.Main.Owner.Trim();
        var choices = new List<Choice<string>> { new(string.Empty, "(nobody)") };
        choices.AddRange(_engine.OwnerChoices().Select(o => new Choice<string>(o.Id, o.Title)));
        if (owner.Length > 0 && !choices.Any(c => string.Equals(c.Value, owner, StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new(owner, $"{owner} (not found)"));
        }

        picker.Items.AddRange([.. choices]);
        picker.SelectedIndex = choices.FindIndex(c => string.Equals(c.Value, owner, StringComparison.OrdinalIgnoreCase));
        picker.Width = LogicalToDeviceUnits(UiTheme.Grow(420));
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!Loading && picker.SelectedItem is Choice<string> choice)
            {
                draft.Main.Owner = choice.Value;
                OnChanged();
            }
        };
        return picker;
    }

    private void AddText(CreatorDraft draft, string field, string caption)
    {
        var box = Field(draft.Main.Get(field), v => { draft.SetField(field, v); Sync(); });
        _boxes[field] = box;
        AddRow(caption + ":", box);
    }

    private void Sync()
    {
        if (_draft is null)
        {
            return;
        }

        foreach (var (field, box) in _boxes.Where(b => !b.Value.Focused))
        {
            var value = _draft.Main.Get(field);
            if (box.Text != value)
            {
                Quietly(() => box.Text = value);
            }
        }
    }

    private Control ColorPicker(CreatorDraft draft)
    {
        var colors = _engine.Colors();
        var combo = Combo();
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.Items.AddRange([.. colors.Select(c => new Choice<(string Prefix, string Color)>(c, c.Color))]);
        combo.SelectedIndex = colors.ToList().FindIndex(c => string.Equals(c.Color, draft.Main.Get(CreatorCatalog.ColorField), StringComparison.OrdinalIgnoreCase));
        combo.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index >= 0 && combo.Items[e.Index] is Choice<(string Prefix, string Color)> choice)
            {
                var swatch = new Rectangle(e.Bounds.X + 3, e.Bounds.Y + 2, e.Bounds.Height - 4, e.Bounds.Height - 4);
                using (var brush = new SolidBrush(Color.FromName(choice.Value.Color) is { IsKnownColor: true } known ? known : UiTheme.Surface))
                {
                    e.Graphics.FillRectangle(brush, swatch);
                }

                using (var pen = new Pen(UiTheme.Muted))
                {
                    e.Graphics.DrawRectangle(pen, swatch);
                }
                TextRenderer.DrawText(e.Graphics, $"{choice.Text}  {choice.Value.Prefix}", e.Font, new Point(swatch.Right + 6, e.Bounds.Y + 1), e.ForeColor);
            }

            e.DrawFocusRectangle();
        };
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (!Loading && combo.SelectedItem is Choice<(string Prefix, string Color)> choice)
            {
                draft.SetField(CreatorCatalog.ColorField, choice.Value.Color);
                draft.SetField(CreatorCatalog.ColorPrefixField, choice.Value.Prefix);
                OnChanged();
            }
        };
        return combo;
    }

    private string Describe(XElement entry, EntityKind kind)
    {
        var key = entry.Element(kind.IdField ?? kind.NameField)?.Value.Trim() ?? string.Empty;
        var name = entry.Element(CreatorCatalog.FullNameField)?.Value.Trim();
        var text = string.IsNullOrEmpty(name) || name == key ? key : $"{key} – {name}";
        if (entry.Element(CreatorCatalog.AssociationField)?.Value.Trim() is { Length: > 0 } owner && kind == CreatorCatalog.Den)
        {
            text += $" ({owner})";
        }

        return _engine.SourceLabel(entry, kind.FileBase) is { } file ? $"{text} · {file}" : text;
    }

    private static EntityKind LocationKind(LocationType? type) =>
        CreatorCatalog.Location(type?.Element ?? string.Empty, type is null ? "location" : DisplayNames.Humanize(type.Element).ToLowerInvariant());

    private static string Intro(CreatorMode mode) => mode switch
    {
        CreatorMode.Gang => "Pick a gang to start from and give the new one a name. That is all you need – the other steps are optional.",
        CreatorMode.Agency => "Pick an agency to start from and give the new one a name. That is all you need – the other steps are optional.",
        CreatorMode.GangDen => "Pick the gang and the den to copy, then give the new den a name.",
        _ => "Pick what kind of place you want, the place to copy and give it a name."
    };
}
