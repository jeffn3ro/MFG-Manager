namespace MfgManager.Core.Store;

/// <summary>DRM info from a game's Steam store page.</summary>
/// <param name="Notice">The store's own text, e.g. "Denuvo Anti-tamper. 5 a day machine activation limit".</param>
/// <param name="Tags">Short names for the filters, e.g. "Denuvo", "Ubisoft Connect".</param>
public sealed record StoreDrm(string? Notice, IReadOnlyList<string> Tags);
