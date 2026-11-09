using MfgManager.App.ViewModels;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;

namespace MfgManager.Tests.ViewModels;

public class GameActionsViewModelTests
{
    private static string DllIn(GameViewModel game) => Path.Combine(game.Game.ExeFolder!, "version.dll");

    [Fact]
    public async Task Install_and_remove_change_the_game_folder()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;

        await s.Actions.InstallCommand.ExecuteAsync();

        Assert.True(File.Exists(DllIn(game)));
        Assert.Equal("Installed", game.Badge);
        Assert.True(game.BadgeIsInstalled);
        Assert.StartsWith("Installed MFG Unlocker v1.7.0 in Alpha", s.Vm.Status.Text);

        await s.Actions.RemoveCommand.ExecuteAsync();

        Assert.False(File.Exists(DllIn(game)));
        Assert.Null(game.Badge);
        // The details panel keeps showing the same game.
        Assert.Same(game, s.Library.SelectedGame);
    }

    [Fact]
    public async Task The_install_name_you_pick_is_used()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        game.SelectedInstallName = "winmm.dll";

        await s.Actions.InstallCommand.ExecuteAsync();

        Assert.True(File.Exists(Path.Combine(game.Game.ExeFolder!, "winmm.dll")));
        Assert.Equal(["winmm.dll"], game.InstallNames);
    }

    [Fact]
    public async Task Anti_cheat_game_warns_first_and_only_installs_after_yes()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Shooter", Launcher.Steam, antiCheat: "Easy Anti-Cheat");
        s.Library.SelectedGame = game;

        // "No, don't install".
        s.Dialogs.Answer = false;
        await s.Actions.InstallCommand.ExecuteAsync();
        string warning = s.Dialogs.Messages.Single();
        Assert.Contains("Easy Anti-Cheat", warning);
        Assert.Contains("ban your account", warning);
        Assert.False(game.CanRemove);
        Assert.False(File.Exists(DllIn(game)));

        s.Dialogs.Answer = true;
        await s.Actions.InstallCommand.ExecuteAsync();
        Assert.True(game.CanRemove);
        Assert.True(File.Exists(DllIn(game)));
    }

    [Fact]
    public async Task A_game_without_frame_gen_asks_first()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Plain", Launcher.Steam, frameGen: false);
        s.Library.SelectedGame = game;
        s.Dialogs.Answer = false;

        await s.Actions.InstallCommand.ExecuteAsync();

        Assert.Contains("doesn't seem to have DLSS Frame Generation", s.Dialogs.Messages.Single());
        Assert.False(File.Exists(DllIn(game)));
    }

    [Fact]
    public async Task A_running_game_is_not_changed()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        s.RunningExes.Add(game.Game.Exe!);

        await s.Actions.InstallCommand.ExecuteAsync();
        Assert.False(File.Exists(DllIn(game)));
        Assert.Contains("Close Alpha first", s.Dialogs.Messages.Last());

        s.RunningExes.Clear();
        await s.Actions.InstallCommand.ExecuteAsync();
        s.RunningExes.Add(game.Game.Exe!);

        await s.Actions.RemoveCommand.ExecuteAsync();
        Assert.True(File.Exists(DllIn(game)));

        await s.Actions.RevertAllCommand.ExecuteAsync();
        Assert.True(File.Exists(DllIn(game)));
    }

    [Fact]
    public async Task Revert_all_lists_the_games_and_removes_the_mod_from_each()
    {
        using var s = new AppSetup();
        GameViewModel alpha = s.AddGame("Alpha", Launcher.Steam);
        GameViewModel beta = s.AddGame("Beta", Launcher.Steam);
        s.AddGame("Untouched", Launcher.Steam);
        foreach (GameViewModel game in new[] { alpha, beta })
        {
            s.Library.SelectedGame = game;
            await s.Actions.InstallCommand.ExecuteAsync();
        }

        await s.Actions.RevertAllCommand.ExecuteAsync();

        Assert.False(File.Exists(DllIn(alpha)));
        Assert.False(File.Exists(DllIn(beta)));
        string question = s.Dialogs.Messages.Last();
        Assert.Contains("Alpha", question);
        Assert.Contains("Beta", question);
        Assert.DoesNotContain("Untouched", question);
        Assert.Equal("Removed the mod from 2 games.", s.Vm.Status.Text);
    }

    [Fact]
    public async Task Removing_can_also_move_the_mods_settings_and_logs_away()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        await s.Actions.InstallCommand.ExecuteAsync();
        string log = Path.Combine(game.Game.ExeFolder!, "RTXMFG-log.txt");
        File.WriteAllText(log, "log");

        s.Actions.RemoveModData = true;
        await s.Actions.RemoveCommand.ExecuteAsync();

        Assert.False(File.Exists(log));
    }

    [Fact]
    public async Task A_newer_mod_makes_installed_games_show_update_and_keep_their_file_name()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        await s.Actions.InstallCommand.ExecuteAsync();

        // The app re-checks every game when the mod changes.
        s.Vm.Mod.Mod = FakeMod.Create(s.Temp, "1.8.0");

        Assert.Equal("Update", game.Badge);
        Assert.True(game.BadgeIsUpdate);
        Assert.Equal(["version.dll"], game.InstallNames);
        Assert.False(game.CanChooseName);
    }

    [Fact]
    public async Task Change_exe_is_refused_while_the_mod_is_installed()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        await s.Actions.InstallCommand.ExecuteAsync();
        s.Dialogs.PickedFile = s.Temp.File("elsewhere/Other.exe", "game");

        await s.Actions.ChangeExeCommand.ExecuteAsync();

        Assert.Contains("Remove the mod from this game first", s.Dialogs.Messages.Last());
        Assert.Empty(s.Settings.ExeOverrides);
    }

    [Fact]
    public async Task Change_exe_is_remembered()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        string other = s.Temp.File("games/Alpha/bin/Other.exe", "game");
        s.Dialogs.PickedFile = other;

        await s.Actions.ChangeExeCommand.ExecuteAsync();

        Assert.Equal(other, s.Settings.ExeOverrides[game.Folder]);
        Assert.Equal(other, game.ExeText);
    }

    [Fact]
    public async Task Change_exe_refuses_a_program_outside_the_games_folder()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        string before = game.ExeText;
        s.Dialogs.PickedFile = s.Temp.File("elsewhere/Other.exe", "game");

        await s.Actions.ChangeExeCommand.ExecuteAsync();

        Assert.Contains("isn't in Alpha's folder", s.Dialogs.Messages.Single());
        Assert.Empty(s.Settings.ExeOverrides);
        Assert.Equal(before, game.ExeText);
    }

    [Fact]
    public async Task A_taken_file_name_shows_the_reason_and_changes_nothing()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        game.SelectedInstallName = "dxgi.dll";
        s.Temp.File("games/Alpha/dxgi.dll", "the game's own file");

        await s.Actions.InstallCommand.ExecuteAsync();

        Assert.Contains("dxgi.dll already exists", s.Dialogs.Messages.Single());
        Assert.Equal("the game's own file", File.ReadAllText(Path.Combine(game.Game.ExeFolder!, "dxgi.dll")));
    }

    // ---------- buttons enable and disable by themselves; a stuck button would break the app ----------

    [Fact]
    public async Task Buttons_enable_and_disable_as_things_change()
    {
        using var s = new AppSetup();
        GameActionsViewModel actions = s.Actions;
        // Nothing selected.
        Assert.False(actions.InstallCommand.CanExecute());
        Assert.False(actions.RemoveCommand.CanExecute());
        Assert.False(actions.RevertAllCommand.CanExecute());
        Assert.False(actions.ChangeExeCommand.CanExecute());
        Assert.False(actions.OpenFolderCommand.CanExecute());

        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        Assert.True(actions.InstallCommand.CanExecute());
        Assert.True(actions.ChangeExeCommand.CanExecute());
        Assert.True(actions.OpenFolderCommand.CanExecute());
        Assert.False(actions.RemoveCommand.CanExecute());

        await actions.InstallCommand.ExecuteAsync();
        Assert.True(actions.RemoveCommand.CanExecute());
        Assert.True(actions.RevertAllCommand.CanExecute());

        s.Vm.Mod.Mod = null;
        // No DLL to install.
        Assert.False(actions.InstallCommand.CanExecute());
    }

    [Fact]
    public async Task The_window_is_told_each_time_a_button_should_recheck_itself()
    {
        using var s = new AppSetup();
        // The first game gets selected by itself.
        s.AddGame("Alpha", Launcher.Steam);
        GameViewModel game = s.AddGame("Beta", Launcher.Steam);
        int removeSignals = 0;
        int installSignals = 0;
        s.Actions.RemoveCommand.CanExecuteChanged += (_, _) => removeSignals++;
        s.Actions.InstallCommand.CanExecuteChanged += (_, _) => installSignals++;

        s.Library.SelectedGame = game;
        Assert.True(removeSignals > 0);
        Assert.True(installSignals > 0);

        removeSignals = 0;
        await s.Actions.InstallCommand.ExecuteAsync();
        // Remove must wake up after an install.
        Assert.True(removeSignals > 0);
    }

    [Fact]
    public async Task A_button_is_disabled_while_its_own_action_runs()
    {
        var gate = new TaskCompletionSource();
        var command = new RelayCommand(() => gate.Task);

        Task running = command.ExecuteAsync();
        Assert.False(command.CanExecute());

        gate.SetResult();
        await running;
        Assert.True(command.CanExecute());
    }

    [Fact]
    public async Task Rescan_and_add_game_are_disabled_while_busy()
    {
        using var s = new AppSetup();
        Assert.True(s.Library.RescanCommand.CanExecute());

        s.Vm.Status.IsBusy = true;

        Assert.False(s.Library.RescanCommand.CanExecute());
        Assert.False(s.Library.AddGameCommand.CanExecute());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Copying_the_Linux_launch_option_does_nothing_without_one()
    {
        using var s = new AppSetup();
        s.Library.SelectedGame = s.AddGame("Alpha", Launcher.Steam);

        // The mod isn't installed yet.
        await s.Actions.CopyLaunchOptionCommand.ExecuteAsync();

        Assert.Null(s.Dialogs.Copied);
    }

    [Fact]
    public async Task Buttons_that_change_games_wait_while_a_scan_runs()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        await s.Actions.InstallCommand.ExecuteAsync();
        Assert.True(game.CanRemove);
        RelayCommand[] buttons = [s.Actions.InstallCommand, s.Actions.RemoveCommand, s.Actions.RevertAllCommand, s.Actions.ChangeExeCommand];
        int changes = 0;
        foreach (RelayCommand button in buttons) button.CanExecuteChanged += (_, _) => changes++;

        s.Vm.Status.IsBusy = true;
        Assert.All(buttons, button => Assert.False(button.CanExecute()));

        s.Vm.Status.IsBusy = false;
        Assert.All(buttons, button => Assert.True(button.CanExecute()));
        // The window only re-checks a button when it's told to.
        Assert.True(changes >= buttons.Length * 2);
    }

    [Fact]
    public async Task Install_runs_in_the_background_and_the_window_shows_it_until_done()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        using var started = new ManualResetEventSlim();
        using var carryOn = new ManualResetEventSlim();
        int workThread = 0;
        // Saving happens inside Install, so holding it here holds the install half-way, like a slow disk would.
        void SlowSave()
        {
            workThread = Environment.CurrentManagedThreadId;
            started.Set();
            carryOn.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        var installer = new ModInstaller(s.Settings, SlowSave, s.Temp.Folder("removed-here"), FakeMod.ReadVersion);
        var actions = new GameActionsViewModel(s.Library, s.Vm.Mod, installer, s.Settings, SlowSave, s.Dialogs, s.Vm.Status, _ => false);

        Task install = actions.InstallCommand.ExecuteAsync();
        Assert.True(started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        // The click came back while the install was still going: the window would have kept answering.
        Assert.False(install.IsCompleted);
        Assert.NotEqual(Environment.CurrentManagedThreadId, workThread);
        Assert.True(s.Vm.Status.IsBusy);
        Assert.Equal("Installing MFG Unlocker in Alpha...", s.Vm.Status.Text);
        Assert.False(actions.Changing.IsCompleted);
        Assert.False(actions.RemoveCommand.CanExecute());

        carryOn.Set();
        await install;

        Assert.True(actions.Changing.IsCompleted);
        Assert.False(s.Vm.Status.IsBusy);
        Assert.Equal("Installed MFG Unlocker v1.7.0 in Alpha as version.dll.", s.Vm.Status.Text);
        Assert.True(game.CanRemove);
    }

    [Fact]
    public async Task A_failed_change_shows_why_puts_the_status_back_and_lets_the_window_close()
    {
        using var s = new AppSetup();
        GameViewModel game = s.AddGame("Alpha", Launcher.Steam);
        s.Library.SelectedGame = game;
        game.SelectedInstallName = "dxgi.dll";
        s.Temp.File("games/Alpha/dxgi.dll", "the game's own file");
        s.Vm.Status.Text = "Found 1 game.";

        await s.Actions.InstallCommand.ExecuteAsync();

        Assert.Contains("dxgi.dll already exists", s.Dialogs.Messages.Single());
        Assert.Equal("Found 1 game.", s.Vm.Status.Text);
        Assert.False(s.Vm.Status.IsBusy);
        // Closing the window waits for this, so it mustn't fail even though the install did.
        Assert.True(s.Actions.Changing.IsCompletedSuccessfully);
    }
}
