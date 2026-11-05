namespace MfgManager.Core.Scanning.Launchers;

/// <summary>One game from GOG's registry list.</summary>
/// <param name="DependsOn">Set on DLC and add-ons (the id of the game they belong to). They share the game's folder.</param>
public sealed record GogEntry(string? Name, string? Path, string? Exe, string? DependsOn);
