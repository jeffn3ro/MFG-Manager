using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using MfgManager.App.Components.Banners;
using MfgManager.App.Components.Details;
using MfgManager.App.Components.Dialogs;
using MfgManager.App.Components.Library;
using MfgManager.App.Components.Shell;
using MfgManager.App.Components.Sidebar;
using MfgManager.App.Components.Status;
using MfgManager.App.Components.Toolbar;
using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Store;
using MfgManager.Tests.ViewModels;

namespace MfgManager.Tests.Components;

/// <summary>
/// Draws every component in both modes with realistic data. Each test checks that the component shows the right things
/// and that all its text is easy to read. Set MFG_SHOTS to a folder to get a picture of each one.
/// </summary>
public class ComponentTests
{
    public static TheoryData<string> Modes => Stage.Modes;

    /// <summary>A library with every kind of game: installed, update ready, broken, anti-cheat, and no cover.</summary>
    internal static AppSetup Library()
    {
        var s = new AppSetup();
        GameViewModel installed = s.AddGame("Alpha Strike", Launcher.Steam, drm: ["Steamworks", "Denuvo"]);
        s.AddGame("Beta Run", Launcher.Ubisoft, drm: "Ubisoft Connect");
        GameViewModel cheat = s.AddGame("Cheat Hunter", Launcher.Steam, antiCheat: "Easy Anti-Cheat");
        GameViewModel outdated = s.AddGame("Delta Force Reloaded With A Very Long Name That Wraps", Launcher.Gog);
        s.AddGame("Epsilon", Launcher.Ea);
        GameViewModel missing = s.AddGame("Zeta", Launcher.Epic);

        var installer = new ModInstaller(s.Settings, () => { }, s.Temp.Folder("removed2"), FakeMod.ReadVersion);
        foreach (GameViewModel game in new[] { installed, cheat, outdated, missing }) installer.Install(game.Game, s.Vm.Mod.Mod!);
        // "Check".
        File.Delete(Path.Combine(missing.Game.ExeFolder!, "version.dll"));

        // An older copy in one game, so it shows "Update".
        File.WriteAllText(Path.Combine(outdated.Game.ExeFolder!, "version.dll"), "MOD 1.6.0");

        installed.StoreDrm = new StoreDrm("Denuvo Anti-tamper. 5 a day machine activation limit", ["Denuvo"]);
        s.Library.RefreshStatuses();
        s.Library.RefreshFilterOptions();
        s.Library.SelectedGame = installed;
        return s;
    }

    private static void CheckAndSnap(Window window, string name, string mode)
    {
        Assert.True(ReadabilityCheck.CountTexts(window) > 0, $"{name} shows no text at all");
        ReadabilityCheck.Assert_all_text_is_readable(window, $"{name} in {mode} mode");
        Stage.Snapshot(window, name, mode);
    }

