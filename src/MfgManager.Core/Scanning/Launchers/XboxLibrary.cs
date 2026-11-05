using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

public static class XboxLibrary
{
    /// <summary>The Xbox app installs into an "XboxGames" folder at the root of a drive.</summary>
    public static IEnumerable<Game> FindGames(IEnumerable<string> driveRoots)
    {
        foreach (string root in driveRoots)
        {
            string xboxGames = Path.Combine(root, "XboxGames");
            if (!Directory.Exists(xboxGames)) continue;

            foreach (string folder in Directory.EnumerateDirectories(xboxGames))
            {
                if (Path.GetFileName(folder).Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new Game { Name = Path.GetFileName(folder), Launcher = Launcher.Xbox, Folder = PathUtil.Normalize(folder) };
            }
        }
    }

    /// <summary>Only on Windows: the Xbox app doesn't exist on Linux.</summary>
    public static IEnumerable<string> FixedDriveRoots() =>
        OperatingSystem.IsWindows()
            ? DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName)
            : [];
}
