using MfgManager.Core.Models;
using MfgManager.Core.Settings;

namespace MfgManager.Core.Mod;

/// <summary>
/// Copies the mod next to a game's .exe and takes it out again.
///
/// How the mod works: games load some Windows DLLs (version.dll, dxgi.dll, ...) from their own
/// folder first. The mod pretends to be one of those, so the game loads it automatically.
/// Rules from the mod's README that this class follows:
///   - Never overwrite a DLL that belongs to the game or another mod. Use a free name instead.
///   - Only one copy per game.
/// </summary>
/// <param name="saveSettings">Called after every change so the install list is never lost.</param>
/// <param name="removedFilesFolder">Where the mod's settings/logs are moved when you remove them.</param>
/// <param name="readModVersion">How to tell if a DLL is the mod. Tests swap this for a fake.</param>
public sealed class ModInstaller(AppSettings settings, Action saveSettings, string removedFilesFolder, Func<string, Version?>? readModVersion = null)
{
    /// <summary>File names the mod supports (from its README), in the order "Auto" tries them.</summary>
    public static readonly IReadOnlyList<string> SupportedNames =
    [
        "version.dll", "winmm.dll", "dxgi.dll", "dinput8.dll", "winhttp.dll", "wininet.dll", "dsound.dll",
        "d3d11.dll", "d3d12.dll", "d3d10.dll", "d3d9.dll",
        "xinput1_4.dll", "xinput1_3.dll", "xinput1_2.dll", "xinput1_1.dll", "xinput9_1_0.dll", "xinputuap.dll",
    ];

    /// <summary>Settings, status and log files the mod writes next to the game.</summary>
    private static readonly string[] ModDataPatterns = ["RTXMFG*", "RTX40MFG*", "RTX30MFG*"];

    private readonly AppSettings _settings = settings;
    private readonly Action _saveSettings = saveSettings;
    private readonly string _removedFilesFolder = removedFilesFolder;
    private readonly Func<string, Version?> _readModVersion = readModVersion ?? ModPackage.ReadVersion;

    public ModStatus GetStatus(Game game, ModPackage? currentMod)
    {
        if (game.ExeFolder is not { } folder) return new ModStatus(ModState.NotInstalled);

        string? name = FindInstalledName(folder);
        if (name is null) return new ModStatus(ModState.NotInstalled);

        string path = Path.Combine(folder, name);
        if (!File.Exists(path)) return new ModStatus(ModState.Missing, name);

        Version? version = _readModVersion(path);
        if (version is null) return new ModStatus(ModState.Replaced, name);
        if (currentMod is not null && version < currentMod.Version) return new ModStatus(ModState.Outdated, name, version);
        return new ModStatus(ModState.Installed, name, version);
    }

    /// <summary>Supported names that aren't already used by a file in the game folder.</summary>
    public static IReadOnlyList<string> GetFreeNames(Game game)
    {
        if (game.ExeFolder is not { } folder) return [];
        HashSet<string> taken = PathUtil.FileNamesIn(folder);
        return [.. SupportedNames.Where(name => !taken.Contains(name))];
    }

    /// <summary>Installs the mod, or updates it in place if it's already there.</summary>
    /// <param name="preferredName">A name from <see cref="SupportedNames"/>, or null for "Auto".</param>
    /// <returns>The file name the mod was installed as.</returns>
    public string Install(Game game, ModPackage mod, string? preferredName = null)
    {
        string folder = game.ExeFolder ?? throw new InvalidOperationException("Couldn't find this game's .exe, so there's nowhere to put the mod.");
        if (IsInWindowsFolder(folder))
            throw new InvalidOperationException("That folder belongs to Windows, so the mod won't be put there.");
        ModStatus status = GetStatus(game, mod);

        string name;
        if (status.State == ModState.Replaced)
        {
            throw new InvalidOperationException($"{status.DllName} in this game now belongs to something else. Remove the mod first, then install it again.");
        }
        else if (status.DllName is not null)
        {
            // Already installed (or missing) -> reuse the same name.
            name = status.DllName;
        }
        else
        {
            name = preferredName ?? (GetFreeNames(game) is [var free, ..] ? free : null)
                ?? throw new InvalidOperationException("Every file name the mod supports is already used in this game folder.");
            if (!SupportedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"{name} isn't a file name the mod supports.", nameof(preferredName));
            if (PathUtil.FileNamesIn(folder).Contains(name))
                throw new InvalidOperationException($"{name} already exists in the game folder and belongs to the game or another mod. Pick a different name.");
        }

        File.Copy(mod.DllPath, Path.Combine(folder, name), overwrite: true);

        ForgetRecord(folder);
        _settings.Installs.Add(new InstallRecord(folder, name, mod.Version.ToString(), DateTime.Now));
        _saveSettings();
        return name;
    }

    /// <summary>
    /// Takes the mod out of a game. It only ever deletes a file that really is the mod; if the file
    /// was replaced by something else, it's left alone and just forgotten.
    /// </summary>
    /// <param name="removeModData">Also move the mod's settings and logs out of the game folder.</param>
    /// <returns>True if a DLL was deleted.</returns>
    public bool Remove(Game game, bool removeModData = false)
    {
        if (game.ExeFolder is not { } folder) return false;

        bool deleted = false;
        if (FindInstalledName(folder) is { } name)
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path) && _readModVersion(path) is not null)
            {
                File.Delete(path);
                deleted = true;
            }
        }

        if (removeModData) MoveModDataAway(game, folder);

        ForgetRecord(folder);
        _saveSettings();
        return deleted;
    }

    /// <summary>The mod's own files in a game folder (settings, logs).</summary>
    public static IReadOnlyList<string> FindModDataFiles(string folder) =>
        [.. ModDataPatterns.SelectMany(pattern => Directory.EnumerateFiles(folder, pattern)).Distinct()];

    /// <summary>The name the mod is installed as in this folder: from our records, or by checking each supported name.</summary>
    private string? FindInstalledName(string folder)
    {
        // settings.json can be changed by any program, and this name decides where the app writes and deletes as admin.
        InstallRecord? record = _settings.Installs.FirstOrDefault(r => PathUtil.SameFolder(r.Folder, folder));
        if (record is not null && SupportedNames.Contains(record.DllName, StringComparer.OrdinalIgnoreCase)) return record.DllName;

        return SupportedNames.FirstOrDefault(name =>
        {
            string path = Path.Combine(folder, name);
            return File.Exists(path) && _readModVersion(path) is not null;
        });
    }

    /// <summary>Game folders come from files any program can change, so a "game" could point here. Linux has no such folder.</summary>
    private static bool IsInWindowsFolder(string folder)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return windows.Length > 0 && (PathUtil.SameFolder(folder, windows) || PathUtil.IsInside(folder, windows));
    }

    private void ForgetRecord(string folder) =>
        _settings.Installs.RemoveAll(r => PathUtil.SameFolder(r.Folder, folder));

    /// <summary>Moves (not deletes) the mod's settings/logs, so nothing is lost if you change your mind.</summary>
    private void MoveModDataAway(Game game, string folder)
    {
        IReadOnlyList<string> files = FindModDataFiles(folder);
        if (files.Count == 0) return;

        string safeName = string.Concat(game.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string target = Path.Combine(_removedFilesFolder, $"{safeName} {DateTime.Now:yyyy-MM-dd HH-mm-ss}");
        Directory.CreateDirectory(target);
        foreach (string file in files) File.Move(file, Path.Combine(target, Path.GetFileName(file)));
    }
}
