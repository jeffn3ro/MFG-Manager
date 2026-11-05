namespace MfgManager.Core.Scanning.Launchers;

/// <summary>One entry from Windows' "Apps &amp; features" list (the Uninstall keys in the registry).</summary>
public sealed record UninstallEntry(string? DisplayName, string? Publisher, string? InstallLocation, string? UninstallString);
