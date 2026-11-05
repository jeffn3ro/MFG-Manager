namespace MfgManager.Core.Mod;

public sealed record ModStatus(ModState State, string? DllName = null, Version? InstalledVersion = null);
