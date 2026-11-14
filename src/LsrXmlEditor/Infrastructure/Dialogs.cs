using System.Diagnostics;

namespace LsrXmlEditor.Infrastructure;

public static class Dialogs
{
    public const string AppTitle = "LSR XML Editor";
    public const string ConfigFolderPrompt = @"Select the Los Santos RED folder with the XML files (usually ...\Grand Theft Auto V\Plugins\LosSantosRED)";
    public const string GtaFolderPrompt = "Select your Grand Theft Auto V folder (contains GTA5.exe)";
    public const string CodeWalkerPrompt = "Select your CodeWalker folder (contains CodeWalker.Core.dll)";

    public static void Error(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirm(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    public static DialogResult AskSave(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

    public static string? PickFolder(IWin32Window? owner, string description, string initial)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(initial) ? initial : string.Empty,
            ShowNewFolderButton = false
        };

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    public static void Reveal(string path)
    {
        var argument = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", argument) { UseShellExecute = true });
    }
}
