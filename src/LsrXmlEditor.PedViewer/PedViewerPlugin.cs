using LsrXmlEditor.PedViewer.GameData;
using LsrXmlEditor.PedViewer.UI;

namespace LsrXmlEditor.PedViewer;

public static class PedViewerPlugin
{
    public static Control CreatePanel() => new PreviewPanel();

    public static bool CanPreview(string xml) => PreviewRequests.CanPreview(xml);

    public static Task<string?> ShowEntryAsync(Control panel, string gtaFolder, string xml, string title) =>
        ((PreviewPanel)panel).ShowAsync(gtaFolder, xml, title);

    public static void Release() => GameArchive.Release();

    public static void SetEntryWriter(Control panel, Action<string>? writer) => ((PreviewPanel)panel).SetEntryWriter(writer);
}
