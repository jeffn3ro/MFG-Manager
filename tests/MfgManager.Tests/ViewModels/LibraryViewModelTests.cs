using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Scanning;
using MfgManager.Core.Store;

namespace MfgManager.Tests.ViewModels;

public class LibraryViewModelTests
{
    [Fact]
    public void Filters_by_frame_gen_launcher_drm_and_search()
    {
        using var s = new AppSetup();
        s.AddGame("Alpha", Launcher.Steam, drm: "Steamworks");
        s.AddGame("Beta", Launcher.Ubisoft, drm: "Ubisoft Connect");
        s.AddGame("Gamma", Launcher.Steam, frameGen: false);
        s.Library.GroupBy = LibraryViewModel.NoGrouping;

        Assert.Equal(["Alpha", "Beta"], s.Shown());

        s.Library.OnlyFrameGen = false;
        Assert.Equal(["Alpha", "Beta", "Gamma"], s.Shown());

        s.Library.LauncherFilter = "Steam";
        Assert.Equal(["Alpha", "Gamma"], s.Shown());

        s.Library.DrmFilter = GameViewModel.NoDrmFound;
        Assert.Equal(["Gamma"], s.Shown());

        s.Library.LauncherFilter = LibraryViewModel.AllLaunchers;
        s.Library.DrmFilter = LibraryViewModel.AnyDrm;
        s.Library.SearchText = "bet";
        Assert.Equal(["Beta"], s.Shown());
        Assert.Equal([LibraryViewModel.AllLaunchers, "Steam", "Ubisoft"], s.Library.LauncherOptions.Select(o => o.Name));
    }

    [Fact]
    public async Task A_game_with_the_mod_stays_visible_even_without_frame_gen()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Plain", Launcher.Steam, frameGen: false);
        Assert.Empty(s.Shown());

        s.Library.OnlyFrameGen = false;
        s.Library.SelectedGame = game;
        await s.Actions.InstallCommand.ExecuteAsync();
        s.Library.OnlyFrameGen = true;

