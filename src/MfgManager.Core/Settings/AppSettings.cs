namespace MfgManager.Core.Settings;

/// <summary>Everything the app remembers between runs. Saved as JSON.</summary>
public sealed class AppSettings
{
    /// <summary>Light, dark, or follow the system. See <see cref="ThemeChoice"/>.</summary>
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;

    /// <summary>MFGUnlocker.dll the user picked by hand. Null = use the newest one in Downloads.</summary>
    public string? ModDllPath { get; set; }

    /// <summary>Games the user added by picking their .exe.</summary>
    public List<string> ManualGames { get; set; } = [];

    /// <summary>Game folder -> the .exe the user says is the real game.</summary>
    public Dictionary<string, string> ExeOverrides { get; set; } = new(PathUtil.Comparer);

    /// <summary>Every place this app put the mod, so it can always take it out again.</summary>
    public List<InstallRecord> Installs { get; set; } = [];
}
