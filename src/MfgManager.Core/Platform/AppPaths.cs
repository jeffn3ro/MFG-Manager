namespace MfgManager.Core.Platform;

/// <summary>Where the app keeps its own files.</summary>
public static class AppPaths
{
    /// <summary>Settings, caches and removed mod files. %LOCALAPPDATA%\MFG Manager on Windows, ~/.local/share/MFG Manager on Linux.</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MFG Manager");

    /// <summary>Where the mod DLL is kept and updates are downloaded to.</summary>
    public static string ModsFolder => PickModsFolder(OperatingSystem.IsWindows(), AppContext.BaseDirectory, DataFolder);

    /// <summary>
    /// On Windows it's next to the app: in Program Files only admins can change it, which matters because the app runs as admin.
    /// On Linux the app is usually in a read-only folder and doesn't run as admin, so it's in your own data folder.
    /// </summary>
    public static string PickModsFolder(bool isWindows, string appFolder, string dataFolder) =>
        Path.Combine(isWindows ? appFolder : dataFolder, "Mods");

    /// <summary>Creates a folder. On Linux only you can read it, because other users have no reason to.</summary>
    public static void EnsureFolder(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(folder);
            return;
        }
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
