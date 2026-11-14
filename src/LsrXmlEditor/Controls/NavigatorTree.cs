using LsrXmlEditor.Core.Files;
using LsrXmlEditor.Core.Text;
using LsrXmlEditor.Infrastructure;
using LsrXmlEditor.Services;

namespace LsrXmlEditor.Controls;

public abstract record NavTarget;

public sealed record RootTarget : NavTarget;

public sealed record GroupTarget(string Group) : NavTarget;

public sealed record FileTarget(ConfigFile File) : NavTarget;

public sealed record VariantsTarget(ConfigFile BaseFile, IReadOnlyList<ConfigFile> Variants) : NavTarget;

public sealed record CategoryTarget(ConfigFile File, string CategoryPath, string CategoryName) : NavTarget;

public sealed class NavigatorTree : TreeView
{
    private const int LogicalItemHeight = 20;

    public NavigatorTree()
    {
        HideSelection = false;
        ShowLines = true;
        ShowRootLines = true;
        FullRowSelect = false;
        ImageList = StockImages.CreateTreeImages(LogicalToDeviceUnits(StockImages.SmallSize));
        BorderStyle = BorderStyle.None;
        ItemHeight = LogicalToDeviceUnits(LogicalItemHeight);
    }

    public NavTarget? SelectedTarget => SelectedNode?.Tag as NavTarget;

    public void Populate(Workspace workspace)
    {
        var selectedKey = SelectedNode is null ? null : KeyOf(SelectedNode);
        var expanded = AllNodes(Nodes).Where(n => n.IsExpanded).Select(KeyOf).ToHashSet();

        BeginUpdate();
        try
        {
            Nodes.Clear();
            var root = Node(Path.GetFileName(workspace.Folder.TrimEnd('\\')), StockImages.Root, new RootTarget());
            Nodes.Add(root);

            var byBase = workspace.Files
                .GroupBy(f => f.File.BaseName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var group in KnownConfigs.Groups)
            {
                var members = byBase.Values
                    .Where(list => string.Equals(KnownConfigs.Describe(list[0].File.BaseName).Group, group, StringComparison.Ordinal))
                    .OrderBy(list => KnownConfigs.Order(list[0].File.BaseName))
                    .ThenBy(list => list[0].File.BaseName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (members.Count == 0)
                {
                    continue;
                }

                var groupNode = Node(group, StockImages.Folder, new GroupTarget(group));
                groupNode.SelectedImageKey = StockImages.FolderOpen;
                root.Nodes.Add(groupNode);

                foreach (var files in members)
                {
                    var baseFile = files.FirstOrDefault(f => !f.File.IsVariant && f.File.IsInRootFolder) ?? files[0];
                    var variants = files.Where(f => f != baseFile).ToList();
                    groupNode.Nodes.Add(FileNode(baseFile, variants, workspace));
                }
            }

            root.Expand();
            foreach (var node in AllNodes(Nodes).Where(n => expanded.Contains(KeyOf(n))))
            {
                node.Expand();
            }

            SelectedNode = AllNodes(Nodes).FirstOrDefault(n => KeyOf(n) == selectedKey) ?? root;
        }
        finally
        {
            EndUpdate();
        }
    }

    public void RefreshDirtyMarkers(Workspace workspace)
    {
        BeginUpdate();
        try
        {
            foreach (var node in AllNodes(Nodes))
            {
                if (node.Tag is FileTarget { File: var file })
                {
                    var text = FileLabel(file) + (workspace.IsDirty(file.FullPath) ? " *" : string.Empty);
                    if (node.Text != text)
                    {
                        node.Text = text;
                    }
                }
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    public bool Select(Func<NavTarget, bool> predicate)
    {
        var node = AllNodes(Nodes).FirstOrDefault(n => n.Tag is NavTarget target && predicate(target));
        if (node is null)
        {
            return false;
        }

        node.EnsureVisible();
        SelectedNode = node;
        return true;
    }

    private static TreeNode FileNode(ConfigFile file, IReadOnlyList<ConfigFile> variants, Workspace workspace)
    {
        var image = file.File.BaseName.Equals("Settings", StringComparison.OrdinalIgnoreCase) ? StockImages.Settings : StockImages.Document;
        var node = Node(FileLabel(file) + (workspace.IsDirty(file.FullPath) ? " *" : string.Empty), image, new FileTarget(file));
        node.ToolTipText = file.Info.Description;
        AddCategories(node, file);

        if (variants.Count > 0)
        {
            var variantsNode = Node($"Alternate configs ({variants.Count})", StockImages.Folder, new VariantsTarget(file, variants));
            variantsNode.SelectedImageKey = StockImages.FolderOpen;
            foreach (var variant in variants)
            {
                var variantNode = Node(FileLabel(variant), StockImages.Variant, new FileTarget(variant));
                variantNode.ToolTipText = variant.File.RelativePath;
                AddCategories(variantNode, variant);
                variantsNode.Nodes.Add(variantNode);
            }

            node.Nodes.Add(variantsNode);
        }

        return node;
    }

    private static void AddCategories(TreeNode node, ConfigFile file)
    {
        var categories = file.Stats?.Categories ?? [];
        if (categories.Count <= 1)
        {
            return;
        }

        foreach (var category in categories)
        {
            node.Nodes.Add(Node($"{DisplayNames.Humanize(category.Name)} ({category.Count})", StockImages.Category,
                new CategoryTarget(file, category.Path, category.Name)));
        }
    }

    public static string FileLabel(ConfigFile file) => file.File.Kind switch
    {
        LsrFileKind.AdditiveVariant => $"+ {file.File.VariantSuffix}" + FolderSuffix(file),
        LsrFileKind.ReplacementVariant => $"Replaced by {file.File.VariantSuffix}" + FolderSuffix(file),
        _ when string.Equals(file.Info.Title, file.File.BaseName, StringComparison.OrdinalIgnoreCase) => file.Info.Title,
        _ => $"{file.Info.Title} ({file.File.BaseName})"
    };

    private static string FolderSuffix(ConfigFile file) =>
        file.File.IsInRootFolder ? string.Empty : $"  [{Path.GetDirectoryName(file.File.RelativePath)}]";

    private static TreeNode Node(string text, string image, NavTarget target) =>
        new(text) { ImageKey = image, SelectedImageKey = image, Tag = target };

    private static string KeyOf(TreeNode node) => node.Tag switch
    {
        RootTarget => "root",
        GroupTarget g => "group:" + g.Group,
        FileTarget f => "file:" + f.File.FullPath,
        VariantsTarget v => "variants:" + v.BaseFile.FullPath,
        CategoryTarget c => "category:" + c.File.FullPath + "|" + c.CategoryPath,
        _ => node.FullPath
    };

    private static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in AllNodes(node.Nodes))
            {
                yield return child;
            }
        }
    }
}
