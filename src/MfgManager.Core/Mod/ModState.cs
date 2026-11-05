namespace MfgManager.Core.Mod;

public enum ModState
{
    NotInstalled,
    Installed,
    /// <summary>Installed, but an older version than the one you have downloaded.</summary>
    Outdated,
    /// <summary>We installed it, but the file is gone (e.g. the game verified/repaired its files).</summary>
    Missing,
    /// <summary>We installed it, but something else (game update, another mod) now uses that file name.</summary>
    Replaced,
}
