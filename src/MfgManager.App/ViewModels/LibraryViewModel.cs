using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using MfgManager.App.Services;
using MfgManager.Core;
using MfgManager.Core.Collections;
using MfgManager.Core.Mod;
using MfgManager.Core.Scanning;
using MfgManager.Core.Scanning.Launchers;
using MfgManager.Core.Settings;
using MfgManager.Core.Store;

namespace MfgManager.App.ViewModels;

/// <summary>The games: finding them, filtering and grouping them, and which one is selected.</summary>
public sealed class LibraryViewModel : ObservableObject
{
    public const string AllLaunchers = "All launchers";
    public const string AnyDrm = "Any DRM";
    public const string GroupByLauncher = "Launcher";
    public const string GroupByDrm = "DRM";
    public const string NoGrouping = "No grouping";

    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly GameScanner _scanner;
    private readonly ModInstaller _installer;
    private readonly SteamStore _store;
    private readonly IDialogs _dialogs;
    private readonly StatusViewModel _status;

    private GameViewModel? _selectedGame;
    private string _searchText = "";
    private string _launcherFilter = AllLaunchers;
    private string _drmFilter = AnyDrm;
    private string _groupBy = GroupByLauncher;
    private bool _onlyFrameGen = true;
    private int _statusVersion;

    public LibraryViewModel(AppSettings settings, Action saveSettings, GameScanner scanner, ModInstaller installer,
        SteamStore store, IDialogs dialogs, StatusViewModel status)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        _scanner = scanner;
        _installer = installer;
        _store = store;
        _dialogs = dialogs;
        _status = status;

