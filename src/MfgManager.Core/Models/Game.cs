namespace MfgManager.Core.Models;

/// <summary>Where a game was installed from.</summary>
public enum Launcher { Steam, Ubisoft, Epic, Gog, Xbox, BattleNet, Ea, Rockstar, Manual }

/// <summary>
/// One installed game. The scanners create it (name, launcher, folder) and
/// <see cref="Scanning.GameInspector"/> fills in the rest by looking at its files.
/// </summary>
public sealed class Game
{
    public required string Name { get; init; }
    public required Launcher Launcher { get; init; }

    /// <summary>The game's install folder.</summary>
    public required string Folder { get; init; }

    /// <summary>An .exe the launcher says it starts, if it told us.</summary>
    public string? ExeHint { get; init; }

    /// <summary>Steam's id for the game. Used for cover art and DRM info.</summary>
    public int? SteamAppId { get; set; }

    // ---- filled in by GameInspector ----

    /// <summary>The real game .exe. The mod goes in the same folder.</summary>
    public string? Exe { get; set; }

    /// <summary>True when the game ships NVIDIA DLSS Frame Generation.</summary>
    public bool HasFrameGen { get; set; }

    /// <summary>Name of the anti-cheat found in the game folder, or null.</summary>
    public string? AntiCheat { get; set; }

    /// <summary>DRM / online services the game uses, e.g. "Steamworks", "Denuvo".</summary>
    public List<string> Drm { get; } = [];

    public string? ExeFolder => Exe is null ? null : Path.GetDirectoryName(Exe);
}
