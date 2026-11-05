using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MfgManager.App.Components.Library;
using MfgManager.App.Components.Shell;
using MfgManager.App.ViewModels;
using MfgManager.Core.Models;
using MfgManager.Core.Settings;
using MfgManager.Tests.Components;

namespace MfgManager.Tests.ViewModels;

/// <summary>
/// The rule: a full game scan only happens when the app opens and when you press Rescan. GitHub and the Downloads folder
/// are only checked when the app opens. Nothing runs on its own in the background.
/// These tests fail if a later change starts a scan from anywhere else.
/// </summary>
public class ScanRulesTests
{
    private const string GitHubCheck = "releases/latest";

    private static AppSetup AppWithTwoGames()
    {
        var s = new AppSetup();
        s.ScannerGames.Add(s.MakeGame("Alpha", Launcher.Steam));
        s.ScannerGames.Add(s.MakeGame("Beta", Launcher.Ubisoft));
        // A scan looks at the real files, and the window only shows Frame Generation games at first.
        s.Temp.File("games/Alpha/nvngx_dlssg.dll");
        s.Temp.File("games/Beta/nvngx_dlssg.dll");
        return s;
    }

    /// <summary>Every button and setting in the app, apart from Rescan.</summary>
    private static async Task DoEverythingExceptRescan(AppSetup s)
    {
        s.Vm.Mod.Mod = FakeMod.Create(s.Temp, "1.7.0");
        var alpha = s.Library.Games.Single(g => g.Name == "Alpha");

        // Looking around
        s.Library.SelectedGame = s.Library.Games.Single(g => g.Name == "Beta");
        s.Library.SearchText = "a";
        s.Library.SearchText = "";
        s.Library.OnlyFrameGen = false;
        s.Library.LauncherFilter = "Steam";
        s.Library.LauncherFilter = LibraryViewModel.AllLaunchers;
        s.Library.DrmFilter = GameViewModel.NoDrmFound;
        s.Library.DrmFilter = LibraryViewModel.AnyDrm;
        s.Library.GroupBy = LibraryViewModel.GroupByDrm;
        s.Library.GroupBy = LibraryViewModel.NoGrouping;
        s.Library.GroupBy = LibraryViewModel.GroupByLauncher;
        s.Vm.Theme = ThemeChoice.Dark;

        // Changing things. The checks in between make sure each button really did its work.
        s.Library.SelectedGame = alpha;
        await s.Actions.InstallCommand.ExecuteAsync();
        Assert.True(alpha.CanRemove);
        await s.Actions.RemoveCommand.ExecuteAsync();
        Assert.False(alpha.CanRemove);
        await s.Actions.InstallCommand.ExecuteAsync();
        await s.Actions.RevertAllCommand.ExecuteAsync();
        Assert.False(alpha.CanRemove);

        s.Dialogs.PickedFile = s.Temp.File("games/Alpha/Other.exe", "game");
        await s.Actions.ChangeExeCommand.ExecuteAsync();
        Assert.Equal(s.Dialogs.PickedFile, alpha.Game.Exe);

        s.Dialogs.PickedFile = s.Temp.File("games/Mine/Mine.exe", "game");
        await s.Library.AddGameCommand.ExecuteAsync();
        Assert.Contains(s.Library.Games, g => g.Name == "Mine");

        s.Dialogs.PickedFile = s.WriteRealLookingMod("picked", "1.8.0");
        await s.Vm.Mod.ChooseCommand.ExecuteAsync();
        Assert.Equal(new Version(1, 8, 0), s.Vm.Mod.Mod!.Version);

        await s.Vm.Mod.DownloadCommand.ExecuteAsync();
    }

    [Fact]
    public async Task Games_are_scanned_once_at_start_and_after_that_only_by_Rescan()
    {
        using var s = AppWithTwoGames();
        Assert.Equal(0, s.Scans);   // creating the app isn't starting it

        await s.Vm.InitializeAsync();
        Assert.Equal(1, s.Scans);

        await DoEverythingExceptRescan(s);
        Assert.Equal(1, s.Scans);

        await s.Library.RescanCommand.ExecuteAsync();
        Assert.Equal(2, s.Scans);

        await DoEverythingExceptRescan(s);
        Assert.Equal(2, s.Scans);
    }

    [Fact]
    public async Task GitHub_is_only_asked_at_start_whatever_you_do_afterwards()
    {
        using var s = AppWithTwoGames();
        s.Web.Add("https://api.github.com/", """{"tag_name":"v1.9.0","html_url":"https://github.com/x","assets":[]}""");
        int Checks() => s.Web.Requests.Count(url => url.Contains(GitHubCheck));

        await s.Vm.InitializeAsync();
        Assert.Equal(1, Checks());

        await DoEverythingExceptRescan(s);
        await s.Library.RescanCommand.ExecuteAsync();

        Assert.Equal(1, Checks());
    }

