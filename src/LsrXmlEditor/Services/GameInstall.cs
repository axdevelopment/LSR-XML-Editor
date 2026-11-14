using Microsoft.Win32;

namespace LsrXmlEditor.Services;

public static class GameInstall
{
    private const string GameExecutable = "GTA5.exe";
    private const string GameArchive = "x64v.rpf";

    private static readonly string[] RegistryKeys =
    [
        @"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V",
        @"SOFTWARE\Rockstar Games\Grand Theft Auto V"
    ];

    private static readonly string[] RegistryValues = ["InstallFolder", "InstallFolderSteam", "InstallFolderEpic"];

    public static IEnumerable<string> Folders() =>
        RegistryFolders().Where(IsGameFolder).Distinct(StringComparer.OrdinalIgnoreCase);

    public static bool IsGameFolder(string folder) =>
        File.Exists(Path.Combine(folder, GameExecutable)) && File.Exists(Path.Combine(folder, GameArchive));

    private static IEnumerable<string> RegistryFolders()
    {
        foreach (var keyName in RegistryKeys)
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyName);
            if (key is null)
            {
                continue;
            }

            foreach (var value in RegistryValues)
            {
                if (key.GetValue(value) is string folder && folder.Length > 0)
                {
                    yield return folder.TrimEnd('\\');
                }
            }
        }
    }
}
