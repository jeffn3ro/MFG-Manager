using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MfgManager.Core.Models;

namespace MfgManager.Core.Store;

/// <summary>
/// Gets cover art and DRM info from Steam's public store (no account or key needed).
/// It works for non-Steam games too, as long as the game is also sold on Steam:
/// we look it up by name. Everything is cached on disk so each game is only looked up once.
/// Lookups are kept in memory; call <see cref="SaveCacheAsync"/> once after a batch of games to write them.
/// </summary>
public sealed partial class SteamStore(HttpClient http, string cacheFolder) : IDisposable
{
    private static readonly string[] DrmNoticeKeys = ["drm_notice", "ext_user_account_notice"];

    private readonly HttpClient _http = http;
    private readonly string _artFolder = Path.Combine(cacheFolder, "art");
    private readonly string _cacheFile = Path.Combine(cacheFolder, "store-cache.json");
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private StoreCache? _cache;
    // True when there are lookups the cache file doesn't have yet.
    private bool _cacheChanged;

    [GeneratedRegex("""\bEA\b""")]
    private static partial Regex EaWord { get; }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag { get; }

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NotLowercaseLetterOrDigit { get; }

    public void Dispose() => _cacheLock.Dispose();

    /// <summary>
    /// A cover image file for the game, or null if none was found.
    /// Order: Steam's own local cache -> our cache -> download from the Steam store.
    /// </summary>
    public async Task<string?> GetCoverAsync(Game game, string? steamFolder, CancellationToken cancel = default)
    {
        int? appId = game.SteamAppId ?? await FindAppIdAsync(game.Name, cancel);
        if (appId is null) return null;
        game.SteamAppId = appId;

        if (steamFolder is not null && FindLocalSteamCover(steamFolder, appId.Value) is { } local) return local;

        string cached = Path.Combine(_artFolder, $"{appId}.jpg");
        if (File.Exists(cached)) return cached;

        string json = await _http.GetStringAsync(CoverLookupUrl(appId.Value), cancel);
        if (ParseCoverUrl(json) is not { } url) return null;

        byte[] bytes = await _http.GetByteArrayAsync(url, cancel);
        Directory.CreateDirectory(_artFolder);
        await File.WriteAllBytesAsync(cached, bytes, cancel);
        return cached;
    }

    public async Task<StoreDrm?> GetDrmAsync(Game game, CancellationToken cancel = default)
    {
        int? appId = game.SteamAppId ?? await FindAppIdAsync(game.Name, cancel);
        if (appId is null) return null;

        StoreCache cache = await LoadCacheAsync();
        if (cache.Drm.TryGetValue(appId.Value, out StoreDrm? known)) return known;

        string json = await _http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", cancel);
        StoreDrm drm = ParseDrm(json, appId.Value);
        cache.Drm[appId.Value] = drm;
        _cacheChanged = true;
        return drm;
    }

    public async Task<int?> FindAppIdAsync(string gameName, CancellationToken cancel = default)
    {
        StoreCache cache = await LoadCacheAsync();
        if (cache.AppIds.TryGetValue(gameName, out int? known)) return known;

        string json = await _http.GetStringAsync(
            $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(gameName)}&cc=US&l=english", cancel);
        int? appId = PickSearchResult(json, gameName);
        // Remember "not found" too, so we don't ask again.
        cache.AppIds[gameName] = appId;
        _cacheChanged = true;
        return appId;
    }

    // ---------------- small helpers, public so the unit tests can check them ----------------

