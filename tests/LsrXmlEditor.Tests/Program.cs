using System.Diagnostics;
using System.Xml.Linq;
using LsrXmlEditor.Core.Backups;
using LsrXmlEditor.Core.Changes;
using LsrXmlEditor.Core.Creators;
using LsrXmlEditor.Core.Files;
using LsrXmlEditor.Core.Search;
using LsrXmlEditor.Core.Workspace;
using LsrXmlEditor.Core.Xml;

if (args.Length == 0 || !Directory.Exists(args[0]))
{
    Console.WriteLine("Usage: LsrXmlEditor.Tests <folder with LSR xml files>");
    return 2;
}

var source = args[0];
var failures = 0;

void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {name}");
    if (!condition)
    {
        failures++;
    }
}

var work = Path.Combine(Path.GetTempPath(), "LsrXmlEditorTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);

try
{
    var files = FileScanner.Discover(source, includeSubfolders: true);
    Check(files.Count > 0, $"discover ({files.Count} files)");

    var watch = Stopwatch.StartNew();
    var stats = files.AsParallel().Select(f => (File: f, Stats: FileScanner.Analyze(f.FullPath))).ToList();
    Console.WriteLine($"      analyze: {watch.ElapsedMilliseconds} ms");
    foreach (var (file, stat) in stats.Where(s => !s.File.IsVariant))
    {
        Console.WriteLine($"      {file.FileName,-28} {stat.RootName,-34} {stat.EntryCount,6}  {string.Join(", ", stat.Categories.Take(4).Select(c => $"{c.Name}:{c.Count}"))}");
    }

    Check(stats.All(s => s.Stats.Error is null), "analyze without errors");

    var variants = files.Where(f => f.IsVariant).ToList();
    Check(variants.Any(v => v.FileName.StartsWith("Agencies+_", StringComparison.Ordinal) || v.BaseName == "DispatchableVehicles"), $"variants detected ({variants.Count})");

    foreach (var file in files)
    {
        var copy = Path.Combine(work, "roundtrip.xml");
        var document = XmlConfigDocument.Load(file.FullPath);
        document.WriteTo(copy);
        var identical = File.ReadAllBytes(file.FullPath).AsSpan().SequenceEqual(File.ReadAllBytes(copy));
        if (!identical && XNode.DeepEquals(XmlConfigDocument.ParseFile(file.FullPath), XmlConfigDocument.ParseFile(copy)))
        {
            Console.WriteLine($"NOTE  roundtrip only reformats markup: {file.RelativePath}");
        }
        else if (!identical)
        {
            Check(false, $"roundtrip identical: {file.RelativePath}");
        }

        var skeleton = stats.First(s => s.File == file).Stats;
        var full = document.Categories.Sum(c => c.Count);
        if (full != skeleton.EntryCount)
        {
            Check(false, $"skeleton count matches full count: {file.RelativePath} ({skeleton.EntryCount} vs {full})");
        }
    }

    Check(true, "roundtrip + count checks completed");

    var quoted = Path.Combine(work, "quoted.xml");
    File.WriteAllText(quoted, "<?xml version='1.0' encoding='utf-8'?>\r\n<Root>\r\n  <Value>1</Value>\r\n</Root>\r\n");
    var quotedDocument = XmlConfigDocument.Load(quoted);
    XmlEditing.SetValue(quotedDocument.Root.Element("Value")!, "2");
    quotedDocument.WriteTo(quoted);
    Check(File.ReadAllText(quoted) == "<?xml version='1.0' encoding='utf-8'?>\r\n<Root>\r\n  <Value>2</Value>\r\n</Root>\r\n", "single-quoted declaration kept after an edit");

    var folder = Path.Combine(work, "data");
    Directory.CreateDirectory(folder);
    File.Copy(Path.Combine(source, "Agencies.xml"), Path.Combine(folder, "Agencies.xml"));
    var pristine = Path.Combine(work, "Agencies.pristine.xml");
    File.Copy(Path.Combine(source, "Agencies.xml"), pristine);

    var backups = new BackupService(Path.Combine(work, "backups"));
    var changeLog = new ChangeLogStore(Path.Combine(work, "changes.json"));
    var saver = new ConfigSaver(folder, backups, changeLog);

    var agencies = XmlConfigDocument.Load(Path.Combine(folder, "Agencies.xml"));
    var category = agencies.Categories.Single();
    var lspd = category.Entries.First();
    var spawnLimit = lspd.Element("SpawnLimit")!;
    var editedLimit = (int.Parse(spawnLimit.Value) + 1).ToString();
    XmlEditing.SetValue(spawnLimit, editedLimit);
    var clone = XmlEditing.CloneAfter(lspd);
    XmlEditing.SetValue(clone.Element("ID")!, "LSPD_TEST");
    XmlEditing.Remove(category.Entries.Last());
    Check(agencies.IsDirty, "document dirty after edit");

    var result = saver.Save(agencies);
    Check(result.Backup is not null && File.Exists(result.Backup.BackupPath), "backup created");
    Check(result.Changes?.Operations.Count == 3, $"diff produced 3 operations ({result.Changes?.Operations.Count})");
    foreach (var op in result.Changes?.Operations ?? [])
    {
        Console.WriteLine($"      {op.Kind}: {op.Describe()}");
    }

    var reloaded = XmlConfigDocument.Load(Path.Combine(folder, "Agencies.xml"));
    Check(reloaded.Categories.Single().Entries.First().Element("SpawnLimit")!.Value == editedLimit, "edited value persisted");
    Check(changeLog.Load().Count == 1, "change log stored");

    var fresh = XmlConfigDocument.Load(pristine);
    var replay = ChangeReplayer.Apply(fresh, changeLog.Load().SelectMany(c => c.Operations));
    Check(replay.All(r => r.Status == ReplayStatus.Applied), "replay applies cleanly on pristine copy");
    var replayPath = Path.Combine(work, "replayed.xml");
    fresh.WriteTo(replayPath);
    Check(File.ReadAllText(replayPath) == File.ReadAllText(Path.Combine(folder, "Agencies.xml")), "replayed file equals edited file");

    var replayAgain = ChangeReplayer.Apply(fresh, changeLog.Load().SelectMany(c => c.Operations));
    Check(replayAgain.All(r => r.Status == ReplayStatus.AlreadyApplied), "replay is idempotent");

    var restored = backups.Restore(backups.List().First(b => b.RelativePath == "Agencies.xml"), folder);
    Check(File.ReadAllText(Path.Combine(folder, "Agencies.xml")) == File.ReadAllText(pristine), "restore brings back original");
    Check(restored is not null, "restore keeps safety backup");

    var gangs = XmlConfigDocument.Load(Path.Combine(source, "Gangs.xml")).Categories.Single().Entries.First();
    var agency = XmlConfigDocument.Load(Path.Combine(source, "Agencies.xml")).Categories.Single().Entries.First();
    Check(GameReferences.Describe(gangs.Element("ID")!) is not null, "gang relationship group is a game reference");
    Check(GameReferences.Describe(agency.Element("ID")!) is null, "agency ID is not a game reference");
    Check(GameReferences.Describe(agency.Element("FullName")!) is null, "free text is not a game reference");

    var hits = new List<SearchHit>();
    watch.Restart();
    foreach (var file in files.Where(f => !f.IsVariant))
    {
        var root = XmlConfigDocument.ParseFile(file.FullPath).Root!;
        ConfigSearch.Run(root, file.FullPath, file.RelativePath, new SearchQuery("police4"), hits.Add, CancellationToken.None);
    }

    Console.WriteLine($"      search: {hits.Count} hits in {watch.ElapsedMilliseconds} ms");
    Check(hits.Count > 0, "global search finds police4");

    var multiDoc = XmlConfigDocument.Load(Path.Combine(source, "Agencies.xml"));
    var picked = multiDoc.Categories.Single().Entries.Take(3).ToList();
    var shared = SharedFields.Of(picked);
    Check(shared.Count > 0 && shared.All(f => f.Elements.Count == picked.Count), $"shared fields across 3 agencies ({shared.Count})");
    var idField = shared.First(f => f.Segment == "ID");
    Check(SharedFields.CommonValue(idField.Elements) is null, "differing IDs report no common value");
    var limitField = shared.First(f => f.Segment == "SpawnLimit");
    foreach (var element in limitField.Elements)
    {
        XmlEditing.SetValue(element, "7");
    }

    Check(SharedFields.CommonValue(limitField.Elements) == "7", "common value after writing all selected");
    Check(picked.All(p => p.Element("SpawnLimit")!.Value == "7"), "value written to every selected entry");
    Check(shared.All(f => f.IsLeaf || !StructureAnalyzer.IsCollection(f.Elements[0])), "lists are excluded from multi edit");

    var a = XElement.Parse("<E><A>1</A><B>x</B><C><D>1</D></C></E>");
    var b = XElement.Parse("<E><A>2</A><C><D>1</D></C><X>9</X></E>");
    var ab = SharedFields.Of([a, b]);
    Check(ab.Select(f => f.Segment).SequenceEqual(["A", "C"]), "only fields present in every entry are shared");
    var nested = SharedFields.Of(ab.Single(f => f.Segment == "C").Elements);
    Check(SharedFields.CommonValue(nested.Single().Elements) == "1", "nested equal values are common");

    var emptied = DocumentDiff.Compute(
        XElement.Parse("<Root><A><Item><ID>a</ID></Item><Item><ID>b</ID></Item></A><B><Item><ID>c</ID></Item><Item><ID>d</ID></Item></B></Root>"),
        XElement.Parse("<Root><A><Item><ID>a</ID></Item><Item><ID>b</ID></Item></A><B /></Root>"));
    Check(emptied.Count == 2 && emptied.All(o => o.Kind == ChangeKind.EntryRemoved && o.Category == "Root/B"), "emptied category reports removed entries");

    var logPath = Path.Combine(work, "corrupt.json");
    File.WriteAllText(logPath, "{ not json");
    var corruptLog = new ChangeLogStore(logPath);
    corruptLog.Append(new ChangeSet(Guid.NewGuid(), DateTime.Now, "x.xml", []));
    Check(Directory.GetFiles(work, "corrupt.json.corrupt-*").Length == 1 && corruptLog.Load().Count == 1, "corrupt change log is moved aside");

    var limited = new BackupService(Path.Combine(work, "limited"), keepPerFile: 2);
    var limitedFile = Path.Combine(folder, "Agencies.xml");
    for (var i = 0; i < 2; i++)
    {
        limited.Create(folder, limitedFile);
        Thread.Sleep(5);
    }

    var oldest = limited.List().Last();
    var restoreOldest = Record(() => limited.Restore(oldest, folder));
    Check(restoreOldest is null && limited.List().Count == 3, "restoring the oldest backup at the limit succeeds and keeps the original");

    var minimal = new BackupService(Path.Combine(work, "minimal"), keepPerFile: 0);
    for (var i = 0; i < 4; i++)
    {
        minimal.Create(folder, limitedFile);
        Thread.Sleep(5);
    }

    var minimalList = minimal.List();
    Check(minimalList.Count == 2 && minimalList[0].Timestamp > minimalList[1].Timestamp, "pruning keeps the newest and the original backup");

    var guarded = XmlConfigDocument.Load(limitedFile);
    XmlEditing.SetValue(guarded.Categories.Single().Entries.First().Element("SpawnLimit")!, "11");
    File.SetLastWriteTimeUtc(limitedFile, DateTime.UtcNow.AddMinutes(-5));
    Check(Record(() => saver.Save(guarded)) is ExternalChangeException && guarded.IsDirty, "external change blocks the save");
    Check(Record(() => saver.Save(guarded, overwriteExternalChanges: true)) is null && !guarded.IsDirty, "overwrite after external change saves");

    var racing = XmlConfigDocument.Load(limitedFile);
    var racingLimit = racing.Categories.Single().Entries.First().Element("SpawnLimit")!;
    XmlEditing.SetValue(racingLimit, "12");
    var snapshot = racing.CreateSnapshot();
    XmlEditing.SetValue(racingLimit, "13");
    saver.Write(snapshot, overwriteExternalChanges: false);
    racing.AcceptSave(snapshot);
    Check(racing.IsDirty, "edit made during a save keeps the document dirty");
    Check(!File.Exists(limitedFile + ".tmp"), "no temp file left after save");

    var broken = Path.Combine(folder, "Broken.xml");
    File.WriteAllText(broken, "<Root><Item><ID>a</ID></Item><Item><ID>b</ID></Item></Root>");
    var brokenDoc = XmlConfigDocument.Load(broken);
    XmlEditing.SetValue(brokenDoc.Categories.Single().Entries.First().Element("ID")!, "z");
    File.WriteAllText(broken, "<Root><Item>");
    File.SetLastWriteTimeUtc(broken, brokenDoc.LoadedWriteTime);
    var brokenResult = saver.Save(brokenDoc);
    Check(brokenResult.Changes is null && XmlConfigDocument.Load(broken).Root.Elements().First().Element("ID")!.Value == "z", "unparsable file on disk is still saved");

    Check(NameRules.GangId("The Test Crew") == "AMBIENT_GANG_TEST_CREW", "creator: gang id from name");
    Check(NameRules.Initials("Los Santos Police Department") == "LSPD", "creator: agency initials");
    Check(NameRules.Numbers("vector4(981.85, -103.02, 74.84, 220)").SequenceEqual([981.85, -103.02, 74.84, 220]), "creator: coordinates from clipboard text");
    Check(NameRules.Numbers("981,85 -103,02 74,84").SequenceEqual([981.85, -103.02, 74.84]), "creator: comma decimals");
    Check(NameRules.Numbers("1200,-300,30").SequenceEqual([1200, -300, 30]), "creator: commas between whole numbers separate them");
    Check(NameRules.Numbers("1200,5; -300,25; 30").SequenceEqual([1200.5, -300.25, 30]), "creator: comma decimals with semicolons");
    Check(NameRules.Token("Café Ñandú") == "CafeNandu" && NameRules.GangId("Straße") == "AMBIENT_GANG_STRASSE", "creator: accents are folded into ids");

    var place = XElement.Parse("<D><EntrancePosition><X>0</X><Y>0</Y><Z>0</Z></EntrancePosition><EntranceHeading>0</EntranceHeading><S><Location><X>0</X><Y>10</Y><Z>1</Z></Location><Heading>0</Heading></S></D>");
    Relocation.Move(place, new Placement(100, 100, 5, 90), attached: true);
    var spawn = place.Element("S")!;
    Check(spawn.Element("Location")!.Element("X")!.Value == "90" && spawn.Element("Location")!.Element("Y")!.Value == "100" && spawn.Element("Heading")!.Value == "90", "creator: spawn points turn with the entrance");

    var respawn = XElement.Parse("<D><EntrancePosition><X>0</X><Y>0</Y><Z>0</Z></EntrancePosition><EntranceHeading>0</EntranceHeading><RespawnLocation><X>0</X><Y>10</Y><Z>1</Z></RespawnLocation><RespawnHeading>10</RespawnHeading><PropHeading>0</PropHeading></D>");
    Relocation.Move(respawn, new Placement(0, 0, 0, 90), attached: true);
    Check(respawn.Element("RespawnHeading")!.Value == "100" && respawn.Element("PropHeading")!.Value == "0", "creator: respawn heading turns, unset headings stay");

    var placed = new EntryDraft(CreatorCatalog.Location("GamblingDen", "casino"), XElement.Parse("<GamblingDen><Name>C</Name><EntrancePosition><X>0</X><Y>0</Y><Z>0</Z></EntrancePosition><EntranceHeading>0</EntranceHeading><S><Location><X>0</X><Y>10</Y><Z>1</Z></Location><Heading>0</Heading></S></GamblingDen>"));
    placed.Placement = new Placement(100, 100, 5, 90);
    placed.Apply();
    placed.Apply();
    var placedSpawn = placed.Entry.Element("S")!.Element("Location")!;
    Check(placedSpawn.Element("X")!.Value == "90" && placedSpawn.Element("Y")!.Value == "100", "creator: applying twice moves only once");
    placed.MoveAttached = false;
    placed.Apply();
    Check(placedSpawn.Element("X")!.Value == "0" && placedSpawn.Element("Y")!.Value == "10" && placed.Entry.Element("EntrancePosition")!.Element("X")!.Value == "100", "creator: switching off move along puts the points back");
    XmlEditing.SetValue(placed.Entry.Element("EntrancePosition")!.Element("Z")!, "7");
    placed.Apply();
    Check(placed.Entry.Element("EntrancePosition")!.Element("Z")!.Value == "7", "creator: edits from all settings survive another apply");

    var relations = XDocument.Parse("<ArrayOfGang>\n  <Gang>\n    <ID>A</ID>\n    <EnemyGangs />\n  </Gang>\n</ArrayOfGang>", LoadOptions.PreserveWhitespace);
    CreatorEngine.AddToList(relations.Root!.Element("Gang")!, "EnemyGangs", "B");
    var listXml = relations.Root.Element("Gang")!.Element("EnemyGangs")!.ToString(SaveOptions.DisableFormatting).ReplaceLineEndings("\n");
    Check(listXml == "<EnemyGangs>\n      <string>B</string>\n    </EnemyGangs>", "creator: list entries keep the indentation");

    var owned = new EntryDraft(CreatorCatalog.Location("GamblingDen", "casino"), XElement.Parse("<GamblingDen><Name>C</Name><AssignedAssociationID>OLD</AssignedAssociationID><S><AssociationID>OLD</AssociationID></S><S><AssociationID>COPS</AssociationID></S></GamblingDen>"));
    owned.Owner = "MID";
    owned.Apply();
    owned.Owner = "NEW";
    owned.Apply();
    var spawnOwners = owned.Entry.Elements("S").Select(s => s.Element("AssociationID")!.Value).ToList();
    Check(owned.Entry.Element("AssignedAssociationID")!.Value == "NEW" && spawnOwners.SequenceEqual(["NEW", "COPS"]), "creator: spawns inside follow the new owner");
    var mixed = new EntryDraft(CreatorCatalog.Location("GamblingDen", "casino"), XElement.Parse("<GamblingDen><Name>C</Name><AssignedAssociationID>OLD</AssignedAssociationID><S><AssociationID>OLD</AssociationID></S><S><AssociationID>COPS</AssociationID></S></GamblingDen>"));
    mixed.Owner = "COPS";
    mixed.Apply();
    mixed.Owner = "NEW";
    mixed.Apply();
    Check(mixed.Entry.Elements("S").Select(s => s.Element("AssociationID")!.Value).SequenceEqual(["NEW", "COPS"]), "creator: switching owner twice keeps other spawns apart");

    var menu = XElement.Parse("<ShopMenu><ID>M</ID><Items><MenuItem><NumberOfItemsSoldToPlayer>4</NumberOfItemsSoldToPlayer><ModItemName>A</ModItemName><PurchasePrice>5</PurchasePrice></MenuItem></Items></ShopMenu>");
    var added = MenuEditing.Add(menu, "B", menu.Element("Items")!.Element("MenuItem"));
    MenuEditing.SetNumber(added, MenuEditing.BuyField, 12);
    Check(MenuEditing.ItemNames(menu).SequenceEqual(["A", "B"]) && MenuEditing.Number(added, MenuEditing.BuyField) == 12 && added.Element("NumberOfItemsSoldToPlayer")!.Value == "0", "creator: menu item added from a pattern");
    var blank = MenuEditing.Add(XElement.Parse("<ShopMenu><Items /></ShopMenu>"), "C", null);
    Check(MenuEditing.Text(blank, MenuEditing.ItemField) == "C" && MenuEditing.Number(blank, MenuEditing.StockField) == -1, "creator: blank menu item");

    var menus = XDocument.Parse("<ShopMenus><ShopMenuList><ShopMenu><ID>M</ID><Items /></ShopMenu></ShopMenuList></ShopMenus>");
    var engine = new CreatorEngine(f => f == "ShopMenus" ? [menus] : [], _ => "ShopMenus.xml");
    var edited = new XElement(engine.FindGroup(CreatorCatalog.ShopMenu, "M")!);
    MenuEditing.Add(edited, "X", null);
    engine.CopyGroup(CreatorCatalog.ShopMenu, "M", "M2", edited);
    Check(MenuEditing.ItemCount(engine.FindGroup(CreatorCatalog.ShopMenu, "M")!) == 0 && MenuEditing.ItemNames(engine.FindGroup(CreatorCatalog.ShopMenu, "M2")!).Single() == "X", "creator: edited menu copy leaves the original alone");

    var dealers = XDocument.Parse("<ShopMenus><ShopMenuGroupList><ShopMenuGroup><ID>G</ID><PossibleShopMenus><PercentageSelectShopMenu><ShopMenu><ID>D</ID><GroupName>G</GroupName></ShopMenu></PercentageSelectShopMenu></PossibleShopMenus></ShopMenuGroup></ShopMenuGroupList><ShopMenuGroupContainers><ShopMenuGroupContainer><ID>Area</ID><PercentageSelectGropMenuGroups><PercentageSelectGroupMenuContainer><ShopMenuGroupID>G</ShopMenuGroupID></PercentageSelectGroupMenuContainer></PercentageSelectGropMenuGroups></ShopMenuGroupContainer></ShopMenuGroupContainers></ShopMenus>");
    var dealerEngine = new CreatorEngine(f => f == "ShopMenus" ? [dealers] : [], _ => "ShopMenus.xml");
    var dealerCopy = dealerEngine.CopyGroup(CreatorCatalog.ShopMenuGroup, "G", "G2");
    Check(dealerCopy.Descendants("GroupName").Single().Value == "G2", "creator: copied dealer menus point to their new group");
    Check(dealerEngine.Users(CreatorCatalog.ShopMenuGroup, "G").Single() == "Area (area dealers)", "creator: used by counts area dealers");

    var places = XDocument.Parse("<PossibleLocations><GamblingDens><GamblingDen><Name>C</Name><MenuID>Missing</MenuID></GamblingDen></GamblingDens></PossibleLocations>");
    var placeEngine = new CreatorEngine(f => f == "Locations" ? [places] : f == "ShopMenus" ? [menus] : [], _ => "Locations.xml");
    var placeKind = CreatorCatalog.Location("GamblingDen", "casino");
    var missingDraft = new CreatorDraft(CreatorMode.Location, placeEngine, placeKind, placeEngine.Entries(placeKind).Single());
    var missingLink = missingDraft.Main.Links.Single(l => l.Link.Field == "MenuID");
    Check(!missingLink.Copy, "creator: a menu that does not exist is shared");
    missingLink.Copy = true;
    missingDraft.SetName("New");
    Check(missingDraft.Problems().Any(p => p.Blocking && p.Message.StartsWith("Missing", StringComparison.Ordinal)), "creator: copying a missing menu is blocked");
    Check(Record(() => missingDraft.Commit()) is InvalidOperationException && places.Root!.Descendants("GamblingDen").Count() == 1, "creator: a blocked commit changes nothing");

    var drops = XElement.Parse("<Root><Drops>" + string.Concat(Enumerable.Range(0, 5).Select(i => $"<Drop><Name>Dead Drop</Name><X>{i}</X></Drop>")) + "</Drops></Root>");
    var dropsAfter = new XElement(drops);
    dropsAfter.Element("Drops")!.Elements().ElementAt(1).Remove();
    XmlEditing.SetValue(dropsAfter.Element("Drops")!.Elements().ElementAt(2).Element("X")!, "33");
    var dropOps = DocumentDiff.Compute(drops, dropsAfter);
    Check(dropOps.Count == 2 && dropOps[0].Kind == ChangeKind.EntryRemoved && dropOps[1].Kind == ChangeKind.FieldChanged, $"diff: deleting one of several equal names logs one removal ({dropOps.Count})");
    var dropsTarget = Path.Combine(work, "drops.xml");
    File.WriteAllText(dropsTarget, "<Root><Drops><Drop><Name>Dead Drop</Name><X>9</X></Drop>" + string.Concat(Enumerable.Range(0, 5).Select(i => $"<Drop><Name>Dead Drop</Name><X>{i}</X></Drop>")) + "</Drops></Root>");
    var dropsDoc = XmlConfigDocument.Load(dropsTarget);
    var dropReplay = ChangeReplayer.Apply(dropsDoc, dropOps);
    var dropValues = dropsDoc.Root.Element("Drops")!.Elements().Select(e => e.Element("X")!.Value).ToList();
    Check(dropReplay.All(r => r.Status == ReplayStatus.Applied) && dropValues.SequenceEqual(["9", "0", "2", "33", "4"]), $"replay: duplicates are found by content ({string.Join(",", dropValues)})");

    var latin = Path.Combine(work, "latin.xml");
    File.WriteAllBytes(latin, [.. "<?xml version=\"1.0\" encoding=\"iso-8859-1\"?>\r\n<Root><Value>"u8, 0xFC, .. "</Value></Root>"u8]);
    var latinDoc = XmlConfigDocument.Load(latin);
    Check(latinDoc.Root.Element("Value")!.Value == "ü", "latin-1 file is read correctly");
    XmlEditing.SetValue(latinDoc.Root.Element("Value")!, "ä");
    latinDoc.WriteTo(latin);
    Check(File.ReadAllBytes(latin).Contains((byte)0xE4) && XmlConfigDocument.Load(latin).Root.Element("Value")!.Value == "ä", "latin-1 file keeps its encoding on save");
}
finally
{
    Directory.Delete(work, recursive: true);
}

Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;

static Exception? Record(Action action)
{
    try
    {
        action();
        return null;
    }
    catch (Exception ex)
    {
        return ex;
    }
}
