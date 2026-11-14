namespace LsrXmlEditor.Core.Files;

public sealed record ConfigInfo(string BaseName, string Group, string Title, string Description);

public static class KnownConfigs
{
    public const string OtherGroup = "Other";

    public static IReadOnlyList<string> Groups { get; } =
    [
        "Police & Dispatch",
        "Gangs & Contacts",
        "Locations & World",
        "Items, Weapons & Shops",
        "Vehicles",
        "Peds & Appearance",
        "Phone & Animations",
        "Settings",
        "Save Games",
        OtherGroup
    ];

    private static readonly ConfigInfo[] All =
    [
        new("Agencies", "Police & Dispatch", "Agencies", "Police, sheriff, EMS, fire and other agencies"),
        new("DispatchablePeople", "Police & Dispatch", "Dispatch Peds", "Ped groups spawned by agencies and gangs"),
        new("DispatchableVehicles", "Police & Dispatch", "Dispatch Vehicles", "Vehicle groups for agencies and gangs"),
        new("IssuableWeapons", "Police & Dispatch", "Issuable Weapons", "Weapon sets for officers and gangs"),
        new("CountyJurisdictions", "Police & Dispatch", "County Jurisdictions", "Which agency is responsible for which county"),
        new("ZoneJurisdictions", "Police & Dispatch", "Zone Jurisdictions", "Which agency is responsible for which zone"),
        new("Crimes", "Police & Dispatch", "Crimes", "Crimes, wanted levels and reactions"),
        new("Gangs", "Gangs & Contacts", "Gangs", "Gangs with reputation, payments, vehicles and weapons"),
        new("GangTerritories", "Gangs & Contacts", "Gang Territories", "Which gang is active in which zone"),
        new("Organizations", "Gangs & Contacts", "Organizations", "Companies and organizations such as taxi firms"),
        new("Contacts", "Gangs & Contacts", "Contacts", "Phone contacts such as gun dealers or exporters"),
        new("Locations", "Locations & World", "Locations", "Stores, stations, hospitals, residences and more"),
        new("LocationTypes", "Locations & World", "Counties & States", "Counties and states of the game world"),
        new("Zones", "Locations & World", "Zones", "Zones with county, economy and type"),
        new("Streets", "Locations & World", "Streets", "Street names and speed limits"),
        new("Interiors", "Locations & World", "Interiors", "Building interiors"),
        new("SpawnBlocks", "Locations & World", "Spawn Blocks", "Areas where nothing spawns"),
        new("ModItems", "Items, Weapons & Shops", "Items", "All items that can be bought, found or used"),
        new("ShopMenus", "Items, Weapons & Shops", "Shop Menus", "Store offers and prices"),
        new("PhysicalItems", "Items, Weapons & Shops", "Props", "Models for items in the world"),
        new("CraftableItems", "Items, Weapons & Shops", "Craftable Items", "Crafting recipes"),
        new("Itoxicants", "Items, Weapons & Shops", "Drugs & Alcohol", "Intoxicants and their effects"),
        new("Weapons", "Items, Weapons & Shops", "Weapons", "Weapon properties and categories"),
        new("PlateTypes", "Vehicles", "License Plates", "Plate types and vanity plates"),
        new("VehicleRaces", "Vehicles", "Races", "Race tracks and checkpoints"),
        new("Heads", "Peds & Appearance", "Heads & Hair", "Head, hair and face data"),
        new("PedGroups", "Peds & Appearance", "Ped Groups", "Ped relationship groups"),
        new("Names", "Peds & Appearance", "Names", "First and last names for peds"),
        new("SavedOutfits", "Peds & Appearance", "Saved Outfits", "Outfits saved by the player"),
        new("Cellphones", "Phone & Animations", "Phones", "Phone models"),
        new("Dances", "Phone & Animations", "Dances", "Dance animations"),
        new("Gestures", "Phone & Animations", "Gestures", "Gesture animations"),
        new("Speeches", "Phone & Animations", "Speeches", "Speech lines"),
        new("Settings", "Settings", "Mod Settings", "All Los Santos RED settings"),
        new("SaveGames", "Save Games", "Save Games", "Saved games")
    ];

    private static readonly Dictionary<string, ConfigInfo> ByName = All.ToDictionary(c => c.BaseName, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SaveDataFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SaveGames",
        "SavedOutfits"
    };

    public static IEnumerable<string> BaseNames => ByName.Keys;

    public static ConfigInfo Describe(string baseName) =>
        ByName.TryGetValue(baseName, out var info) ? info : new ConfigInfo(baseName, OtherGroup, baseName, string.Empty);

    public static int Order(string baseName) => Array.FindIndex(All, c => string.Equals(c.BaseName, baseName, StringComparison.OrdinalIgnoreCase)) is var index and >= 0 ? index : int.MaxValue;

    public static bool IsSaveData(string baseName) => SaveDataFiles.Contains(baseName);
}
