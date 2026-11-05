using System.Text.Json;
using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

/// <summary>
/// Heroic Games Launcher, the usual way to play Epic and GOG games on Linux. It's a normal install or a Flatpak,
/// and it keeps Epic's and GOG's installed games in two JSON files.
/// </summary>
public static class HeroicLibrary
{
    /// <summary>Heroic's config folders: the normal one, then the Flatpak one. None on Windows, where those launchers have their own apps.</summary>
    public static IEnumerable<string> DefaultConfigFolders()
    {
        if (OperatingSystem.IsWindows()) return [];
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [Path.Combine(home, ".config/heroic"), Path.Combine(home, ".var/app/com.heroicgameslauncher.hgl/config/heroic")];
    }

    public static IEnumerable<Game> FindGames(IEnumerable<string> configFolders) =>
        configFolders.SelectMany(folder => EpicGames(folder).Concat(GogGames(folder)));

    /// <summary>legendaryConfig/legendary/installed.json: { "appName": { "title", "install_path", "executable" }, ... }</summary>
    private static List<Game> EpicGames(string configFolder)
    {
        var games = new List<Game>();
        using JsonDocument? doc = ReadJson(Path.Combine(configFolder, "legendaryConfig", "legendary", "installed.json"));
        if (doc?.RootElement.ValueKind != JsonValueKind.Object) return games;

        foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.Object) continue;
            string? folder = Text(entry.Value, "install_path");
            if (folder is null || !Directory.Exists(folder)) continue;

            string? executable = Text(entry.Value, "executable")?.Replace('\\', '/');
            games.Add(new Game
            {
                Name = Text(entry.Value, "title") ?? Path.GetFileName(folder),
                Launcher = Launcher.Epic,
                Folder = PathUtil.Normalize(folder),
                ExeHint = string.IsNullOrEmpty(executable) ? null : Path.Combine(folder, executable),
            });
        }
        return games;
    }

    /// <summary>gog_store/installed.json: { "installed": [ { "install_path", "is_dlc" }, ... ] }. It has no titles, so the folder name is used.</summary>
    private static List<Game> GogGames(string configFolder)
    {
        var games = new List<Game>();
        using JsonDocument? doc = ReadJson(Path.Combine(configFolder, "gog_store", "installed.json"));
        if (doc?.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("installed", out JsonElement installed)
            || installed.ValueKind != JsonValueKind.Array) return games;

        foreach (JsonElement entry in installed.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            if (entry.TryGetProperty("is_dlc", out JsonElement isDlc) && isDlc.ValueKind == JsonValueKind.True) continue;

            string? folder = Text(entry, "install_path");
            if (folder is null || !Directory.Exists(folder)) continue;

            string normalized = PathUtil.Normalize(folder);
            games.Add(new Game { Name = Path.GetFileName(normalized), Launcher = Launcher.Gog, Folder = normalized });
        }
        return games;
    }

    private static JsonDocument? ReadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonDocument.Parse(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
