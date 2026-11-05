using System.Text.Json;
using MfgManager.Core.Platform;

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

public enum ThemeChoice { System, Light, Dark }

/// <param name="Folder">The folder the DLL was copied into (next to the game .exe).</param>
/// <param name="DllName">The name it was given, e.g. "version.dll".</param>
public sealed record InstallRecord(string Folder, string DllName, string ModVersion, DateTime InstalledAt);

/// <summary>Loads and saves <see cref="AppSettings"/> to a JSON file.</summary>
public sealed class SettingsStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Load()
    {
        if (!File.Exists(filePath)) return new AppSettings();
        try
        {
            return Cleaned(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new AppSettings());
        }
        catch (JsonException)
        {
            return new AppSettings();   // a broken file shouldn't stop the app
        }
    }

    /// <summary>
    /// Valid JSON can still be missing things or hold nonsense, if the file was damaged or changed by hand.
    /// The rest of the app counts on the lists being there and on every path being a usable one.
    /// </summary>
    private static AppSettings Cleaned(AppSettings settings)
    {
        static bool Usable(string? path) => !string.IsNullOrWhiteSpace(path) && !path.Contains('\0');

        // JSON loses the "ignore case" rule on dictionaries, so this puts it back too.
        var exeOverrides = new Dictionary<string, string>(PathUtil.Comparer);
        foreach (var (folder, exe) in settings.ExeOverrides ?? new())
            if (Usable(folder) && Usable(exe)) exeOverrides[folder] = exe;
        settings.ExeOverrides = exeOverrides;

        settings.ManualGames = (settings.ManualGames ?? []).Where(Usable).ToList();
        settings.Installs = (settings.Installs ?? []).Where(install => install is not null && Usable(install.Folder) && Usable(install.DllName)).ToList();
        if (!Usable(settings.ModDllPath)) settings.ModDllPath = null;
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = ThemeChoice.System;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureFolder(Path.GetDirectoryName(filePath)!);
        // Write to a temp file first so a crash mid-write can't leave a half-written settings file.
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, filePath, overwrite: true);
    }
}
