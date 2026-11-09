using MfgManager.App.Services;
using MfgManager.Core;
using MfgManager.Core.Mod;
using MfgManager.Core.Platform;
using MfgManager.Core.Scanning;
using MfgManager.Core.Settings;

namespace MfgManager.App.ViewModels;

/// <summary>What you can do to games: install, remove, revert many, change the .exe, open the folder.</summary>
public sealed class GameActionsViewModel : ObservableObject
{
    private readonly LibraryViewModel _library;
    private readonly ModViewModel _mod;
    private readonly ModInstaller _installer;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly IDialogs _dialogs;
    private readonly StatusViewModel _status;
    private readonly Func<string, bool> _isRunning;

    /// <param name="isRunning">How to tell if a game is running. Tests swap this for a fake.</param>
    public GameActionsViewModel(LibraryViewModel library, ModViewModel mod, ModInstaller installer, AppSettings settings, Action saveSettings,
        IDialogs dialogs, StatusViewModel status, Func<string, bool>? isRunning = null)
    {
        _library = library;
        _mod = mod;
        _installer = installer;
        _settings = settings;
        _saveSettings = saveSettings;
        _dialogs = dialogs;
        _status = status;
        _isRunning = isRunning ?? GameProcesses.IsRunning;

        // These change game folders and settings, so they wait while a scan reads them in the background.
        InstallCommand = new RelayCommand(InstallAsync, () => !_status.IsBusy && Game?.Game.Exe is not null && _mod.Mod is not null)
            .WatchEnabledOn(_library, _mod, _status);
        RemoveCommand = new RelayCommand(RemoveAsync, () => !_status.IsBusy && Game?.CanRemove == true).WatchEnabledOn(_library, _status);
        RevertAllCommand = new RelayCommand(RevertAllAsync, () => !_status.IsBusy && _library.Games.Any(g => g.CanRemove)).WatchEnabledOn(_library, _status);
        ChangeExeCommand = new RelayCommand(ChangeExeAsync, () => !_status.IsBusy && Game is not null).WatchEnabledOn(_library, _status);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => Game?.Game.Exe is not null).WatchEnabledOn(_library);
        CopyLaunchOptionCommand = new RelayCommand(CopyLaunchOptionAsync);

