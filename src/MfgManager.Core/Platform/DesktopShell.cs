using System.ComponentModel;
using System.Diagnostics;

namespace MfgManager.Core.Platform;

/// <summary>Opens web pages and folders with whatever the system normally uses.</summary>
public static class DesktopShell
{
    /// <summary>
    /// Opens a web page. On Windows this goes through Explorer instead of starting the browser directly:
    /// this app runs as admin, and a browser it started directly would run as admin too, which is unsafe.
    /// Returns false if the system couldn't open it.
    /// </summary>
    public static bool OpenWebPage(string url) => Start(WebPageCommand(url, OperatingSystem.IsWindows()));

    /// <summary>Shows a file in the system's file manager. Linux has no standard way to select it, so it opens the folder.</summary>
    public static bool RevealFile(string path) => Start(RevealCommand(path, OperatingSystem.IsWindows()));

    public static ProcessStartInfo WebPageCommand(string url, bool isWindows) =>
        Command(isWindows ? "explorer.exe" : "xdg-open", url);

    public static ProcessStartInfo RevealCommand(string path, bool isWindows)
    {
        if (isWindows) return new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{path}\"", UseShellExecute = false };
        return Command("xdg-open", Path.GetDirectoryName(path) ?? path);
    }

    /// <summary>The argument is passed on its own, never through a shell, so nothing in it can be run as a command.</summary>
    private static ProcessStartInfo Command(string program, string argument)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false };
        info.ArgumentList.Add(argument);
        return info;
    }

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
