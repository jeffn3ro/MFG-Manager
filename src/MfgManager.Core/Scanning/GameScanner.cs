using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Scanning.Launchers;
using MfgManager.Core.Settings;

namespace MfgManager.Core.Scanning;

/// <summary>Asks every launcher for its games, removes duplicates, then inspects each game.</summary>
/// <param name="launchers">Where games come from. Tests pass their own; the app uses every launcher on the system.</param>
/// <remarks>
/// The launchers are asked at the same time, and the games are looked through at the same time, because both are
/// mostly waiting on the disk. Each game is only changed by its own check, so they can't get in each other's way.
/// </remarks>
public sealed class GameScanner(AppSettings settings, IReadOnlyList<Func<IEnumerable<Game>>>? launchers = null)
{
    public IReadOnlyList<Game> Scan(IProgress<string>? progress = null)
    {
        var games = FindAllGames()
            // Same folder found twice -> keep one.
            .GroupBy(g => g.Folder, PathUtil.Comparer)
            .Select(group => group.First())
            .ToList();

        int started = 0;
        SideBySide.For(games.Count, i =>
        {
            Game game = games[i];
            progress?.Report($"Checking {Interlocked.Increment(ref started)} of {games.Count}: {game.Name}");
            settings.ExeOverrides.TryGetValue(game.Folder, out string? exeOverride);
            GameInspector.Inspect(game, exeOverride);
        });
        return games;
    }

    private List<Game> FindAllGames()
    {
        var sources = new List<Func<IEnumerable<Game>>>(launchers ?? AllLaunchers())
        {
            () => settings.ManualGames.Where(File.Exists).Select(ManualGame),
        };

        // Each answer keeps its launcher's place in the list, so when two launchers report the same folder,
        // the one higher in the list wins, whichever answered first.
        var found = new List<Game>[sources.Count];
        SideBySide.For(sources.Count, i => found[i] = Ask(sources[i]));
        return [.. found.SelectMany(games => games)];
    }

    /// <summary>One broken launcher can't stop the whole scan. Launchers that only exist on one system simply find nothing on the other.</summary>
    private static List<Game> Ask(Func<IEnumerable<Game>> source)
    {
        try
        {
            return [.. source()];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
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
