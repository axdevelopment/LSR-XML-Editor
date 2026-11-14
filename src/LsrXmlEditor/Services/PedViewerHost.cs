using System.Reflection;
using System.Runtime.ExceptionServices;
using LsrXmlEditor.Core.Storage;

namespace LsrXmlEditor.Services;

public sealed class PedViewerHost
{
    public const string CodeWalkerAssembly = "CodeWalker.Core";
    private const string PluginFolder = "PedViewer";
    private const string PluginAssembly = "LsrXmlEditor.PedViewer.dll";
    private const string PluginType = "LsrXmlEditor.PedViewer.PedViewerPlugin";
    private const string LoadFailed = "The 3D viewer plugin could not be loaded.";
    private const string DllExtension = ".dll";
    private const string ReleaseMarker = "Release";
    private const string ObjFolder = "obj";

    private readonly Lazy<Type?> _plugin;

    public PedViewerHost() => _plugin = new(LoadPluginType);

    public static string PluginDirectory => Path.Combine(AppContext.BaseDirectory, PluginFolder);

    public static string PluginPath => Path.Combine(PluginDirectory, PluginAssembly);

    public static bool IsInstalled => File.Exists(PluginPath);

    public string? CodeWalkerFolder { get; set; }

    public bool IsAvailable => _plugin.Value is not null;

    public bool HasCodeWalker => CodeWalkerPath() is not null;

    public Control CreatePanel() => Invoke<Control>("CreatePanel") ?? throw new InvalidOperationException(LoadFailed);

    public bool CanPreview(string xml)
    {
        try
        {
            return Invoke<object>("CanPreview", xml) is true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    public Task<string?> ShowEntryAsync(Control panel, string gtaFolder, string xml, string title) =>
        Invoke<Task<string?>>("ShowEntryAsync", panel, gtaFolder, xml, title) ?? Task.FromResult<string?>(LoadFailed);

    public void Release() => Invoke<object>("Release");

    public void SetEntryWriter(Control panel, Action<string> writer) => Invoke<object>("SetEntryWriter", panel, writer);

    public static string? FindCodeWalkerCore(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var direct = Path.Combine(folder, CodeWalkerAssembly + DllExtension);
        if (File.Exists(direct))
        {
            return direct;
        }

        try
        {
            return Directory.EnumerateFiles(folder, CodeWalkerAssembly + DllExtension, SearchOption.AllDirectories)
                .Where(p => !p.Split(Path.DirectorySeparatorChar).Contains(ObjFolder, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Contains(ReleaseMarker, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? FindGtaFolder(string configFolder, string? remembered = null) =>
        Candidates(configFolder, remembered).FirstOrDefault(GameInstall.IsGameFolder);

    private string? CodeWalkerPath()
    {
        var bundled = Path.Combine(PluginDirectory, CodeWalkerAssembly + DllExtension);
        return File.Exists(bundled) ? bundled : FindCodeWalkerCore(CodeWalkerFolder);
    }

    private T? Invoke<T>(string method, params object[] arguments) where T : class
    {
        try
        {
            return _plugin.Value?.GetMethod(method, BindingFlags.Public | BindingFlags.Static)?.Invoke(null, arguments) as T;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw;
        }
    }

    private static IEnumerable<string> Candidates(string configFolder, string? remembered)
    {
        if (!string.IsNullOrEmpty(remembered))
        {
            yield return remembered;
        }

        if (Directory.GetParent(configFolder)?.Parent is { } root)
        {
            yield return root.FullName;
        }

        foreach (var folder in GameInstall.Folders())
        {
            yield return folder;
        }

        if (Directory.GetParent(AppSettings.DefaultFolder)?.Parent is { } defaultRoot)
        {
            yield return defaultRoot.FullName;
        }
    }

    private Type? LoadPluginType()
    {
        if (!IsInstalled)
        {
            return null;
        }

        AppDomain.CurrentDomain.AssemblyResolve += ResolveDependency;
        try
        {
            return Assembly.LoadFrom(PluginPath).GetType(PluginType);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or TypeLoadException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private Assembly? ResolveDependency(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (name is null)
        {
            return null;
        }

        var codeWalker = CodeWalkerPath();
        string?[] folders = [PluginDirectory, codeWalker is null ? null : Path.GetDirectoryName(codeWalker)];
        var path = folders.OfType<string>().Select(f => Path.Combine(f, name + DllExtension)).FirstOrDefault(File.Exists);
        return path is null ? null : Assembly.LoadFrom(path);
    }
}
