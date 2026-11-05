using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Scanning.Launchers;
using MfgManager.Core.Settings;

namespace MfgManager.Core.Scanning;

/// <summary>Asks every launcher for its games, removes duplicates, then inspects each game.</summary>
/// <param name="launchers">Where games come from. Tests pass their own; the app uses every launcher on the system.</param>
public sealed class GameScanner(AppSettings settings, IReadOnlyList<Func<IEnumerable<Game>>>? launchers = null)
{
    public IReadOnlyList<Game> Scan(IProgress<string>? progress = null)
    {
        var games = FindAllGames()
            // Same folder found twice -> keep one.
            .GroupBy(g => g.Folder, PathUtil.Comparer)
            .Select(group => group.First())
            .ToList();

        for (int i = 0; i < games.Count; i++)
        {
            Game game = games[i];
            progress?.Report($"Checking {i + 1} of {games.Count}: {game.Name}");
            settings.ExeOverrides.TryGetValue(game.Folder, out string? exeOverride);
            GameInspector.Inspect(game, exeOverride);
        }
        return games;
    }

    private IEnumerable<Game> FindAllGames()
    {
        // Each launcher is wrapped so one broken launcher can't stop the whole scan.
        // Launchers that only exist on one system simply find nothing on the other.
        var sources = new List<Func<IEnumerable<Game>>>(launchers ?? AllLaunchers())
        {
            () => settings.ManualGames.Where(File.Exists).Select(ManualGame),
        };

        foreach (Func<IEnumerable<Game>> source in sources)
        {
            List<Game> found;
            try { found = [.. source()]; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            foreach (Game game in found) yield return game;
        }
    }

    private static Func<IEnumerable<Game>>[] AllLaunchers() =>
    [
        () => SteamLibrary.FindSteamFolder() is { } steam ? SteamLibrary.FindGames(steam) : [],
        () => UbisoftLibrary.FindGames(UbisoftLibrary.FindInstallFolders()),
        () => EpicLibrary.FindGames(EpicLibrary.DefaultManifestsFolder),
        () => GogLibrary.FindGames(GogLibrary.ReadEntries()),
        () => XboxLibrary.FindGames(XboxLibrary.FixedDriveRoots()),
        // Battle.net, EA app, Rockstar.
        () => UninstallLibrary.FindGames(UninstallLibrary.ReadEntries()),
        // Epic and GOG on Linux.
        () => HeroicLibrary.FindGames(HeroicLibrary.DefaultConfigFolders()),
    ];

    /// <summary>A game the user added by picking its .exe.</summary>
    public static Game ManualGame(string exePath)
    {
        string folder = PathUtil.Normalize(Path.GetDirectoryName(exePath)!);
        string? productName = PeVersionInfo.Read(exePath)?.ProductName;
        return new Game
        {
            Name = string.IsNullOrWhiteSpace(productName) ? Path.GetFileNameWithoutExtension(exePath) : productName,
            Launcher = Launcher.Manual,
            Folder = folder,
            ExeHint = exePath,
        };
    }
}