        View = new GroupedView<GameViewModel>(Games)
        {
            Filter = game => Passes(game),
            Sort = Comparer<GameViewModel>.Create((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)),
        };
        ApplyGrouping();
        View.PropertyChanged += OnViewChanged;
        _status.PropertyChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyMessage));

        RescanCommand = new RelayCommand(() => ScanAsync(Mod), () => !_status.IsBusy).WatchEnabledOn(_status);
        AddGameCommand = new RelayCommand(AddGameAsync, () => !_status.IsBusy).WatchEnabledOn(_status);
    }

    // ---------------- what the window shows ----------------

    public ObservableCollection<GameViewModel> Games { get; } = [];

    /// <summary>The cards on screen: Games, but filtered, sorted and grouped.</summary>
    public GroupedView<GameViewModel> View { get; }

    public ObservableCollection<FilterOption> LauncherOptions { get; } = [new(AllLaunchers)];
    public ObservableCollection<FilterOption> DrmOptions { get; } = [new(AnyDrm)];
    public IReadOnlyList<string> GroupByOptions { get; } = [GroupByLauncher, GroupByDrm, NoGrouping];

    public bool ShowEmptyMessage => View.IsEmpty && !_status.IsBusy;

    public RelayCommand RescanCommand { get; }
    public RelayCommand AddGameCommand { get; }

    /// <summary>Changes whenever a game's mod status was re-read, so buttons can check themselves again.</summary>
    public int StatusVersion
    {
        get => _statusVersion;
        private set => SetProperty(ref _statusVersion, value);
    }

    /// <summary>The mod that's currently in use. Kept here so a rescan can compare each game against it.</summary>
    public ModPackage? Mod { get; set; }

    public GameViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            var previous = _selectedGame;
            if (!SetProperty(ref _selectedGame, value)) return;
            if (previous is not null) previous.IsSelected = false;
            if (value is not null) value.IsSelected = true;
        }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? "")) FiltersChanged(); }
    }

    public string LauncherFilter
    {
        get => _launcherFilter;
        set
        {
            if (!SetProperty(ref _launcherFilter, value ?? AllLaunchers)) return;
            OnPropertyChanged(nameof(SelectedLauncherOption));
            FiltersChanged();
        }
    }

    public string DrmFilter
    {
        get => _drmFilter;
        set
        {
            if (!SetProperty(ref _drmFilter, value ?? AnyDrm)) return;
            OnPropertyChanged(nameof(SelectedDrmOption));
            FiltersChanged();
        }
    }

    // The sidebar lists work with options; a list sends null when its chosen entry disappears, which means "show all".
    public FilterOption? SelectedLauncherOption
    {
        get => LauncherOptions.FirstOrDefault(option => option.Name == LauncherFilter);
        set => LauncherFilter = value?.Name ?? AllLaunchers;
    }

    public FilterOption? SelectedDrmOption
    {
        get => DrmOptions.FirstOrDefault(option => option.Name == DrmFilter);
        set => DrmFilter = value?.Name ?? AnyDrm;
    }

    public string GroupBy
    {
        get => _groupBy;
        set { if (value is not null && SetProperty(ref _groupBy, value)) ApplyGrouping(); }
    }

    public bool OnlyFrameGen
    {
        get => _onlyFrameGen;
        set { if (SetProperty(ref _onlyFrameGen, value)) FiltersChanged(); }
    }

    /// <summary>"1 game" or "3 games".</summary>
    public static string CountOfGames(int count) => count == 1 ? "1 game" : $"{count} games";

    // ---------------- scanning ----------------

    public async Task ScanAsync(ModPackage? mod)
    {
        Mod = mod;
        _status.IsBusy = true;
        try
        {
            var progress = new Progress<string>(text => _status.Text = text);
            var games = await Task.Run(() => _scanner.Scan(progress));   // Task.Run keeps the window responsive

            var selectedFolder = SelectedGame?.Folder;
            using (View.DeferRefresh())
            {
                Games.Clear();
                foreach (var game in games) Games.Add(new GameViewModel(game, select => SelectedGame = select));
            }
            RefreshStatuses();
            RefreshFilterOptions();
            SelectedGame = Games.FirstOrDefault(g => g.Folder == selectedFolder) ?? View.Items.FirstOrDefault();

            _status.Text = $"Found {Games.Count} games, {Games.Count(g => g.HasFrameGen)} with DLSS Frame Generation.";
        }
        finally
        {
            _status.IsBusy = false;
        }
        await LoadStoreInfoAsync(Games);
    }

    /// <summary>Cover art and store DRM info for these games. Frame-gen games first, since those are the ones you see.</summary>
    private async Task LoadStoreInfoAsync(IEnumerable<GameViewModel> games)
    {
        var steamFolder = SteamLibrary.FindSteamFolder() is { } folder ? PathUtil.Normalize(folder) : null;
        foreach (var game in games.OrderByDescending(g => g.HasFrameGen).ToList())
        {
            try
            {
                game.CoverPath = await _store.GetCoverAsync(game.Game, steamFolder);
                game.StoreDrm = await _store.GetDrmAsync(game.Game);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
            {
                // No internet or the store is down: the card just keeps its placeholder.
            }
        }
        try
        {
            await _store.SaveCacheAsync();   // one write for the whole batch (none if nothing was new)
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved: the next start just looks those games up again.
        }
        RefreshFilterOptions();
        View.Refresh();
    }

    private async Task AddGameAsync()
    {
        var exe = await _dialogs.PickFileAsync("Pick the game's .exe", "Programs", "*.exe");
        if (exe is null) return;

        var game = GameScanner.ManualGame(exe);
        if (!_settings.ManualGames.Contains(exe, PathUtil.Comparer)) _settings.ManualGames.Add(exe);
        _settings.ExeOverrides[game.Folder] = exe;
        _saveSettings();

        // Only this game is checked; the rest of the list is still up to date, so no full rescan.
        // If the scan already found this folder (e.g. a Steam game), that card is updated instead of adding a second one.
        var card = Games.FirstOrDefault(g => PathUtil.SameFolder(g.Folder, game.Folder));
        _status.IsBusy = true;
        try
        {
            await Task.Run(() => GameInspector.Inspect(card?.Game ?? game, exe));
        }
        finally
        {
            _status.IsBusy = false;
        }
        if (card is null)
        {
            card = new GameViewModel(game, select => SelectedGame = select);
            Games.Add(card);
        }

        RefreshStatuses();
        RefreshFilterOptions();
        if (!card.HasFrameGen) OnlyFrameGen = false;   // otherwise the filter would hide it
        SelectedGame = card;
        _status.Text = $"Added {card.Name}.";
        await LoadStoreInfoAsync([card]);
    }

    // ---------------- keeping the screen up to date ----------------

    /// <summary>Re-reads every game's mod status. Call it after anything that changes game folders or the mod.</summary>
    public void RefreshStatuses()
    {
        foreach (var game in Games) game.Refresh(_installer, Mod);
        View.Refresh();
        StatusVersion++;
    }

    public void RefreshFilterOptions()
    {
        SyncOptions(LauncherOptions, AllLaunchers, Games.Select(g => g.Launcher));
        SyncOptions(DrmOptions, AnyDrm, Games.SelectMany(g => g.DrmGroups));

        // A filter whose entry is gone (e.g. the last game with that DRM was removed) goes back to showing everything.
        if (LauncherOptions.All(option => option.Name != LauncherFilter)) LauncherFilter = AllLaunchers;
        if (DrmOptions.All(option => option.Name != DrmFilter)) DrmFilter = AnyDrm;

        UpdateOptionCounts();
        OnPropertyChanged(nameof(SelectedLauncherOption));
        OnPropertyChanged(nameof(SelectedDrmOption));
    }

    private static void SyncOptions(ObservableCollection<FilterOption> options, string first, IEnumerable<string> names)
    {
        var existing = options.ToDictionary(option => option.Name);
        var wanted = names.Distinct().Order().Prepend(first)
            .Select(name => existing.GetValueOrDefault(name) ?? new FilterOption(name))
            .ToList();
        ListSync.Sync(options, wanted);
    }

    /// <summary>Each number shows how many games you'd see if you picked that line, with your other filters still on.</summary>
    private void UpdateOptionCounts()
    {
        foreach (var option in LauncherOptions)
            option.Count = Games.Count(g => Passes(g, launcher: option.Name));
        foreach (var option in DrmOptions)
            option.Count = Games.Count(g => Passes(g, drm: option.Name));
    }

    private void FiltersChanged()
    {
        View.Refresh();
        UpdateOptionCounts();
    }

    private bool Passes(GameViewModel game, string? launcher = null, string? drm = null)
    {
        launcher ??= LauncherFilter;
        drm ??= DrmFilter;
        return (!OnlyFrameGen || game.HasFrameGen || game.CanRemove)
            && (launcher == AllLaunchers || game.Launcher == launcher)
            && (drm == AnyDrm || game.DrmGroups.Contains(drm))
            && (SearchText.Length == 0 || game.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyGrouping() =>
        // Grouping by DRM puts a game under each DRM it uses.
        View.GroupBy = GroupBy switch
        {
            GroupByLauncher => game => [game.Launcher],
            GroupByDrm => game => game.DrmGroups,
            _ => null,
        };

    private void OnViewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(View.Items)) return;
        OnPropertyChanged(nameof(ShowEmptyMessage));

        // If the filters hid the selected game, move on to the first one you can see.
        if (SelectedGame is null || !View.Items.Contains(SelectedGame)) SelectedGame = View.Items.FirstOrDefault();
    }
}