    private static List<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "")];

    [Theory, MemberData(nameof(Modes))]
    public Task Sidebar_lists_launchers_and_drm_with_counts(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        Window window = Stage.Show(new Sidebar { DataContext = s.Vm }, mode, 248, 700);

        List<string> texts = Texts(window);
        Assert.Contains("LIBRARY", texts);
        Assert.Contains("DRM", texts);
        Assert.Contains("Steam", texts);
        Assert.Contains("Denuvo", texts);
        Assert.Contains("MFG Unlocker v1.7.0", texts);
        CheckAndSnap(window, "Sidebar", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Clicking_a_launcher_in_the_sidebar_filters_the_games(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        Window window = Stage.Show(new Sidebar { DataContext = s.Vm }, mode, 248, 700);

        var lists = window.GetVisualDescendants().OfType<ListBox>().ToList();
        lists[0].SelectedItem = s.Library.LauncherOptions.Single(o => o.Name == "Ubisoft");
        Stage.Settle();

        Assert.Equal("Ubisoft", s.Library.LauncherFilter);
        Assert.Equal(["Beta Run"], s.Shown());
        CheckAndSnap(window, "Sidebar-selected", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Toolbar_controls_reach_the_library(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        Window window = Stage.Show(new LibraryToolbar { DataContext = s.Vm }, mode, 660, 120);

        TextBox search = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search games");
        search.Text = "beta";
        Stage.Settle();
        Assert.Equal(["Beta Run"], s.Shown());
        search.Text = "";

        CheckBox onlyFrameGen = window.GetVisualDescendants().OfType<CheckBox>().Single();
        onlyFrameGen.IsChecked = false;
        Stage.Settle();
        Assert.False(s.Library.OnlyFrameGen);

        ComboBox groupBy = window.GetVisualDescendants().OfType<ComboBox>().Single();
        groupBy.SelectedItem = LibraryViewModel.GroupByDrm;
        Stage.Settle();
        Assert.Equal(LibraryViewModel.GroupByDrm, s.Library.GroupBy);

        CheckAndSnap(window, "Toolbar", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Search_box_placeholder_is_readable(string mode) => TestApp.Run(() =>
    {
        using var s = new AppSetup();
        Window window = Stage.Show(new LibraryToolbar { DataContext = s.Vm }, mode, 660, 120);

        Assert.Contains("Search games", Texts(window));
        CheckAndSnap(window, "Toolbar-empty", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Banners_appear_only_when_they_matter(string mode) => TestApp.Run(async () =>
    {
        using var s = new AppSetup();
        Window window = Stage.Show(new BannerList { DataContext = s.Vm }, mode, 660, 200);
        // Nothing to say.
        Assert.Empty(Texts(window));

        s.Web.Add("https://api.github.com/", """{"tag_name":"v1.8.0","html_url":"https://github.com/x","assets":[]}""");
        await s.Vm.Mod.CheckGitHubOnceAsync();
        s.Library.SelectedGame = s.AddGame("Cheat", Launcher.Steam, antiCheat: "BattlEye");
        await s.Actions.InstallCommand.ExecuteAsync();
        Stage.Settle();

        List<string> texts = Texts(window);
        Assert.Contains("MFG Unlocker v1.8.0 is out (you have v1.7.0).", texts);
        // The question before installing is warning enough.
        Assert.DoesNotContain(texts, t => t.Contains("anti-cheat"));
        CheckAndSnap(window, "Banners", mode);

        s.Vm.Mod.Mod = null;
        Stage.Settle();
        Assert.Contains(Texts(window), t => t.StartsWith("No MFGUnlocker.dll", StringComparison.Ordinal));
        CheckAndSnap(window, "Banners-missing", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Cover_wall_shows_groups_and_cards_with_labels(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        s.Library.OnlyFrameGen = false;
        Window window = Stage.Show(new CoverWall { DataContext = s.Library }, mode, 660, 700);

        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).ToList();
        Assert.Equal(6, window.GetVisualDescendants().OfType<GameCard>().Count());
        Assert.Contains(texts, t => t.Text == "Installed");
        Assert.Contains(texts, t => t.Text == "Update");
        Assert.Contains(texts, t => t.Text == "Anti-cheat");
        // Initials, because there's no cover.
        Assert.Contains(texts, t => t.Text == "AS");
        CheckAndSnap(window, "CoverWall", mode);
        window.Close();
    });

    [Fact]
    public Task Group_headings_show_name_and_count_and_vanish_without_grouping() => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        Window window = Stage.Show(new CoverWall { DataContext = s.Library }, "Dark", 660, 700);

        string Headings() => string.Join("|", window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("heading"))
            .Select(t => string.Concat(t.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)).Trim()));

        Assert.Contains("Steam", Headings());
        Assert.Contains("(2)", Headings());

        s.Library.GroupBy = LibraryViewModel.NoGrouping;
        Stage.Settle();
        Assert.Equal("", Headings());
        window.Close();
    });

    [Fact]
    public Task A_real_mouse_click_on_a_card_selects_the_game() => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        Window window = Stage.Show(new CoverWall { DataContext = s.Library }, "Dark", 660, 700);
        var cards = window.GetVisualDescendants().OfType<GameCard>().ToList();
        // Its group is at the top, so it's on screen.
        GameCard target = cards.First(c => ((GameViewModel)c.DataContext!).Name == "Epsilon");

        Point point = target.TranslatePoint(new Avalonia.Point(40, 40), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Stage.Settle();

        Assert.Equal("Epsilon", s.Library.SelectedGame?.Name);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Empty_library_says_what_to_do(string mode) => TestApp.Run(() =>
    {
        using var s = new AppSetup();
        Window window = Stage.Show(new CoverWall { DataContext = s.Library }, mode, 660, 300);

        Assert.Contains(Texts(window), t => t.StartsWith("No games to show", StringComparison.Ordinal));
        CheckAndSnap(window, "CoverWall-empty", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Details_panel_shows_everything_about_the_game(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        Window window = Stage.Show(new DetailsPanel { DataContext = s.Actions }, mode, 372, 800);

        List<string> texts = Texts(window);
        Assert.Contains("Alpha Strike", texts);
        Assert.Contains("Steam", texts);
        Assert.Contains("Yes (DLSS Frame Generation found)", texts);
        Assert.Contains("Denuvo", texts);
        Assert.Contains("Steamworks", texts);
        Assert.Contains("Steam store says: Denuvo Anti-tamper. 5 a day machine activation limit", texts);
        Assert.Contains("Installed as version.dll (v1.7.0)", texts);
        // It can wrap between folders.
        Assert.Contains(s.Library.SelectedGame!.ExeText, texts.Select(t => t.Replace("​", "")));
        Assert.Contains("Install / Update", texts);
        Assert.Contains("Remove (revert)", texts);
        Assert.Contains("Change .exe...", texts);
        Assert.Contains("Open folder", texts);
        CheckAndSnap(window, "Details-installed", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Details_panel_warns_about_anti_cheat_and_nothing_selected(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        s.Library.SelectedGame = s.Library.Games.Single(g => g.Name == "Cheat Hunter");
        Window window = Stage.Show(new DetailsPanel { DataContext = s.Actions }, mode, 372, 800);

        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => string.Concat(t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []).Contains("Easy Anti-Cheat"));
        CheckAndSnap(window, "Details-anticheat", mode);

        s.Library.SelectedGame = null;
        Stage.Settle();
        Assert.Contains("Pick a game to see its details.", Texts(window));
        CheckAndSnap(window, "Details-none", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Disabled_buttons_and_dropdowns_are_still_readable(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        // Install is disabled now.
        s.Vm.Mod.Mod = null;
        // Remove is disabled too.
        s.Library.SelectedGame = s.Library.Games.Single(g => g.Name == "Beta Run");
        Window window = Stage.Show(new DetailsPanel { DataContext = s.Actions }, mode, 372, 800);

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.False(buttons.Single(b => (b.Content as string) == "Install / Update").IsEffectivelyEnabled);
        Assert.False(buttons.Single(b => (b.Content as string) == "Remove (revert)").IsEffectivelyEnabled);
        CheckAndSnap(window, "Details-disabled", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Status_bar_shows_the_message_and_a_busy_bar(string mode) => TestApp.Run(() =>
    {
        using var s = new AppSetup();
        s.Vm.Status.Text = "Found 16 games, 3 with DLSS Frame Generation.";
        Window window = Stage.Show(new StatusBar { DataContext = s.Vm.Status }, mode, 800, 40);

        Assert.Contains("Found 16 games, 3 with DLSS Frame Generation.", Texts(window));
        ProgressBar bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.False(bar.IsEffectivelyVisible);

        s.Vm.Status.IsBusy = true;
        Stage.Settle();
        Assert.True(bar.IsEffectivelyVisible);
        CheckAndSnap(window, "StatusBar", mode);
        window.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task Message_dialog_questions_default_to_no_and_messages_only_have_OK(string mode) => TestApp.Run(() =>
    {
        var question = new MessageDialog("Remove the mod from every game?\n\n  • Alpha\n  • Beta", MessageKind.Warning, isQuestion: true);
        Stage.Show(question, mode, 480, 220);
        var buttons = question.GetVisualDescendants().OfType<Button>().ToList();
        Assert.True(buttons.Single(b => (b.Content as string) == "No").IsDefault);
        Assert.False(buttons.Single(b => (b.Content as string) == "Yes").IsDefault);
        CheckAndSnap(question, "Dialog-question", mode);
        question.Close();

        var message = new MessageDialog("Download failed: something went wrong.", MessageKind.Error, isQuestion: false);
        Stage.Show(message, mode, 480, 160);
        var visible = message.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
        Assert.Equal("OK", Assert.Single(visible).Content);
        CheckAndSnap(message, "Dialog-error", mode);
        message.Close();
    });

    [Theory, MemberData(nameof(Modes))]
    public Task The_whole_window_works_together(string mode) => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        s.Library.OnlyFrameGen = false;
        Window window = Stage.Show(new MainWindow(), mode, 1280, 820, s.Vm);

        Assert.Equal(6, window.GetVisualDescendants().OfType<GameCard>().Count());
        CheckAndSnap(window, "MainWindow", mode);

        s.Library.SelectedGame = s.Library.Games.Single(g => g.Name == "Cheat Hunter");
        Stage.Settle();
        Assert.Contains("Cheat Hunter", Texts(window));
        CheckAndSnap(window, "MainWindow-anticheat", mode);
        window.Close();
    });

    [Fact]
    public Task Switching_mode_while_open_changes_the_colours_and_stays_readable() => TestApp.Run(() =>
    {
        using AppSetup s = Library();
        s.Vm.Theme = MfgManager.Core.Settings.ThemeChoice.Dark;
        Window window = Stage.Show(new MainWindow(), "Dark", 1280, 820, s.Vm);
        TextBlock title = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "MFG Manager");
        Color darkColor = ((Avalonia.Media.ISolidColorBrush)title.Foreground!).Color;

        s.Vm.Theme = MfgManager.Core.Settings.ThemeChoice.Light;
        Stage.Settle();

        Assert.NotEqual(darkColor, ((Avalonia.Media.ISolidColorBrush)title.Foreground!).Color);
        ReadabilityCheck.Assert_all_text_is_readable(window, "after switching to light");
        window.Close();
    });

    [Fact]
    public Task The_readability_check_itself_catches_bad_text() => TestApp.Run(() =>
    {
        var bad = new Window
        {
            Background = Avalonia.Media.Brushes.White,
            Content = new TextBlock { Text = "invisible", Foreground = Avalonia.Media.Brushes.White },
        };
        bad.Show();
        Stage.Settle();

        Assert.NotEmpty(ReadabilityCheck.FindProblems(bad));
        bad.Close();
    });
}
