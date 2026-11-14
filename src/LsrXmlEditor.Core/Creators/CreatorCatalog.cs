namespace LsrXmlEditor.Core.Creators;

public sealed record GroupKind(string FileBase, string Element, string IdElement);

public sealed record LinkField(string Field, string Label, GroupKind Kind, string Suffix, bool CopyByDefault);

public sealed record EntityKind(
    string Title,
    string FileBase,
    string Element,
    string? IdField,
    IReadOnlyList<string> TextFields,
    IReadOnlyList<LinkField> Links);

public static class CreatorCatalog
{
    public const string LocationsFile = "Locations";
    public const string ZonesFile = "Zones";
    public const string GangTerritoriesFile = "GangTerritories";
    public const string ZoneJurisdictionsFile = "ZoneJurisdictions";
    public const string CountyJurisdictionsFile = "CountyJurisdictions";
    public const string GangDenElement = "GangDen";
    public const string AssociationField = "AssignedAssociationID";
    public const string MenuField = "MenuID";

    public static readonly GroupKind Peds = new("DispatchablePeople", "DispatchablePersonGroup", "DispatchablePersonGroupID");
    public static readonly GroupKind Vehicles = new("DispatchableVehicles", "DispatchableVehicleGroup", "DispatchableVehicleGroupID");
    public static readonly GroupKind Weapons = new("IssuableWeapons", "IssuableWeaponsGroup", "IssuableWeaponsID");
    public static readonly GroupKind Heads = new("Heads", "HeadDataGroup", "HeadDataGroupID");
    public static readonly GroupKind ShopMenu = new("ShopMenus", "ShopMenu", "ID");
    public static readonly GroupKind ShopMenuGroup = new("ShopMenus", "ShopMenuGroup", "ID");

    public static readonly EntityKind Gang = new(
        "Gang",
        "Gangs",
        "Gang",
        "ID",
        ["FullName", "ShortName", "MemberName", "ContactName", "DenName", "LicensePlatePrefix"],
        [
            new("PersonnelID", "Gang members", Peds, "Peds", true),
            new("VehiclesID", "Vehicles", Vehicles, "Vehicles", true),
            new("SideArmsID", "Sidearms", Weapons, "Sidearms", true),
            new("LongGunsID", "Long guns", Weapons, "LongGuns", true),
            new("MeleeWeaponsID", "Melee weapons", Weapons, "Melee", false),
            new("HeadDataGroupID", "Heads", Heads, "Heads", false),
            new("DealerMenuGroup", "Dealer menu", ShopMenuGroup, "DealerMenu", false)
        ]);

    public static readonly EntityKind Agency = new(
        "Agency",
        "Agencies",
        "Agency",
        "ID",
        ["FullName", "ShortName", "MemberName", "LicensePlatePrefix"],
        [
            new("PersonnelID", "Officers", Peds, "Peds", true),
            new("VehiclesID", "Vehicles", Vehicles, "Vehicles", true),
            new("SideArmsID", "Sidearms", Weapons, "Sidearms", true),
            new("LongGunsID", "Long guns", Weapons, "LongGuns", true),
            new("LessLethalWeaponsID", "Less lethal weapons", Weapons, "LessLethal", false),
            new("HeadDataGroupID", "Heads", Heads, "Heads", false),
            new("OffDutyPersonnelID", "Off duty officers", Peds, "OffDutyPeds", false),
            new("OffDutyVehiclesID", "Off duty vehicles", Vehicles, "OffDutyVehicles", false)
        ]);

    public static readonly IReadOnlyList<LinkField> LocationLinks =
    [
        new(MenuField, "Shop menu", ShopMenu, "Menu", true)
    ];

    public static readonly IReadOnlyList<string> LocationTextFields = ["Name", "FullName", "Description"];

    public static EntityKind Location(string element) =>
        new("Location", LocationsFile, element, null, LocationTextFields, LocationLinks);
}