        Assert.Equal(["Plain"], s.Shown());
    }

    [Fact]
    public void Groups_by_launcher_by_drm_or_not_at_all()
    {
        using var s = new AppSetup();
        s.AddGame("Alpha", Launcher.Steam, drm: ["Steamworks", "Denuvo"]);
        s.AddGame("Beta", Launcher.Ubisoft);

        Assert.Equal(["Steam", "Ubisoft"], s.Library.View.Groups.Select(g => g.Name));

        s.Library.GroupBy = LibraryViewModel.GroupByDrm;
        // A game appears under each of its DRM.
        Assert.Equal(["Denuvo", GameViewModel.NoDrmFound, "Steamworks"], s.Library.View.Groups.Select(g => g.Name));
        Assert.Equal("Alpha", s.Library.View.Groups.Single(g => g.Name == "Denuvo").Items.Single().Name);

        s.Library.GroupBy = LibraryViewModel.NoGrouping;
        Assert.False(s.Library.View.Groups.Single().HasName);
    }

    [Fact]
    public void Sidebar_counts_show_what_you_would_see_with_your_other_filters_on()
    {
        using var s = new AppSetup();
        s.AddGame("Alpha", Launcher.Steam);
        s.AddGame("Beta", Launcher.Steam);
        s.AddGame("Gamma", Launcher.Ubisoft, frameGen: false);
        int Count(string name) => s.Library.LauncherOptions.Single(o => o.Name == name).Count;

        Assert.Equal(2, Count(LibraryViewModel.AllLaunchers));
        Assert.Equal(2, Count("Steam"));
        Assert.Equal(0, Count("Ubisoft"));

        s.Library.OnlyFrameGen = false;
        Assert.Equal(3, Count(LibraryViewModel.AllLaunchers));
        Assert.Equal(1, Count("Ubisoft"));

        s.Library.SearchText = "alp";
        Assert.Equal(1, Count("Steam"));
    }

    [Fact]
    public void Clicking_a_card_selects_it_and_only_it()
    {
        using var s = new AppSetup();
        GameViewModel alpha = s.AddGame("Alpha", Launcher.Steam);
        GameViewModel beta = s.AddGame("Beta", Launcher.Steam);

        alpha.SelectCommand.Execute(null);
        Assert.True(alpha.IsSelected);

        beta.SelectCommand.Execute(null);

        Assert.Same(beta, s.Library.SelectedGame);
        Assert.False(alpha.IsSelected);
        Assert.True(beta.IsSelected);
    }

    [Fact]
    public void When_filters_hide_the_selected_game_the_first_visible_one_is_selected()
    {
        using var s = new AppSetup();
        GameViewModel alpha = s.AddGame("Alpha", Launcher.Steam);
        GameViewModel beta = s.AddGame("Beta", Launcher.Ubisoft);
        s.Library.SelectedGame = beta;

        s.Library.SearchText = "bet";
        // Still visible: stays selected.
        Assert.Same(beta, s.Library.SelectedGame);

        s.Library.SearchText = "alp";
        Assert.Same(alpha, s.Library.SelectedGame);

        s.Library.SearchText = "nothing matches this";
        Assert.Null(s.Library.SelectedGame);
        Assert.True(s.Library.ShowEmptyMessage);
    }

    [Fact]
    public void A_filter_whose_entry_disappears_goes_back_to_showing_everything()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.LauncherFilter = "Steam";

        s.Library.Games.Remove(game);
        s.Library.RefreshFilterOptions();

        Assert.Equal(LibraryViewModel.AllLaunchers, s.Library.LauncherFilter);
    }

    [Fact]
    public void Sidebar_selection_and_filter_stay_in_step()
    {
        using var s = new AppSetup();
        s.AddGame("Alpha", Launcher.Steam);
        s.AddGame("Beta", Launcher.Ubisoft);

        s.Library.SelectedLauncherOption = s.Library.LauncherOptions.Single(o => o.Name == "Ubisoft");
        Assert.Equal("Ubisoft", s.Library.LauncherFilter);
        Assert.Equal(["Beta"], s.Shown());

        // What a list sends when its entry goes away.
        s.Library.SelectedLauncherOption = null;
        Assert.Equal(LibraryViewModel.AllLaunchers, s.Library.LauncherFilter);
    }

    [Fact]
    public async Task A_scan_lists_the_games_and_keeps_the_selected_one()
    {
        using var s = new AppSetup();
        s.ScannerGames.Add(s.MakeGame("Alpha", Launcher.Steam));
        s.ScannerGames.Add(s.MakeGame("Beta", Launcher.Steam));
        s.Temp.File("games/Alpha/nvngx_dlssg.dll");
        s.Temp.File("games/Beta/nvngx_dlssg.dll");

        await s.Library.ScanAsync(s.Vm.Mod.Mod);

        Assert.Equal(["Alpha", "Beta"], s.Shown());
        Assert.Equal("Alpha", s.Library.SelectedGame?.Name);
        Assert.Contains("Found 2 games, 2 with DLSS Frame Generation", s.Vm.Status.Text);

        s.Library.SelectedGame = s.Library.Games.Single(g => g.Name == "Beta");
        await s.Library.ScanAsync(s.Vm.Mod.Mod);

        Assert.Equal("Beta", s.Library.SelectedGame?.Name);
        Assert.False(s.Vm.Status.IsBusy);
    }

    [Fact]
    public async Task Add_game_checks_only_that_game_instead_of_rescanning()
    {
        using var s = new AppSetup();
        // Not in the scanner's list, so a full rescan would drop it.
        s.AddGame("Alpha", Launcher.Steam);
        string exe = s.Temp.File("games/Mine/Mine.exe", "game");
        s.Temp.File("games/Mine/nvngx_dlssg.dll");
        s.Dialogs.PickedFile = exe;

        await s.Library.AddGameCommand.ExecuteAsync();

        Assert.Equal(["Alpha", "Mine"], s.Library.Games.Select(g => g.Name).Order());
        Assert.Equal("Mine", s.Library.SelectedGame?.Name);
        Assert.True(s.Library.SelectedGame!.HasFrameGen);
        Assert.Equal([exe], s.Settings.ManualGames);

        // Adding the same .exe again updates that card instead of adding a second one.
        await s.Library.AddGameCommand.ExecuteAsync();

        Assert.Equal(2, s.Library.Games.Count);
        Assert.Equal([exe], s.Settings.ManualGames);
    }

    [Fact]
    public async Task Adding_a_game_without_frame_gen_turns_off_the_filter_that_would_hide_it()
    {
        using var s = new AppSetup();
        s.Dialogs.PickedFile = s.Temp.File("games/Old/Old.exe", "game");

        await s.Library.AddGameCommand.ExecuteAsync();

        Assert.False(s.Library.OnlyFrameGen);
        Assert.Equal(["Old"], s.Shown());
    }

    [Fact]
    public async Task Cancelling_the_file_picker_changes_nothing()
    {
        using var s = new AppSetup();
        s.Dialogs.PickedFile = null;

        await s.Library.AddGameCommand.ExecuteAsync();

        Assert.Empty(s.Library.Games);
        Assert.Empty(s.Settings.ManualGames);
    }

    [Fact]
    public async Task A_scan_shows_which_games_have_the_mod()
    {
        using var s = new AppSetup();
        Game withMod = s.MakeGame("With mod", Launcher.Steam);
        File.WriteAllText(Path.Combine(withMod.Folder, "version.dll"), "MOD 1.7.0");
        Game without = s.MakeGame("Without", Launcher.Steam);
        s.ScannerGames.AddRange([withMod, without]);

        await s.Library.ScanAsync(s.Vm.Mod.Mod);

        GameViewModel[] games = [.. s.Library.Games];
        Assert.Equal(ModState.Installed, games[0].Status.State);
        Assert.Equal(["version.dll"], games[0].InstallNames);
        Assert.Equal(ModState.NotInstalled, games[1].Status.State);
        Assert.Equal(GameViewModel.AutoName, games[1].InstallNames[0]);
        Assert.Contains("version.dll", games[1].InstallNames);
    }

    [Fact]
    public async Task A_mod_picked_while_the_scan_runs_is_the_one_games_are_compared_with()
    {
        using var s = new AppSetup();
        Game game = s.MakeGame("Has 1.7.0", Launcher.Steam);
        File.WriteAllText(Path.Combine(game.Folder, "version.dll"), "MOD 1.7.0");
        using var scanStarted = new ManualResetEventSlim();
        using var carryOn = new ManualResetEventSlim();
        IEnumerable<Game> SlowLauncher()
        {
            scanStarted.Set();
            carryOn.Wait(TestContext.Current.CancellationToken);
            return [game];
        }
        var installer = new ModInstaller(s.Settings, () => { }, s.Temp.Folder("removed-here"), FakeMod.ReadVersion);
        var library = new LibraryViewModel(s.Settings, () => { }, new GameScanner(s.Settings, [SlowLauncher]), installer,
            new SteamStore(s.Web.Client(), s.Temp.Folder("cache-here")), s.Dialogs, new StatusViewModel());

        Task scan = library.ScanAsync(s.Vm.Mod.Mod);
        scanStarted.Wait(TestContext.Current.CancellationToken);
        // What the window does when you pick a newer DLL.
        library.Mod = FakeMod.Create(s.Temp, "1.8.0");
        carryOn.Set();
        await scan;

        Assert.Equal(ModState.Outdated, Assert.Single(library.Games).Status.State);
    }
}