    /// <summary>Steam keeps cover art it already downloaded in appcache\librarycache\[appid]\.</summary>
    public static string? FindLocalSteamCover(string steamFolder, int appId)
    {
        string folder = Path.Combine(steamFolder, "appcache", "librarycache", appId.ToString(CultureInfo.InvariantCulture));
        if (!Directory.Exists(folder)) return null;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 };
        return Directory.EnumerateFiles(folder, "library_600x900.jpg", options).FirstOrDefault()
            ?? Directory.EnumerateFiles(folder, "library_capsule.jpg", options).FirstOrDefault();
    }

    /// <summary>Picks the search result whose name matches the game (ignoring case, symbols and ™/®).</summary>
    public static int? PickSearchResult(string json, string gameName)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out JsonElement items)) return null;

        string wanted = Simplify(gameName);
        var results = items.EnumerateArray()
            .Select(item => (Id: item.GetProperty("id").GetInt32(), Name: Simplify(item.GetProperty("name").GetString())))
            .ToList();

        (int Id, string Name) exact = results.FirstOrDefault(r => r.Name == wanted);
        if (exact.Id != 0) return exact.Id;
        // e.g. folder "Far Cry 3" vs store "Far Cry 3 - Classic Edition": only accept the top result.
        (int Id, string Name) first = results.FirstOrDefault();
        return first.Id != 0 && wanted.Length > 3 && first.Name.StartsWith(wanted, StringComparison.Ordinal) ? first.Id : null;
    }

    public static string CoverLookupUrl(int appId)
    {
        string input = $$$"""{"ids":[{"appid":{{{appId}}}}],"context":{"language":"english","country_code":"US"},"data_request":{"include_assets":true}}""";
        return "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" + Uri.EscapeDataString(input);
    }

    /// <summary>Builds the 600x900 cover image address from Steam's "GetItems" answer.</summary>
    public static string? ParseCoverUrl(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("response", out JsonElement response)
            || !response.TryGetProperty("store_items", out JsonElement items)
            || items.GetArrayLength() == 0
            || !items[0].TryGetProperty("assets", out JsonElement assets)
            || !assets.TryGetProperty("asset_url_format", out JsonElement format)
            || !assets.TryGetProperty("library_capsule", out JsonElement capsule))
        {
            return null;
        }
        return "https://shared.akamai.steamstatic.com/store_item_assets/" + format.GetString()!.Replace("${FILENAME}", capsule.GetString());
    }

    /// <summary>Reads the DRM notices from Steam's "appdetails" answer.</summary>
    public static StoreDrm ParseDrm(string json, int appId)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(appId.ToString(CultureInfo.InvariantCulture), out JsonElement app) || !app.TryGetProperty("data", out JsonElement data))
            return new StoreDrm(null, []);

        var notices = DrmNoticeKeys
            .Select(key => data.TryGetProperty(key, out JsonElement value) ? CleanHtml(value.GetString()) : null)
            .OfType<string>()
            .ToList();
        string all = string.Join(". ", notices);

        var tags = new List<string>();
        if (all.Contains("Denuvo", StringComparison.OrdinalIgnoreCase)) tags.Add("Denuvo");
        if (all.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase)) tags.Add("Ubisoft Connect");
        if (EaWord.IsMatch(all)) tags.Add("EA app");
        if (all.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)) tags.Add("Rockstar Launcher");
        return new StoreDrm(notices.Count > 0 ? all : null, tags);
    }

    private static string? CleanHtml(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : WebUtility.HtmlDecode(HtmlTag.Replace(text, ". ")).Trim(' ', '.');

    private static string Simplify(string? name) =>
        NotLowercaseLetterOrDigit.Replace((name ?? "").ToLowerInvariant(), "");

    // ---------------- cache ----------------

    private sealed class StoreCache
    {
        public Dictionary<string, int?> AppIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, StoreDrm> Drm { get; set; } = [];
    }

    private async Task<StoreCache> LoadCacheAsync()
    {
        if (_cache is not null) return _cache;
        await _cacheLock.WaitAsync();
        try
        {
            if (_cache is null && File.Exists(_cacheFile))
            {
                try { _cache = JsonSerializer.Deserialize<StoreCache>(await File.ReadAllTextAsync(_cacheFile)); }
                catch (JsonException) { }
            }
            _cache ??= new StoreCache();
            _cache.AppIds = new Dictionary<string, int?>(_cache.AppIds, StringComparer.OrdinalIgnoreCase);
            return _cache;
        }
        finally { _cacheLock.Release(); }
    }

    /// <summary>Writes new lookups to the cache file. Does nothing (no disk write) when nothing new was looked up.</summary>
    public async Task SaveCacheAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            if (!_cacheChanged) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            await File.WriteAllTextAsync(_cacheFile, JsonSerializer.Serialize(_cache));
            _cacheChanged = false;
        }
        finally { _cacheLock.Release(); }
    }
}
