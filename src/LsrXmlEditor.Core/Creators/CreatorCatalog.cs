namespace LsrXmlEditor.Core.Creators;

public enum CreatorMode
{
    Gang,
    GangDen,
    Location,
    Agency
}

public sealed record GroupKind(string FileBase, string Element, string IdElement);

public sealed record LinkField(string Field, string Label, GroupKind Kind, string Suffix, bool CopyByDefault);

public sealed record EntityKind(
    string Title,
    string FileBase,
    string Element,
    string? IdField,
    string NameField,
    string? ClassField,
    IReadOnlyList<string> TextFields,
    IReadOnlyList<LinkField> Links);

public sealed record LocationType(string Container, string Element, string Title, int Count);

public sealed record ZoneInfo(string Code, string Name, string County);

public sealed record SavedPosition(string Title, Placement Placement);

public sealed record CreatorData(IReadOnlyList<LocationType> LocationTypes, IReadOnlyList<ZoneInfo> Zones, IReadOnlyList<SavedPosition> SavedPositions)
{
    public IReadOnlyList<string> Counties { get; } =
        [.. Zones.Select(z => z.County).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)];
}

public static class CreatorCatalog
{
    public const string LocationsFile = "Locations";
    public const string ZonesFile = "Zones";
    public const string SaveGamesFile = "SaveGames";
    public const string ModItemsFile = "ModItems";
    public const string InteriorsFile = "Interiors";
    public const string GangTerritoriesFile = "GangTerritories";
    public const string ZoneJurisdictionsFile = "ZoneJurisdictions";
    public const string CountyJurisdictionsFile = "CountyJurisdictions";
    public const string GangDensContainer = "GangDens";
    public const string GangDenElement = "GangDen";
    public const string AssociationField = "AssignedAssociationID";
    public const string SpawnAssociationField = "AssociationID";
    public const string InteriorField = "InteriorID";
    public const string InteriorIdField = "LocalID";
    public const string PrimaryDenField = "IsPrimaryGangDen";
    public const string MenuField = "MenuID";
    public const string IdField = "ID";
    public const string FullNameField = "FullName";
    public const string ShortNameField = "ShortName";
    public const string MemberNameField = "MemberName";
    public const string ContactNameField = "ContactName";
    public const string DenNameField = "DenName";
    public const string PlatePrefixField = "LicensePlatePrefix";
    public const string NameField = "Name";
    public const string PromptField = "ButtonPromptText";
    public const string ColorPrefixField = "ColorPrefix";
    public const string ColorField = "ColorString";
    public const string EnemiesField = "EnemyGangs";
    public const string FriendsField = "FriendlyGangs";

    public static readonly GroupKind Peds = new("DispatchablePeople", "DispatchablePersonGroup", "DispatchablePersonGroupID");
    public static readonly GroupKind Vehicles = new("DispatchableVehicles", "DispatchableVehicleGroup", "DispatchableVehicleGroupID");
    public static readonly GroupKind Weapons = new("IssuableWeapons", "IssuableWeaponsGroup", "IssuableWeaponsID");
    public static readonly GroupKind Heads = new("Heads", "HeadDataGroup", "HeadDataGroupID");
    public static readonly GroupKind ShopMenu = new("ShopMenus", "ShopMenu", "ID");
    public static readonly GroupKind ShopMenuGroup = new("ShopMenus", "ShopMenuGroup", "ID");
    public static readonly GroupKind ShopMenuGroupContainer = new("ShopMenus", "ShopMenuGroupContainer", "ID");
    public static readonly GroupKind ClothingMenu = new("ShopMenus", "PedClothingShopMenu", "ID");
    public static readonly GroupKind BarberMenu = new("ShopMenus", "PedVariationShopMenu", "ID");
    public static readonly GroupKind ModShopMenu = new("ShopMenus", "VehicleVariationShopMenu", "ID");
    public static readonly GroupKind Treatments = new("ShopMenus", "TreatmentOptions", "ID");

    public static readonly EntityKind Gang = new(
        "gang",
        "Gangs",
        "Gang",
        IdField,
        FullNameField,
        "GangClassification",
        [FullNameField, ShortNameField, MemberNameField, ContactNameField, DenNameField, PlatePrefixField],
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
        "agency",
        "Agencies",
        "Agency",
        IdField,
        FullNameField,
        "Classification",
        [FullNameField, ShortNameField, MemberNameField, PlatePrefixField],
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

    public static readonly EntityKind Organization = new("organization", "Organizations", "Organization", IdField, FullNameField, null, [], []);

    public static readonly EntityKind TaxiFirm = new("taxi firm", "Organizations", "TaxiFirm", IdField, FullNameField, null, [], []);

    public static readonly LinkField MenuLink = new(MenuField, "Shop menu", ShopMenu, "Menu", true);

    public static readonly IReadOnlyList<LinkField> LocationLinks =
    [
        MenuLink,
        new("PedClothingShopMenuID", "Clothing menu", ClothingMenu, "Clothes", false),
        new("PedVariationShopMenuID", "Barber menu", BarberMenu, "Barber", false),
        new("VehicleVariationShopMenuID", "Mod shop menu", ModShopMenu, "ModShop", false),
        new("TreatmentOptionsID", "Treatments", Treatments, "Treatments", false),
        new("VendorPersonnelID", "Staff", Peds, "StaffPeds", false),
        new("VendorMeleeWeaponsID", "Staff melee weapons", Weapons, "StaffMelee", false),
        new("VendorSidearmWeaponsID", "Staff sidearms", Weapons, "StaffSidearms", false),
        new("VendorLongGunWeaponsID", "Staff long guns", Weapons, "StaffLongGuns", false)
    ];

    public static IReadOnlyList<EntityKind> Owners { get; } = [Gang, Agency, Organization, TaxiFirm];

    public static EntityKind Location(string element, string title) =>
        new(title, LocationsFile, element, null, NameField, null, [NameField, FullNameField, "Description", PromptField], LocationLinks);

    public static EntityKind Den { get; } = Location(GangDenElement, "gang den");

    public static bool IsMenu(GroupKind kind) => kind == ShopMenu || kind == ShopMenuGroup;

    public static IReadOnlyList<string> FieldsFor(GroupKind kind) =>
        [.. Gang.Links.Concat(Agency.Links).Concat(LocationLinks).Where(l => l.Kind == kind).Select(l => l.Field).Distinct()];
}
