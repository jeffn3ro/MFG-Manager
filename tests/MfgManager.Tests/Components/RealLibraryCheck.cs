using Avalonia.Controls;
using Avalonia.VisualTree;
using MfgManager.App.Components.Shell;
using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Platform;
using MfgManager.Core.Scanning;
using MfgManager.Core.Settings;
using MfgManager.Core.Store;
using MfgManager.Tests.ViewModels;

namespace MfgManager.Tests.Components;

/// <summary>
/// Scans this computer's real games and draws them in a window nobody sees, to catch what made-up games can't:
/// real names, real covers and real folders. Only runs when you ask for it: set MFG_REAL_SCAN=1.
/// It changes nothing real. Your settings and the cache are copied to a temporary folder first, and no button is pressed.
/// </summary>
public class RealLibraryCheck
{
    public static TheoryData<string> Modes => Stage.Modes;

    private static void CopyIfThere(string source, string target)
    {
        if (File.Exists(source)) File.Copy(source, target);
        if (!Directory.Exists(source)) return;
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    [Theory, MemberData(nameof(Modes))]
    public Task Real_library_draws_and_every_game_stays_readable(string mode) => TestApp.Run(async () =>
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("MFG_REAL_SCAN") == "1", "Set MFG_REAL_SCAN=1 to scan this computer's real games");

        using var temp = new TempFolder();
        string settingsFile = Path.Combine(temp.Path, "settings.json");
        CopyIfThere(Path.Combine(AppPaths.DataFolder, "settings.json"), settingsFile);
        CopyIfThere(Path.Combine(AppPaths.DataFolder, "cache"), temp.Folder("cache"));
        string mods = temp.Folder("mods");
        CopyIfThere("""C:\Program Files\MFG Manager\Mods""", mods);

        var store = new SettingsStore(settingsFile);
        AppSettings settings = store.Load();
        void Save() => store.Save(settings);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MFG-Manager/2.0");
        var dialogs = new FakeDialogs { Answer = false };

        var vm = new MainViewModel(settings, Save, new GameScanner(settings), new ModInstaller(settings, Save, temp.Folder("removed")),
            new SteamStore(http, Path.Combine(temp.Path, "cache")), new UpdateChecker(http), dialogs, mods, temp.Folder("downloads"));

        Window window = Stage.Show(new MainWindow(), mode, 1280, 820, vm);
        await vm.InitializeAsync();
        Stage.Settle();

        Assert.NotEmpty(vm.Library.Games);
        ReadabilityCheck.Assert_all_text_is_readable(window, $"real library in {mode} mode");
        Stage.Snapshot(window, "Real-framegen", mode);

        vm.Library.OnlyFrameGen = false;
        Stage.Settle();
        ReadabilityCheck.Assert_all_text_is_readable(window, $"all real games in {mode} mode");
        Stage.Snapshot(window, "Real-all", mode);

        var report = new List<string> { $"status: {vm.Status.Text}", $"mod: {vm.Mod.VersionText} at {vm.Mod.PathText}", $"update: {vm.Mod.UpdateText}", $"games: {vm.Library.Games.Count}", "" };
        foreach (GameViewModel game in vm.Library.Games.OrderBy(g => g.Launcher).ThenBy(g => g.Name))
        {
            vm.Library.SelectedGame = game;
            Stage.Settle();
            ReadabilityCheck.Assert_all_text_is_readable(window, $"{game.Name} in {mode} mode");
            int texts = window.GetVisualDescendants().OfType<TextBlock>().Count(t => t.IsEffectivelyVisible);
            report.Add($"{game.Launcher,-14} {game.Name,-45} badge={game.Badge ?? "-",-10} framegen={game.Game.HasFrameGen,-5} cover={(game.CoverPath is null ? "no" : "yes"),-3} drm=[{string.Join(", ", game.Game.Drm)}] anticheat={game.Game.AntiCheat ?? "-"} exe={game.Game.Exe} status=[{game.StatusText}]");
            Assert.True(texts > 0);
        }
        report.Add("");
        report.AddRange(dialogs.Messages.Select(m => "dialog: " + m));

        if (Environment.GetEnvironmentVariable("MFG_SHOTS") is { } folder)
            File.WriteAllLines(Path.Combine(folder, $"real-report-{mode}.txt"), report);

        window.Close();
    });
}
