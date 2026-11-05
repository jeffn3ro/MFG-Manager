namespace MfgManager.Core.Mod;

/// <summary>A release of the mod on GitHub.</summary>
/// <param name="Sha256">The zip's SHA-256 fingerprint as published by GitHub. Used to check the download.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string? ZipName, string? ZipUrl, string? Sha256, string? ShaSumsUrl);
