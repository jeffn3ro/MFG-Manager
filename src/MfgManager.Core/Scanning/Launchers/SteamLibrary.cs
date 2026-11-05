using Microsoft.Win32;
using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

/// <summary>Steam on Windows and Linux. It keeps the same files on both, so only finding its folder differs.</summary>
public static class SteamLibrary
{
    /// <summary>Installed like games but not games: redistributables, the SDK, SteamVR, and Linux's Proton and runtime tools.</summary>
    private static readonly HashSet<int> NotGames = [228980, 1007, 250820];
    private static readonly string[] ToolNamePrefixes = ["Proton ", "Steam Linux Runtime", "Steamworks "];

    public static string? FindSteamFolder() => OperatingSystem.IsWindows() ? FindOnWindows() : FindOnLinux(Home);

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? FindOnWindows() =>
        OperatingSystem.IsWindows() ? Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string : null;

    /// <summary>The normal locations on Linux, then the Flatpak one.</summary>
    public static string? FindOnLinux(string home) =>
        new[] { ".local/share/Steam", ".steam/steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam" }
            .Select(folder => Path.Combine(home, folder))
            .FirstOrDefault(folder => Directory.Exists(Path.Combine(folder, "steamapps")));

    /// <summary>Steam can have several library folders (e.g. C: and E:). They're listed in libraryfolders.vdf.</summary>
    public static IReadOnlyList<string> GetLibraryFolders(string steamFolder)
    {
        var folders = new List<string> { steamFolder };
        var vdfPath = Path.Combine(steamFolder, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdfPath)) folders.AddRange(Vdf.GetValues(File.ReadAllText(vdfPath), "path"));

        return folders
            .Select(PathUtil.Normalize)
            .Distinct(PathUtil.Comparer)
            .ToList();
    }

    /// <summary>Every installed game has an appmanifest_[id].acf file with its name and folder.</summary>
    public static IEnumerable<Game> FindGames(string steamFolder)
    {
        foreach (var library in GetLibraryFolders(steamFolder))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;

            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(manifest);
                var installDir = Vdf.GetValue(text, "installdir");
                if (string.IsNullOrEmpty(installDir)) continue;

                var folder = Path.Combine(steamapps, "common", installDir);
                if (!Directory.Exists(folder)) continue;

                var name = Vdf.GetValue(text, "name") ?? installDir;
                int? appId = int.TryParse(Vdf.GetValue(text, "appid"), out var id) ? id : null;
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
