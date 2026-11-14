using LsrXmlEditor.PedViewer.GameData;
using LsrXmlEditor.PedViewer.Rendering;

namespace LsrXmlEditor.PedViewer.UI;

public sealed class PreviewPanel : UserControl
{
    private readonly PedViewerPanel _ped = new() { Dock = DockStyle.Fill };
    private readonly ModelViewerPanel _model = new() { Dock = DockStyle.Fill, Visible = false };
    private int _generation;

    public PreviewPanel()
    {
        Controls.Add(_ped);
        Controls.Add(_model);
    }

    public void SetEntryWriter(Action<string>? writer) => _ped.SetEntryWriter(writer);

    public async Task<string?> ShowAsync(string gtaFolder, string xml, string title)
    {
        if (!GameArchive.LooksLikeGtaFolder(gtaFolder))
        {
            return $"\"{gtaFolder}\" is not a GTA V folder.";
        }

        var generation = ++_generation;
        switch (PreviewRequests.Parse(xml))
        {
            case PedPreview ped:
                Show(_ped);
                return await _ped.ShowOutfitAsync(gtaFolder, ped.Xml, title);
            case AnimationPreview animation:
                Show(_ped);
                return await _ped.ShowAnimationAsync(gtaFolder, animation.Dictionary, animation.Clip, title);
            case ModelPreview model:
                UseWaitCursor = true;
                GameArchive archive;
                try
                {
                    archive = await GameArchive.OpenAsync(gtaFolder, null);
                }
                finally
                {
                    UseWaitCursor = false;
                }

                if (generation != _generation)
                {
                    return null;
                }

                if (archive.FindFile(model.Model.ToLowerInvariant() + ".ymt") is not null)
                {
                    Show(_ped);
                    return await _ped.ShowOutfitAsync(gtaFolder, model.Xml, title);
                }

                Show(_model);
                var colors = model.PrimaryColor is null && model.SecondaryColor is null
                    ? null
                    : new VehicleColors(model.PrimaryColor ?? VehiclePalette.DefaultPrimary, model.SecondaryColor ?? model.PrimaryColor ?? VehiclePalette.DefaultSecondary);
                return await _model.ShowModelAsync(gtaFolder, model.Model, colors, title);
            default:
                return "The selected entry has no model or animation that can be shown in 3D.";
        }
    }

    private void Show(Control panel)
    {
        _ped.Visible = panel == _ped;
        _model.Visible = panel == _model;
    }
}
