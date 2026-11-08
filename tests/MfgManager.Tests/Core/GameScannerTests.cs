using MfgManager.Core.Models;
using MfgManager.Core.Scanning;
using MfgManager.Core.Settings;

namespace MfgManager.Tests.Core;

public class GameScannerTests
{
    /// <summary>Collects progress messages straight away, from whichever thread sends them.</summary>
    private sealed class Messages : IProgress<string>
    {
        private readonly List<string> _all = [];

        public List<string> All
        {
            get
            {
                lock (_all) return [.. _all];
            }
        }

        public void Report(string value)
        {
            lock (_all) _all.Add(value);
        }
    }

    private static Game GameIn(TempFolder temp, string name, bool frameGen)
    {
        string exe = temp.File($"games/{name}/{name}.exe", "game");
        if (frameGen) temp.File($"games/{name}/nvngx_dlssg.dll");
        return new Game { Name = name, Launcher = Launcher.Steam, Folder = Path.GetDirectoryName(exe)! };
    }

    [Fact]
    public void Every_game_is_checked_and_the_list_keeps_the_launchers_order()
    {
        using var temp = new TempFolder();
        Game[] first = [.. Enumerable.Range(1, 12).Select(i => GameIn(temp, $"First {i}", frameGen: i % 2 == 0))];
        Game[] second = [.. Enumerable.Range(1, 12).Select(i => GameIn(temp, $"Second {i}", frameGen: i % 3 == 0))];
        var scanner = new GameScanner(new AppSettings(), [() => first, () => second]);

        IReadOnlyList<Game> games = scanner.Scan();

        Assert.Equal(first.Concat(second).Select(g => g.Name), games.Select(g => g.Name));
        Assert.All(games, game => Assert.Equal(Path.Combine(game.Folder, game.Name + ".exe"), game.Exe));
        Assert.Equal(first.Concat(second).Select(g => File.Exists(Path.Combine(g.Folder, "nvngx_dlssg.dll"))), games.Select(g => g.HasFrameGen));
    }

    [Fact]
    public void When_two_launchers_find_the_same_folder_the_first_in_the_list_wins_even_if_it_answers_last()
    {
        using var temp = new TempFolder();
        Game fromSlow = GameIn(temp, "Shared", frameGen: true);
        var fromFast = new Game { Name = "Shared (second launcher)", Launcher = Launcher.Epic, Folder = fromSlow.Folder };
        IEnumerable<Game> Slow()
        {
            Thread.Sleep(300);
            return [fromSlow];
        }
        var scanner = new GameScanner(new AppSettings(), [Slow, () => [fromFast]]);

        Game game = Assert.Single(scanner.Scan());

        Assert.Same(fromSlow, game);
    }

    [Fact]
    public void A_broken_launcher_doesnt_stop_the_others()
    {
        using var temp = new TempFolder();
        Game game = GameIn(temp, "Fine", frameGen: false);
        var scanner = new GameScanner(new AppSettings(), [() => throw new UnauthorizedAccessException(), () => [game]]);

        Assert.Same(game, Assert.Single(scanner.Scan()));
    }

    [Fact]
    public void Progress_counts_every_game_once()
    {
        using var temp = new TempFolder();
        Game[] all = [.. Enumerable.Range(1, 9).Select(i => GameIn(temp, $"Game {i}", frameGen: false))];
        var messages = new Messages();

        new GameScanner(new AppSettings(), [() => all]).Scan(messages);

        Assert.Equal(Enumerable.Range(1, 9).Select(i => $"Checking {i} of 9"), messages.All.Select(m => m.Split(':')[0]).Order(StringComparer.Ordinal));
        Assert.Equal(all.Select(g => g.Name).Order(), messages.All.Select(m => m.Split(": ")[1]).Order());
    }
}
