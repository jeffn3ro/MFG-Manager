using System.Text.Json;
using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

public static class EpicLibrary
{
    public static string DefaultManifestsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    /// <summary>Epic keeps one JSON ".item" file per installed game.</summary>
    public static IEnumerable<Game> FindGames(string manifestsFolder)
    {
        if (!Directory.Exists(manifestsFolder)) yield break;

        foreach (var file in Directory.EnumerateFiles(manifestsFolder, "*.item"))
        {
            EpicManifest? manifest;
            try { manifest = JsonSerializer.Deserialize<EpicManifest>(File.ReadAllText(file)); }
            catch (JsonException) { continue; }

            if (manifest?.InstallLocation is not { } location || !Directory.Exists(location)) continue;

            yield return new Game
            {
                Name = manifest.DisplayName ?? Path.GetFileName(location),
                Launcher = Launcher.Epic,
                Folder = PathUtil.Normalize(location),
                ExeHint = manifest.LaunchExecutable is { Length: > 0 } exe ? Path.Combine(location, exe) : null,
            };
        }
    }

    private sealed record EpicManifest(string? DisplayName, string? InstallLocation, string? LaunchExecutable);
}
