using MfgManager.Core.Models;
using Microsoft.Win32;

namespace MfgManager.Core.Scanning.Launchers;

/// <summary>Steam on Windows and Linux. It keeps the same files on both, so only finding its folder differs.</summary>
public static class SteamLibrary
{
    /// <summary>Installed like games but not games: redistributables, the SDK, SteamVR, and Linux's Proton and runtime tools.</summary>
    private static readonly HashSet<int> NotGames = [228980, 1007, 250820];
    private static readonly string[] ToolNamePrefixes = ["Proton ", "Steam Linux Runtime", "Steamworks "];
    private static readonly string[] LinuxFolders = [".local/share/Steam", ".steam/steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam"];

    public static string? FindSteamFolder() => OperatingSystem.IsWindows() ? FindOnWindows() : FindOnLinux(Home);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? FindOnWindows() =>
        OperatingSystem.IsWindows() ? Registry.GetValue("""HKEY_CURRENT_USER\Software\Valve\Steam""", "SteamPath", null) as string : null;

    /// <summary>The normal locations on Linux, then the Flatpak one.</summary>
    public static string? FindOnLinux(string home) =>
        LinuxFolders
            .Select(folder => Path.Combine(home, folder))
            .FirstOrDefault(folder => Directory.Exists(Path.Combine(folder, "steamapps")));

    /// <summary>Steam can have several library folders (e.g. C: and E:). They're listed in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> GetLibraryFolders(string steamFolder)
    {
        var folders = new List<string> { steamFolder };
        string vdfPath = Path.Combine(steamFolder, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath)) folders.AddRange(Vdf.GetValues(File.ReadAllText(vdfPath), "path"));

        return [.. folders
            .Select(PathUtil.Normalize)
            .Distinct(PathUtil.Comparer)];
    }

    /// <summary>Every installed game has an appmanifest_[id].acf file with its name and folder.</summary>
    public static IEnumerable<Game> FindGames(string steamFolder)
    {
        foreach (string library in GetLibraryFolders(steamFolder))
        {
            string steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            foreach (string manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                string text = File.ReadAllText(manifest);
                string? installDir = Vdf.GetValue(text, "installdir");
                if (string.IsNullOrEmpty(installDir)) continue;

                string folder = Path.Combine(steamapps, "common", installDir);
                if (!Directory.Exists(folder)) continue;

                string name = Vdf.GetValue(text, "name") ?? installDir;
                int? appId = int.TryParse(Vdf.GetValue(text, "appid"), out int id) ? id : null;
                if (IsTool(appId, name)) continue;

                yield return new Game
                {
                    Name = name,
                    Launcher = Launcher.Steam,
                    Folder = PathUtil.Normalize(folder),
                    SteamAppId = appId,
                };
            }
        }
    }

    private static bool IsTool(int? appId, string name) =>
        (appId is { } id && NotGames.Contains(id)) || ToolNamePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
}
