using System.Text.RegularExpressions;
using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning;

/// <summary>
/// Looks through a game's files to work out: does it have DLSS Frame Generation,
/// does it have anti-cheat, which DRM does it use, and which .exe is the real game.
/// </summary>
public static class GameInspector
{
    // Either of these files means the game ships NVIDIA DLSS Frame Generation. sl.interposer.dll doesn't count: it's
    // Streamline's general loader, also used for plain DLSS and Reflex. Dead Space has it and no Frame Generation.
    private static readonly HashSet<string> FrameGenFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvngx_dlssg.dll", "sl.dlss_g.dll",
    };

    // File name -> DRM / online service it belongs to.
    private static readonly Dictionary<string, string> DrmFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["steam_api64.dll"] = "Steamworks",
        ["steam_api.dll"] = "Steamworks",
        ["uplay_r1_loader64.dll"] = "Ubisoft Connect",
        ["uplay_r2_loader64.dll"] = "Ubisoft Connect",
        ["upc_r2_loader64.dll"] = "Ubisoft Connect",
        ["EOSSDK-Win64-Shipping.dll"] = "Epic Online Services",
        ["Galaxy64.dll"] = "GOG Galaxy",
    };

    // Games from these launchers always need the launcher running (same names the Steam store notices use).
    private static readonly Dictionary<Launcher, string> LauncherDrm = new()
    {
        [Launcher.Ubisoft] = "Ubisoft Connect",
        [Launcher.BattleNet] = "Battle.net",
        [Launcher.Ea] = "EA app",
        [Launcher.Rockstar] = "Rockstar Launcher",
    };

    // File or folder name -> anti-cheat it belongs to.
    private static readonly (Regex Pattern, string Name)[] AntiCheats =
    [
        (new Regex("^EasyAntiCheat", RegexOptions.IgnoreCase), "Easy Anti-Cheat"),
        (new Regex("^(BattlEye|BEService)", RegexOptions.IgnoreCase), "BattlEye"),
        (new Regex("^(GameGuard|nProtect)", RegexOptions.IgnoreCase), "nProtect GameGuard"),
        (new Regex("^EAAntiCheat", RegexOptions.IgnoreCase), "EA Javelin"),
        (new Regex("^xigncode", RegexOptions.IgnoreCase), "XIGNCODE3"),
    ];

    // .exe files that are never the game itself.
    private static readonly Regex NotTheGame = new(
        "unins|crash|setup|redist|dxsetup|directx|launcher|anti-?cheat|beservice|battleye|helper|report|prereq|dotnet|cefprocess|update|install",
        RegexOptions.IgnoreCase);

    // Folders inside a game that hold modding or developer tools, never the game itself.
    // Cyberpunk 2077's tools\redmod\bin\scc.exe is twice the size of the real game .exe.
    private static readonly HashSet<string> ToolFolders = new(StringComparer.OrdinalIgnoreCase) { "tools", "_CommonRedist" };

    /// <param name="exeOverride">An .exe the user picked by hand. Wins over our guess.</param>
    public static void Inspect(Game game, string? exeOverride = null)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 8, IgnoreInaccessible = true };
        var entries = new DirectoryInfo(game.Folder).EnumerateFileSystemInfos("*", options).ToList();
        var files = entries.OfType<FileInfo>().ToList();

        game.HasFrameGen = files.Any(f => FrameGenFiles.Contains(f.Name));
        game.AntiCheat = entries
            .Select(e => AntiCheats.FirstOrDefault(a => a.Pattern.IsMatch(e.Name)).Name)
            .FirstOrDefault(name => name is not null);
        // Blizzard/Activision games on Battle.net are online games with anti-cheat built into the game itself,
        // so there's no separate file to find. Flag them anyway, so you're always warned.
        if (game.AntiCheat is null && game.Launcher == Launcher.BattleNet) game.AntiCheat = "Blizzard anti-cheat (built in)";

        game.Drm.Clear();
        game.Drm.AddRange(files.Select(f => DrmFiles.GetValueOrDefault(f.Name)).OfType<string>().Distinct());
        if (LauncherDrm.GetValueOrDefault(game.Launcher) is { } launcherDrm && !game.Drm.Contains(launcherDrm)) game.Drm.Add(launcherDrm);

        // settings.json can be changed by any program, and the mod goes next to this .exe, so it has to belong to this game.
        var useOverride = exeOverride is not null && File.Exists(exeOverride) && PathUtil.IsInside(exeOverride, game.Folder);
        game.Exe = useOverride ? exeOverride : PickGameExe(files, game.ExeHint, game.Folder);
    }

    /// <summary>
    /// Guess the real game .exe. Unreal Engine games have a small launcher .exe at the top and
    /// the real one deep inside as "[Name]-Win64-Shipping.exe", so that one wins. Otherwise
    /// the launcher's hint, otherwise the biggest .exe. A hint that points at a launcher
    /// (like GOG's REDprelauncher.exe for Cyberpunk 2077) is ignored, because the mod has to sit
    /// next to the game itself.
    /// </summary>
    public static string? PickGameExe(IEnumerable<FileInfo> files, string? exeHint, string gameFolder)
    {
        bool CouldBeTheGame(string path) =>
            !NotTheGame.IsMatch(Path.GetFileNameWithoutExtension(path)) && !IsInToolFolder(path, gameFolder);

        var exes = files
            .Where(f => f.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(f => CouldBeTheGame(f.FullName))
            .ToList();

        var shipping = exes
            .Where(f => f.Name.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            .MaxBy(f => f.Length);
        if (shipping is not null) return shipping.FullName;

        if (exeHint is not null && File.Exists(exeHint) && CouldBeTheGame(exeHint)) return exeHint;

        return exes.MaxBy(f => f.Length)?.FullName;
    }

    /// <summary>True when a file sits in a tools folder somewhere below the game folder.</summary>
    private static bool IsInToolFolder(string path, string gameFolder) =>
        Path.GetRelativePath(gameFolder, Path.GetDirectoryName(path)!)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(ToolFolders.Contains);
}
