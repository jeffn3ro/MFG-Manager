using System.Diagnostics;

namespace MfgManager.Core.Platform;

/// <summary>Checks whether a game is running, because its files can't be changed safely while it is.</summary>
public static class GameProcesses
{
    /// <summary>On Linux, the system only keeps the first 15 letters of a program's name.</summary>
    private const int LinuxNameLimit = 15;

    public static bool IsRunning(string exePath)
    {
        var fileName = Path.GetFileName(exePath);
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(process => Matches(SafeName(process), fileName));
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    /// <summary>
    /// Windows names a process "Game". Under Proton on Linux it's "Game.exe", cut off after 15 letters.
    /// </summary>
    public static bool Matches(string? processName, string exeFileName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var withoutExtension = Path.GetFileNameWithoutExtension(exeFileName);

        return processName.Equals(withoutExtension, StringComparison.OrdinalIgnoreCase)
            || processName.Equals(exeFileName, StringComparison.OrdinalIgnoreCase)
            || (processName.Length == LinuxNameLimit && exeFileName.StartsWith(processName, StringComparison.OrdinalIgnoreCase));
    }

    private static string? SafeName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (InvalidOperationException)
        {
            return null;   // it ended while we were looking
        }
    }
}
