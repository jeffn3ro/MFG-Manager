using Microsoft.Win32;
using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

/// <summary>One game from GOG's registry list.</summary>
/// <param name="DependsOn">Set on DLC and add-ons (the id of the game they belong to). They share the game's folder.</param>
public sealed record GogEntry(string? Name, string? Path, string? Exe, string? DependsOn);

public static class GogLibrary
{
    public static IEnumerable<GogEntry> ReadEntries()
    {
        if (!OperatingSystem.IsWindows()) yield break;

        using var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (root is null) yield break;

        foreach (var id in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(id);
            if (key is null) continue;
            yield return new GogEntry(key.GetValue("gameName") as string, key.GetValue("path") as string,
                key.GetValue("exe") as string, key.GetValue("dependsOn") as string);
        }
    }

    /// <summary>
    /// Games from GOG's list. DLC is skipped: Cyberpunk 2077's Phantom Liberty and REDmod are listed
    /// with the game's own folder, and would otherwise show up instead of the game.
    /// </summary>
    public static IEnumerable<Game> FindGames(IEnumerable<GogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.DependsOn)) continue;
            if (entry.Path is not { } path || !Directory.Exists(path)) continue;
            yield return new Game
            {
                Name = entry.Name ?? System.IO.Path.GetFileName(path),
                Launcher = Launcher.Gog,
                Folder = PathUtil.Normalize(path),
                ExeHint = string.IsNullOrEmpty(entry.Exe) ? null : entry.Exe,
            };
        }
    }
}
