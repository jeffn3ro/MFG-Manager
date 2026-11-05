using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

public static class UbisoftLibrary
{
    public static IEnumerable<string> FindInstallFolders() =>
        WindowsRegistry.ReadSubKeyValues(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs", "InstallDir");

    public static IEnumerable<Game> FindGames(IEnumerable<string> installFolders)
    {
        foreach (var raw in installFolders)
        {
            var folder = PathUtil.Normalize(raw);
            if (!Directory.Exists(folder)) continue;
            yield return new Game { Name = Path.GetFileName(folder), Launcher = Launcher.Ubisoft, Folder = folder };
        }
    }
}
