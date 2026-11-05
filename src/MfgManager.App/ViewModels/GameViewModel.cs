using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Store;

namespace MfgManager.App.ViewModels;

/// <summary>One game card in the list, plus everything the details panel shows about it.</summary>
public sealed class GameViewModel : ObservableObject
{
    public const string AutoName = "Auto";
    public const string NoDrmFound = "No DRM found";

    private string? _coverPath;
    private StoreDrm? _storeDrm;
    private ModStatus _status = new(ModState.NotInstalled);
    private IReadOnlyList<string> _installNames = [AutoName];
    private string _selectedInstallName = AutoName;
    private bool _isSelected;

    public GameViewModel(Game game, Action<GameViewModel>? onSelected = null)
    {
        Game = game;
        SelectCommand = new RelayCommand(() => onSelected?.Invoke(this));
    }

    public Game Game { get; }

    /// <summary>Clicking the card.</summary>
    public RelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Name => Game.Name;
    public string Folder => Game.Folder;
    public bool HasFrameGen => Game.HasFrameGen;
    public string FrameGenText => HasFrameGen ? "Yes (DLSS Frame Generation found)" : "No";
    public string? AntiCheat => Game.AntiCheat;
    public bool HasAntiCheat => AntiCheat is not null;
    public string ExeText => Game.Exe ?? "No game .exe found";

    public string Launcher => Game.Launcher switch
    {
        Core.Models.Launcher.Gog => "GOG",
        Core.Models.Launcher.BattleNet => "Battle.net",
        Core.Models.Launcher.Ea => "EA app",
        Core.Models.Launcher.Manual => "Added by you",
        var other => other.ToString(),
    };

    /// <summary>Shown on the card while there's no cover art, e.g. "AC" for "Assassin's Creed".</summary>
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(word => char.IsLetterOrDigit(word[0])).Take(2).Select(word => char.ToUpperInvariant(word[0])));

    public string? CoverPath
    {
        get => _coverPath;
        set
        {
            if (!SetProperty(ref _coverPath, value)) return;
            OnPropertyChanged(nameof(HasNoCover));
        }
    }

    public bool HasNoCover => CoverPath is null;

    /// <summary>DRM info from the Steam store page (null until it has loaded).</summary>
    public StoreDrm? StoreDrm
    {
        get => _storeDrm;
        set
        {
            if (!SetProperty(ref _storeDrm, value)) return;
            OnPropertyChanged(nameof(DrmGroups));
            OnPropertyChanged(nameof(DrmNotice));
        }
    }

    /// <summary>All DRM this game uses: from its files plus its store page. Used for the DRM filter and grouping.</summary>
    public IReadOnlyList<string> DrmGroups
    {
        get
        {
            var all = Game.Drm.Concat(StoreDrm?.Tags ?? []).Distinct().Order().ToList();
            return all.Count > 0 ? all : [NoDrmFound];
        }
    }

    public string? DrmNotice => StoreDrm?.Notice;

    // ---------------- mod status ----------------

    public ModStatus Status => _status;
    public bool CanRemove => _status.State != ModState.NotInstalled;
    public bool CanChooseName => _status.DllName is null;

    public string StatusText => _status.State switch
    {
        ModState.Installed => $"Installed as {_status.DllName} (v{_status.InstalledVersion?.ToString(3)})",
        ModState.Outdated => $"v{_status.InstalledVersion?.ToString(3)} installed as {_status.DllName}. A newer version is ready.",
        ModState.Missing => $"{_status.DllName} is gone. The game may have repaired its files.",
        ModState.Replaced => $"{_status.DllName} now belongs to something else (game update or another mod).",
        _ => "Not installed",
    };

    /// <summary>Small label on the card: Installed / Update / Check. Null = no label.</summary>
    public string? Badge => _status.State switch
    {
        ModState.Installed => "Installed",
        ModState.Outdated => "Update",
        ModState.Missing or ModState.Replaced => "Check",
        _ => null,
    };

    // Which colour the label gets.
    public bool BadgeIsInstalled => _status.State == ModState.Installed;
    public bool BadgeIsUpdate => _status.State == ModState.Outdated;
    public bool BadgeIsCheck => _status.State is ModState.Missing or ModState.Replaced;

    /// <summary>
    /// What to put in Steam's launch options on Linux. Proton only loads a DLL from the game's folder when told to,
    /// and it needs the mod's name without ".dll". Null on Windows, or when the mod isn't in this game.
    /// </summary>
    public string? LinuxLaunchOption =>
        OperatingSystem.IsWindows() || !CanRemove ? null : LaunchOptionFor(_status.DllName);

    public static string? LaunchOptionFor(string? dllName) =>
        dllName is null ? null : $"WINEDLLOVERRIDES=\"{Path.GetFileNameWithoutExtension(dllName)}=n,b\" %command%";

    /// <summary>The file names you can pick under "Install as".</summary>
    public IReadOnlyList<string> InstallNames
    {
        get => _installNames;
        private set => SetProperty(ref _installNames, value);
    }

    public string SelectedInstallName
    {
        get => _selectedInstallName;
        set
        {
            // A dropdown sends null while its list is being replaced; keep the current choice.
            if (value is not null) SetProperty(ref _selectedInstallName, value);
        }
    }

    /// <summary>
    /// Re-reads the mod status from disk and tells the window to redraw the card
    /// (including what the game was found to have, which changes when it's checked again).
    /// </summary>
    public void Refresh(ModInstaller installer, ModPackage? mod)
    {
        _status = installer.GetStatus(Game, mod);
        InstallNames = _status.DllName is { } current ? [current] : [AutoName, .. installer.GetFreeNames(Game)];
        SelectedInstallName = InstallNames[0];

        foreach (var name in new[] { nameof(Status), nameof(CanRemove), nameof(CanChooseName), nameof(StatusText), nameof(Badge),
                     nameof(BadgeIsInstalled), nameof(BadgeIsUpdate), nameof(BadgeIsCheck), nameof(LinuxLaunchOption), nameof(ExeText),
                     nameof(HasFrameGen), nameof(FrameGenText), nameof(AntiCheat), nameof(HasAntiCheat), nameof(DrmGroups) })
            OnPropertyChanged(name);
    }
}
