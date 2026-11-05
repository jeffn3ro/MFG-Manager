using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Scanning;
using MfgManager.Core.Settings;
using MfgManager.Core.Store;
using MfgManager.Tests.Core;

namespace MfgManager.Tests.ViewModels;

/// <summary>The whole app's logic, with fake dialogs, a fake web and fake game folders. Nothing real is touched.</summary>
public sealed class AppSetup : IDisposable
{
    public TempFolder Temp { get; } = new();
    public AppSettings Settings { get; } = new();
    public FakeDialogs Dialogs { get; } = new();
    public FakeWeb Web { get; } = new();
    public List<Game> ScannerGames { get; } = [];
    public HashSet<string> RunningExes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string ModsFolder { get; }
    public string DownloadsFolder { get; }
    public int Saves { get; private set; }
    /// <summary>How many full game scans have run. Each one asks the launcher exactly once.</summary>
    public int Scans { get; private set; }
    public MainViewModel Vm { get; }

    public AppSetup()
    {
        ModsFolder = Temp.Folder("mods");
        DownloadsFolder = Temp.Folder("downloads");
        void Save() => Saves++;
        var installer = new ModInstaller(Settings, Save, Temp.Folder("removed"), FakeMod.ReadVersion);
        IEnumerable<Game> CountingLauncher()
        {
            Scans++;
            return ScannerGames;
        }
        Vm = new MainViewModel(Settings, Save, new GameScanner(Settings, [CountingLauncher]), installer,
            new SteamStore(Web.Client(), Temp.Folder("cache")), new UpdateChecker(Web.Client(), FakeMod.ReadVersion),
            Dialogs, ModsFolder, DownloadsFolder, exe => RunningExes.Contains(exe));
        Vm.Mod.Mod = FakeMod.Create(Temp, "1.7.0");
    }

    public LibraryViewModel Library => Vm.Library;
    public GameActionsViewModel Actions => Vm.Actions;

    public GameViewModel AddGame(string name, Launcher launcher, bool frameGen = true, string? antiCheat = null, params string[] drm)
    {
        Game game = MakeGame(name, launcher, frameGen, antiCheat, drm);
        var vm = new GameViewModel(game, select => Library.SelectedGame = select);
        Library.Games.Add(vm);
        Library.RefreshStatuses();
        Library.RefreshFilterOptions();
        return vm;
    }

    public Game MakeGame(string name, Launcher launcher, bool frameGen = true, string? antiCheat = null, params string[] drm)
    {
        string exe = Temp.File($"games/{name}/{name}.exe", "game");
        var game = new Game { Name = name, Launcher = launcher, Folder = Path.GetDirectoryName(exe)!, Exe = exe, HasFrameGen = frameGen, AntiCheat = antiCheat };
        game.Drm.AddRange(drm);
        return game;
    }

    /// <summary>Writes a file the real reader recognises as the mod, unlike <see cref="FakeMod"/>.</summary>
    public string WriteRealLookingMod(string folder, string version)
    {
        string path = Temp.File(Path.Combine(folder, ModPackage.FileName));
        File.WriteAllBytes(path, FakePe.Build(ModPackage.ProductName, version));
        return path;
    }

    public List<string> Shown() => [.. Library.View.Items.Select(g => g.Name)];

    public void Dispose() => Temp.Dispose();
}
