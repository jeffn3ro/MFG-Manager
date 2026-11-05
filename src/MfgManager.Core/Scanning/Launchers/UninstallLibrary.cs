using System.Text.RegularExpressions;
using Microsoft.Win32;
using MfgManager.Core.Models;

namespace MfgManager.Core.Scanning.Launchers;

/// <summary>One entry from Windows' "Apps &amp; features" list (the Uninstall keys in the registry).</summary>
public sealed record UninstallEntry(string? DisplayName, string? Publisher, string? InstallLocation, string? UninstallString);

/// <summary>
/// Battle.net, the EA app and the Rockstar Games Launcher don't keep an easy-to-read game list,
/// but every game they install adds itself to "Apps &amp; features". We read that list and recognise
/// each launcher by the way its games uninstall.
/// </summary>
public static class UninstallLibrary
{
    private static readonly string[] UninstallKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    public static IEnumerable<UninstallEntry> ReadEntries()
    {
        if (!OperatingSystem.IsWindows()) return [];

        var entries = new List<UninstallEntry>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var keyPath in UninstallKeys)
        {
            using var root = hive.OpenSubKey(keyPath);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(name);
                if (key is null) continue;
                entries.Add(new UninstallEntry(
                    key.GetValue("DisplayName") as string, key.GetValue("Publisher") as string,
                    key.GetValue("InstallLocation") as string, key.GetValue("UninstallString") as string));
            }
        }
        return entries;
    }

    /// <summary>Which launcher installed this entry, or null when it isn't a game from one of these three.</summary>
    public static Launcher? LauncherOf(UninstallEntry entry)
    {
        var uninstall = entry.UninstallString ?? "";
        bool Has(string text) => uninstall.Contains(text, StringComparison.OrdinalIgnoreCase);

        // Battle.net games: "...\Battle.net\Agent\Blizzard Uninstaller.exe" --uid=fenris  (uid=battle.net is the launcher itself)
        if (Has("Blizzard Uninstaller") && !Has("--uid=battle.net")) return Launcher.BattleNet;
        // EA app games: "C:\Program Files\Common Files\EAInstaller\<game>\Cleanup.exe" uninstall_game
        if (Has(@"\EAInstaller\")) return Launcher.Ea;
        // Rockstar games: "<game folder>\uninstall.exe" -uninstall=gta5_gen9  (=launcher is the launcher itself)
        if (entry.Publisher == "Rockstar Games" && Has("-uninstall=") && !Has("-uninstall=launcher")) return Launcher.Rockstar;
        return null;
    }

    public static IEnumerable<Game> FindGames(IEnumerable<UninstallEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (LauncherOf(entry) is not { } launcher) continue;
            if (string.IsNullOrWhiteSpace(entry.InstallLocation) || !Directory.Exists(entry.InstallLocation)) continue;

            var folder = PathUtil.Normalize(entry.InstallLocation);
            yield return new Game
            {
                Name = CleanName(entry.DisplayName) is { Length: > 0 } name ? name : Path.GetFileName(folder),
                Launcher = launcher,
                Folder = folder,
            };
        }
    }

    /// <summary>"Dead Space™" -> "Dead Space", so the Steam store search finds it.</summary>
    public static string CleanName(string? name) =>
        Regex.Replace(name ?? "", @"[™®©]|\((tm|r)\)", "", RegexOptions.IgnoreCase).Trim();
}