        _library.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(LibraryViewModel.SelectedGame)) return;
            OnPropertyChanged(nameof(Game));
            OnPropertyChanged(nameof(HasGame));
            OnPropertyChanged(nameof(HasNoGame));
        };
    }

    /// <summary>The selected game.</summary>
    public GameViewModel? Game => _library.SelectedGame;
    public bool HasGame => Game is not null;
    public bool HasNoGame => Game is null;

    public bool RemoveModData
    {
        get;
        set => SetProperty(ref field, value);
    }

    public RelayCommand InstallCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand RevertAllCommand { get; }
    public RelayCommand ChangeExeCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand CopyLaunchOptionCommand { get; }

    private async Task InstallAsync()
    {
        if (Game is not { } game || _mod.Mod is not { } mod) return;

        if (game.HasAntiCheat && !await _dialogs.ConfirmAsync(
                $"{game.Name} uses {game.AntiCheat}.\n\nAnti-cheat can treat this mod as a cheat and ban your account, " +
                "especially in online modes. You can take it out again at any time with Remove.\n\nInstall anyway?", warning: true))
            return;
        if (!game.HasFrameGen && !await _dialogs.ConfirmAsync(
                $"{game.Name} doesn't seem to have DLSS Frame Generation, so the mod probably has nothing to unlock.\n\nInstall anyway?"))
            return;
        if (await IsRunningAsync(game)) return;

        bool wasInstalled = game.Status.DllName is not null;
        string? preferred = game.SelectedInstallName == GameViewModel.AutoName ? null : game.SelectedInstallName;
        string gameName = game.Name;
        await RunAsync($"Installing MFG Unlocker in {gameName}...", () =>
        {
            string name = _installer.Install(game.Game, mod, preferred);
            return $"{(wasInstalled ? "Updated" : "Installed")} MFG Unlocker v{mod.Version.ToString(3)} in {gameName} as {name}.";
        });
    }

    private async Task RemoveAsync()
    {
        if (Game is not { } game || await IsRunningAsync(game)) return;

        bool removeModData = RemoveModData;
        string gameName = game.Name;
        await RunAsync($"Removing the mod from {gameName}...", () => _installer.Remove(game.Game, removeModData)
            ? $"Removed the mod from {gameName}."
            : $"Forgot about the mod in {gameName} (its file was already gone or replaced).");
    }

    private async Task RevertAllAsync()
    {
        var games = _library.Games.Where(g => g.CanRemove).ToList();
        if (games.Count == 0) return;
        string names = string.Join("\n", games.Select(g => "  • " + g.Name));
        if (!await _dialogs.ConfirmAsync($"Remove the mod from every game?\n\n{names}", warning: true)) return;
        foreach (GameViewModel game in games)
            if (await IsRunningAsync(game)) return;

        bool removeModData = RemoveModData;
        string count = LibraryViewModel.CountOfGames(games.Count);
        var models = games.Select(game => game.Game).ToList();
        await RunAsync($"Removing the mod from {count}...", () =>
        {
            foreach (Core.Models.Game game in models) _installer.Remove(game, removeModData);
            return $"Removed the mod from {count}.";
        });
    }

    private async Task ChangeExeAsync()
    {
        if (Game is not { } game) return;
        if (game.CanRemove)
        {
            await _dialogs.ShowAsync("Remove the mod from this game first, then change the .exe.");
            return;
        }

        string? exe = await _dialogs.PickFileAsync($"Pick the .exe for {game.Name}", "Programs", "*.exe", game.Game.ExeFolder ?? game.Folder);
        if (exe is null) return;
        if (!PathUtil.IsInside(exe, game.Folder))
        {
            await _dialogs.ShowAsync($"That .exe isn't in {game.Name}'s folder, and the mod goes next to it. For a game somewhere else, use Add game.");
            return;
        }

        _settings.ExeOverrides[game.Folder] = exe;
        _saveSettings();
        await RunAsync($"Checking {game.Name}...", () =>
        {
            GameInspector.Inspect(game.Game, exe);
            return null;
        });
    }

    private void OpenFolder()
    {
        if (Game?.Game.Exe is { } exe) DesktopShell.RevealFile(exe);
    }

    private async Task CopyLaunchOptionAsync()
    {
        if (Game?.LinuxLaunchOption is not { } option) return;
        await _dialogs.CopyTextAsync(option);
        _status.Text = "Copied. In Steam: right-click the game, Properties, then paste it under Launch Options.";
    }

    private async Task<bool> IsRunningAsync(GameViewModel game)
    {
        if (game.Game.Exe is not { } exe || !_isRunning(exe)) return false;
        await _dialogs.ShowAsync($"Close {game.Name} first. Its files are in use while it runs.");
        return true;
    }

    /// <summary>
    /// The install, remove or check that's running right now, or a finished task when there's none. It never fails,
    /// so closing the window can wait for it without having to handle its errors.
    /// </summary>
    public Task Changing { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Changes game folders in the background, so a slow disk, or a virus scanner checking the new DLL, can't freeze
    /// the window. Shows any error in a dialog instead of crashing, then refreshes the cards. The buttons that change
    /// games wait until it's done.
    /// </summary>
    /// <param name="working">Shown in the status bar while it runs.</param>
    /// <param name="work">
    /// The change. It runs on another thread, so it mustn't touch anything on screen.
    /// It returns what the status bar shows afterwards, or null to leave it as it was.
    /// </param>
    private async Task RunAsync(string working, Func<string?> work)
    {
        string before = _status.Text;
        _status.IsBusy = true;
        _status.Text = working;
        try
        {
            Task<string?> running = Task.Run(work);
            // WhenAny finishes with the work but never throws its error; that's handled right below.
            Changing = Task.WhenAny(running);
            _status.Text = await running ?? before;
        }
        catch (UnauthorizedAccessException)
        {
            _status.Text = before;
            await _dialogs.ShowAsync(OperatingSystem.IsWindows()
                ? "Windows didn't allow changing files in that folder. Make sure MFG Manager runs as administrator."
                : "The system didn't allow changing files in that folder. Check that your user owns the game folder.", error: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            _status.Text = before;
            await _dialogs.ShowAsync(ex.Message, error: true);
        }

        try
        {
            await _library.RefreshStatusesAsync();
        }
        finally
        {
            _status.IsBusy = false;
        }
    }
}
