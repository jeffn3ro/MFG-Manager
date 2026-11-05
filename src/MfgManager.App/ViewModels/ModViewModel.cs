using System.Text.Json;
using MfgManager.App.Services;
using MfgManager.Core;
using MfgManager.Core.Mod;
using MfgManager.Core.Platform;
using MfgManager.Core.Settings;

namespace MfgManager.App.ViewModels;

/// <summary>Which MFGUnlocker.dll the app uses, and whether a newer one is out on GitHub.</summary>
public sealed class ModViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly UpdateChecker _updates;
    private readonly IDialogs _dialogs;
    private readonly StatusViewModel _status;
    private readonly string _modsFolder;        // where the mod DLL is kept and updates go
    private readonly string _downloadsFolder;   // only looked at for a newer MFGUnlocker.dll to copy into Mods

    private ModPackage? _mod;
    private ReleaseInfo? _availableUpdate;
    private ReleaseInfo? _latestRelease;   // what GitHub said at start-up

    public ModViewModel(AppSettings settings, Action saveSettings, UpdateChecker updates, IDialogs dialogs,
        StatusViewModel status, string modsFolder, string downloadsFolder)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _updates = updates;
        _dialogs = dialogs;
        _status = status;
        _modsFolder = modsFolder;
        _downloadsFolder = downloadsFolder;

        DownloadCommand = new RelayCommand(DownloadAsync, () => AvailableUpdate is not null).WatchEnabledOn(this);
        OpenReleasesCommand = new RelayCommand(OpenReleases);
        ChooseCommand = new RelayCommand(ChooseAsync);
    }

    /// <summary>Raised when a different DLL is now in use, so every game's status gets checked against it.</summary>
    public event Action? ModChanged;

    /// <summary>The MFGUnlocker.dll that gets installed.</summary>
    public ModPackage? Mod
    {
        get => _mod;
        internal set
        {
            if (!SetProperty(ref _mod, value)) return;
            OnPropertyChanged(nameof(VersionText));
            OnPropertyChanged(nameof(PathText));
            OnPropertyChanged(nameof(Missing));
            OnPropertyChanged(nameof(UpdateText));
            ModChanged?.Invoke();
        }
    }

    public string VersionText => Mod is null ? "MFGUnlocker.dll not found" : $"MFG Unlocker v{Mod.Version.ToString(3)}";
    public string PathText => Mod?.DllPath ?? "";
    public bool Missing => Mod is null;

    /// <summary>A newer release on GitHub (or the latest one, if you have none yet). Null = nothing to download.</summary>
    public ReleaseInfo? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            if (!SetProperty(ref _availableUpdate, value)) return;
            OnPropertyChanged(nameof(UpdateText));
            OnPropertyChanged(nameof(ShowUpdateBanner));
        }
    }

    public bool ShowUpdateBanner => AvailableUpdate is not null;

    public string UpdateText => AvailableUpdate is null ? "" :
        Mod is null
            ? $"MFG Unlocker v{AvailableUpdate.Version.ToString(3)} can be downloaded from GitHub."
            : $"MFG Unlocker v{AvailableUpdate.Version.ToString(3)} is out (you have v{Mod.Version.ToString(3)}).";

    public RelayCommand DownloadCommand { get; }
    public RelayCommand OpenReleasesCommand { get; }
    public RelayCommand ChooseCommand { get; }

    public async Task FindAsync()
    {
        // A picked DLL is only trusted inside the Mods folder. settings.json can be changed by any program;
        // once the app is installed in Program Files, the Mods folder can only be changed by admins.
        if (_settings.ModDllPath is { } picked && PathUtil.IsInside(picked, _modsFolder) && File.Exists(picked) && ModPackage.ReadVersion(picked) is { } version)
        {
            Mod = new ModPackage(picked, version);
            return;
        }

        var (inMods, newer) = ModPackage.FindCopies(_modsFolder, _downloadsFolder);
        if (newer is not null && await _dialogs.ConfirmAsync(
                $"There's a newer MFG Unlocker (v{newer.Version.ToString(3)}) in your Downloads folder:\n{newer.DllPath}\n\n" +
                $"Only use it if you downloaded it yourself from {UpdateChecker.ReleasesPage}\n\nCopy it into MFG Manager and use it?", warning: true))
        {
            try
            {
                Mod = ModPackage.Import(newer, _modsFolder);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await _dialogs.ShowAsync(ex.Message, error: true);   // carry on with the copy in Mods, so the games still get scanned
            }
        }
        Mod = inMods;
    }

    /// <summary>Runs once at start-up. This is the only time GitHub is asked about new versions; after that the answer is reused.</summary>
    public async Task CheckGitHubOnceAsync()
    {
        try
        {
            _latestRelease = await _updates.GetLatestAsync();
        }
        // The last two come from an answer that's valid JSON but not what GitHub normally sends.
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            _status.Text += "  (Couldn't check GitHub for mod updates.)";
        }
        CompareWithLatestRelease();
    }

    /// <summary>Shows the update banner if the release GitHub reported at start-up is newer than your copy. No internet needed.</summary>
    private void CompareWithLatestRelease() =>
        AvailableUpdate = _latestRelease is not null && _latestRelease.Version > (Mod?.Version ?? new Version(0, 0, 0)) ? _latestRelease : null;

    private async Task DownloadAsync()
    {
        if (AvailableUpdate is not { } release) return;
        _status.IsBusy = true;
        try
        {
            var progress = new Progress<double>(done => _status.Text = $"Downloading {release.ZipName}... {done:P0}");
            Mod = await _updates.DownloadAsync(release, _modsFolder, progress);
            _settings.ModDllPath = null;   // use the newest download from now on
            _saveSettings();
            AvailableUpdate = null;
            _status.Text = $"Downloaded and checked MFG Unlocker v{Mod!.Version.ToString(3)}. Games with an older copy now show \"Update\".";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowAsync("Download failed: " + ex.Message, error: true);
            _status.Text = "Download failed.";
        }
        finally
        {
            _status.IsBusy = false;
        }
    }

    private async Task ChooseAsync()
    {
        var path = await _dialogs.PickFileAsync("Pick MFGUnlocker.dll", "MFG Unlocker", "*.dll", _downloadsFolder);
        if (path is null) return;

        if (ModPackage.ReadVersion(path) is not { } version)
        {
            await _dialogs.ShowAsync("That file isn't MFG Unlocker.", error: true);
            return;
        }
        try
        {
            Mod = ModPackage.Import(new ModPackage(path, version), _modsFolder);   // keep a copy in the Mods folder
            _settings.ModDllPath = Mod.DllPath;
            _saveSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowAsync(ex.Message, error: true);
        }
        CompareWithLatestRelease();
    }

    private void OpenReleases() => DesktopShell.OpenWebPage(UpdateChecker.IsHttps(AvailableUpdate?.PageUrl) ?? UpdateChecker.ReleasesPage);
}
