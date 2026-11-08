using System.Text.Json;
using MfgManager.Core.Platform;

namespace MfgManager.Core.Settings;

/// <summary>Loads and saves <see cref="AppSettings"/> to a JSON file.</summary>
public sealed class SettingsStore(string filePath)
{
    public AppSettings Load()
    {
        if (!File.Exists(filePath)) return new AppSettings();
        try
        {
            return Cleaned(JsonSerializer.Deserialize(File.ReadAllText(filePath), SettingsJson.Default.AppSettings) ?? new AppSettings());
        }
        catch (JsonException)
        {
            // A broken file shouldn't stop the app.
            return new AppSettings();
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
        foreach ((string? folder, string? exe) in settings.ExeOverrides ?? [])
            if (Usable(folder) && Usable(exe)) exeOverrides[folder] = exe;
        settings.ExeOverrides = exeOverrides;

        settings.ManualGames = [.. (settings.ManualGames ?? []).Where(Usable)];
        settings.Installs = [.. (settings.Installs ?? []).Where(install => install is not null && Usable(install.Folder) && Usable(install.DllName))];
        if (!Usable(settings.ModDllPath)) settings.ModDllPath = null;
        if (!Enum.IsDefined(settings.Theme)) settings.Theme = ThemeChoice.System;
        return settings;
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureFolder(Path.GetDirectoryName(filePath)!);
        // Write to a temp file first so a crash mid-write can't leave a half-written settings file.
        string temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings));
        File.Move(temp, filePath, overwrite: true);
    }
}
