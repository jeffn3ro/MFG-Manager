using MfgManager.App.Services;
using MfgManager.Core.Mod;
using MfgManager.Core.Scanning;
using MfgManager.Core.Settings;
using MfgManager.Core.Store;

namespace MfgManager.App.ViewModels;

/// <summary>The whole window: it creates each part and connects them. The real work is in the parts.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public MainViewModel(AppSettings settings, Action saveSettings, GameScanner scanner, ModInstaller installer, SteamStore store,
        UpdateChecker updates, IDialogs dialogs, string modsFolder, string downloadsFolder, Func<string, bool>? isRunning = null)
    {
        _settings = settings;
        _saveSettings = saveSettings;

        Status = new StatusViewModel();
        Library = new LibraryViewModel(settings, saveSettings, scanner, installer, store, dialogs, Status);
        Mod = new ModViewModel(settings, saveSettings, updates, dialogs, Status, modsFolder, downloadsFolder);
        Actions = new GameActionsViewModel(Library, Mod, installer, settings, saveSettings, dialogs, Status, isRunning);

        // A different mod DLL means every game has to be compared against it again.
        Mod.ModChanged += () =>
        {
            Library.Mod = Mod.Mod;
            Library.RefreshStatuses();
        };

        ThemeService.Apply(settings.Theme);
    }

    public StatusViewModel Status { get; }
    public LibraryViewModel Library { get; }
    public ModViewModel Mod { get; }
    public GameActionsViewModel Actions { get; }

    /// <summary>Shown under the logo. It comes from the build, so it can't go out of date.</summary>
    public string VersionText { get; } = "Version " + typeof(MainViewModel).Assembly.GetName().Version!.ToString(3);

    public IReadOnlyList<ThemeChoice> ThemeOptions { get; } = [ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark];

    public ThemeChoice Theme
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value) return;
            _settings.Theme = value;
            _saveSettings();
            ThemeService.Apply(value);
            OnPropertyChanged();
        }
    }

    /// <summary>Runs once when the window opens: find the mod, scan, then check GitHub.</summary>
    public async Task InitializeAsync()
    {
        await Mod.FindAsync();
        await Library.ScanAsync(Mod.Mod);
        await Mod.CheckGitHubOnceAsync();
    }
}
