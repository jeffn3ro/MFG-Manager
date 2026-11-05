using Microsoft.Win32;

namespace MfgManager.Core.Platform;

public static class KnownFolders
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Your Downloads folder, even if you moved it or your system uses another language.</summary>
    public static string Downloads => (OperatingSystem.IsWindows() ? WindowsDownloads() : LinuxDownloads()) ?? Path.Combine(Home, "Downloads");

    private static string? WindowsDownloads()
    {
        if (!OperatingSystem.IsWindows()) return null;
        string? moved = Registry.GetValue(
            """HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders""",
            "{374DE290-123F-4565-9164-39C4925E467B}", null) as string;
        if (string.IsNullOrEmpty(moved)) return null;
        string expanded = Environment.ExpandEnvironmentVariables(moved);
        return Directory.Exists(expanded) ? expanded : null;
    }

    private static string? LinuxDownloads()
    {
        string configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } set ? set : Path.Combine(Home, ".config");
        string userDirs = Path.Combine(configHome, "user-dirs.dirs");
        return File.Exists(userDirs) ? ParseUserDirs(File.ReadAllText(userDirs), Home) : null;
    }

    /// <summary>Reads the line XDG_DOWNLOAD_DIR="$HOME/Downloads" from Linux's user-dirs.dirs file.</summary>
    public static string? ParseUserDirs(string text, string home)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal)) continue;

            string value = trimmed["XDG_DOWNLOAD_DIR=".Length..].Trim('"');
            if (value.StartsWith("$HOME", StringComparison.Ordinal)) value = home + value["$HOME".Length..];
            return Path.IsPathRooted(value) ? value : null;
        }
        return null;
    }
}