    [Fact]
    public async Task Downloads_is_only_searched_at_start_not_by_Rescan()
    {
        using var s = AppWithTwoGames();
        await s.Vm.InitializeAsync();
        Assert.Empty(s.Dialogs.Messages);

        s.WriteRealLookingMod("downloads/MFGUnlocker-v9.0.0", "9.0.0");
        await s.Library.RescanCommand.ExecuteAsync();
        Assert.Empty(s.Dialogs.Messages);

        // The search does find that file, so the check above really proves Rescan didn't look.
        await s.Vm.Mod.FindAsync();
        Assert.Contains("v9.0.0", s.Dialogs.Messages.Single());
    }

    [Fact]
    public async Task Rescan_doesnt_ask_Steam_again_about_a_game_it_already_answered_for()
    {
        using var s = AppWithTwoGames();
        s.Web.Add("https://store.steampowered.com/api/storesearch", """{"items":[]}""");
        int SteamRequests() => s.Web.Requests.Count(url => url.Contains("steampowered"));

        await s.Vm.InitializeAsync();
        Assert.Equal(2, SteamRequests());   // one search per game, both "not found"

        await s.Library.RescanCommand.ExecuteAsync();
        Assert.Equal(2, SteamRequests());
    }

    [Fact]
    public Task Using_the_real_window_with_the_mouse_never_starts_a_scan() => TestApp.Run(async () =>
    {
        using var s = AppWithTwoGames();
        await s.Vm.InitializeAsync();
        var window = Stage.Show(new MainWindow(), "Dark", 1280, 820, s.Vm);

        void Click(Control control)
        {
            var middle = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
            var point = control.TranslatePoint(middle, window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Stage.Settle();
        }
        Button ButtonNamed(string text) => window.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == text);

        var beta = window.GetVisualDescendants().OfType<GameCard>().Single(c => ((GameViewModel)c.DataContext!).Name == "Beta");
        Click(beta.GetVisualDescendants().OfType<Button>().Single());
        Assert.Equal("Beta", s.Library.SelectedGame?.Name);

        Click(window.GetVisualDescendants().OfType<CheckBox>().First(c => (c.Content as string) == "Only frame-gen games"));
        Assert.False(s.Library.OnlyFrameGen);

        Click(window.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1));   // a launcher in the sidebar
        window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search games").Text = "a";
        Stage.Settle();

        // Leaving the window and coming back doesn't count either.
        window.WindowState = WindowState.Minimized;
        Stage.Settle();
        window.WindowState = WindowState.Normal;
        window.Activate();
        Stage.Settle();

        Assert.Equal(1, s.Scans);

        // And the real Rescan button starts exactly one.
        Click(ButtonNamed("Rescan"));
        for (var i = 0; i < 200 && (s.Scans < 2 || s.Vm.Status.IsBusy); i++)
        {
            await Task.Delay(10);
            Stage.Settle();
        }
        Assert.Equal(2, s.Scans);
        window.Close();
    });

    // ---------------- the source code itself ----------------

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MfgManager.sln"))) root = root.Parent;
        Assert.NotNull(root);

        return Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), pattern, SearchOption.AllDirectories)
            .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
    }

    /// <summary>Which files call a method. Each call counts once, and the method's own definition doesn't count.</summary>
    private static List<string> FilesCalling(string method) =>
        SourceFiles("*.cs")
            .SelectMany(file => File.ReadLines(file)
                .Where(line => Regex.IsMatch(line, $@"\b{method}\(") && !line.TrimStart().StartsWith("public ") && !line.TrimStart().StartsWith("///"))
                .Select(_ => Path.GetFileName(file)))
            .Order()
            .ToList();

    /// <summary>A new line in this list has to be a deliberate decision, because it means a new way to start a scan.</summary>
    [Theory]
    [InlineData("InitializeAsync", "App.axaml.cs")]                                // the window opened
    [InlineData("ScanAsync", "LibraryViewModel.cs", "MainViewModel.cs")]           // Rescan, and the start
    [InlineData("Scan", "LibraryViewModel.cs")]                                    // only through ScanAsync
    [InlineData("FindAsync", "MainViewModel.cs")]                                  // Mods and Downloads, at the start
    [InlineData("CheckGitHubOnceAsync", "MainViewModel.cs")]                       // at the start
    [InlineData("GetLatestAsync", "ModViewModel.cs")]                              // only through CheckGitHubOnceAsync
    public void Scans_can_only_be_started_from_the_known_places(string method, params string[] expectedFiles)
    {
        Assert.Equal(expectedFiles.Order(), FilesCalling(method));
    }

    [Fact]
    public void Only_the_Rescan_button_starts_a_rescan()
    {
        var uses = SourceFiles("*.axaml").Where(file => File.ReadAllText(file).Contains("RescanCommand")).Select(Path.GetFileName);
        Assert.Equal("LibraryToolbar.axaml", Assert.Single(uses));
    }

    [Fact]
    public void Nothing_in_the_app_runs_on_a_timer_or_watches_for_changes()
    {
        var banned = new Regex(@"FileSystemWatcher|DispatcherTimer|PeriodicTimer|System\.Timers|Threading\.Timer|new Timer\(|Task\.Delay|Thread\.Sleep|BackgroundService|\bActivated\b|\bGotFocus\b");

        var found = SourceFiles("*.cs").SelectMany(file => File.ReadLines(file)
            .Where(line => banned.IsMatch(line))
            .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"));

        Assert.Empty(found);
    }
}
