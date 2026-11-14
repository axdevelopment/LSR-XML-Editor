using System.Xml.Linq;

namespace LsrXmlEditor.Core.Xml;

public static class GameReferences
{
    private static readonly Dictionary<string, string> FieldKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ModelName"] = "GTA V model name",
        ["ModelItemID"] = "GTA V model name",
        ["PackageItemID"] = "GTA V model name",
        ["InternalGameName"] = "GTA V zone code",
        ["ZoneInternalGameName"] = "GTA V zone code",
        ["BoneName"] = "GTA V bone name",
        ["ZoneName"] = "GTA V ped body zone",
        ["SubZoneName"] = "GTA V ped body zone",
        ["CollectionName"] = "GTA V overlay collection",
        ["OverlayName"] = "GTA V overlay name",
        ["IconName"] = "GTA V texture name",
        ["VoiceName"] = "GTA V voice name",
        ["MaleFreeModeVoice"] = "GTA V voice name",
        ["FemaleFreeModeVoice"] = "GTA V voice name",
        ["PhoneOS"] = "GTA V scaleform name",
        ["OverLayEffect"] = "GTA V screen effect",
        ["EntitySetName"] = "GTA V interior entity set"
    };

    private static readonly (string Fragment, string Kind)[] FieldFragments =
    [
        ("Animation", "GTA V animation"),
        ("Dictionary", "GTA V animation dictionary"),
        ("ScaleformName", "GTA V scaleform name"),
        ("Ipl", "GTA V IPL name")
    ];

    private static readonly (string Prefix, string Kind)[] ValuePrefixes =
    [
        ("AMBIENT_GANG_", "GTA V relationship group"),
        ("WORLD_HUMAN_", "GTA V scenario"),
        ("PROP_HUMAN_", "GTA V scenario"),
        ("WEAPON_", "GTA V weapon name"),
        ("GADGET_", "GTA V weapon name"),
        ("MP_M_FREEMODE", "GTA V ped model"),
        ("MP_F_FREEMODE", "GTA V ped model"),
        ("A_M_", "GTA V ped model or voice"),
        ("A_F_", "GTA V ped model or voice"),
        ("S_M_", "GTA V ped model or voice"),
        ("S_F_", "GTA V ped model or voice"),
        ("G_M_", "GTA V ped model or voice"),
        ("G_F_", "GTA V ped model or voice"),
        ("U_M_", "GTA V ped model or voice"),
        ("U_F_", "GTA V ped model or voice"),
        ("CSB_", "GTA V ped model"),
        ("IG_", "GTA V ped model")
    ];

    public static string? Describe(XElement leaf)
    {
        if (leaf.HasElements)
        {
            return null;
        }

        var value = leaf.Value;
        if (value.Length == 0 || value.Contains(' '))
        {
            return null;
        }

        var name = leaf.Name.LocalName;
        if (FieldKinds.TryGetValue(name, out var kind))
        {
            return kind;
        }

        foreach (var (fragment, fragmentKind) in FieldFragments)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return fragmentKind;
            }
        }

        foreach (var (prefix, prefixKind) in ValuePrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return prefixKind;
            }
        }

        return null;
    }
}
