namespace MfgManager.Core.Settings;

/// <param name="Folder">The folder the DLL was copied into (next to the game .exe).</param>
/// <param name="DllName">The name it was given, e.g. "version.dll".</param>
public sealed record InstallRecord(string Folder, string DllName, string ModVersion, DateTime InstalledAt);
