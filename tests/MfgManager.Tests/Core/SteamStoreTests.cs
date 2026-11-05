using System.Text;
using MfgManager.Core.Models;
using MfgManager.Core.Store;

namespace MfgManager.Tests.Core;

public class SteamStoreTests
{
    private const string SearchJson = """
        {"total":3,"items":[
          {"id":3751950,"name":"Assassin's Creed Black Flag Resynced"},
          {"id":4496530,"name":"Assassin's Creed Black Flag Resynced - Hellfire Character Pack"}]}
        """;

    private const string ItemsJson = """
        {"response":{"store_items":[{"appid":3751950,"assets":{
          "asset_url_format":"steam/apps/3751950/${FILENAME}?t=1",
          "library_capsule":"36a1/library_capsule.jpg"}}]}}
        """;

    private const string DetailsJson = """
        {"3751950":{"success":true,"data":{
          "drm_notice":"Denuvo Anti-tamper<br>5 a day machine activation limit",
          "ext_user_account_notice":"Ubisoft Account (Supports Linking to Steam Account)"}}}
        """;

    [Theory]
    [InlineData("Assassin's Creed Black Flag Resynced", 3751950)]
    [InlineData("ASSASSINS CREED BLACK FLAG RESYNCED™", 3751950)]
    // Top result starts with the name.
    [InlineData("Assassin's Creed Black Flag", 3751950)]
    [InlineData("Some Other Game", null)]
    public void Matches_search_results_by_name(string name, int? expected) =>
        Assert.Equal(expected, SteamStore.PickSearchResult(SearchJson, name));

    [Fact]
    public void Builds_cover_address() =>
        Assert.Equal(
            "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3751950/36a1/library_capsule.jpg?t=1",
            SteamStore.ParseCoverUrl(ItemsJson));

    [Fact]
    public void No_cover_address_when_steam_gives_none() =>
        Assert.Null(SteamStore.ParseCoverUrl("""{"response":{"store_items":[{"assets":{"asset_url_format":null,"library_capsule":"x"}}]}}"""));

    [Fact]
    public void Reads_drm_notices()
    {
        StoreDrm drm = SteamStore.ParseDrm(DetailsJson, 3751950);

        Assert.Equal(["Denuvo", "Ubisoft Connect"], drm.Tags);
        Assert.StartsWith("Denuvo Anti-tamper. 5 a day", drm.Notice);
    }

    [Fact]
    public void Uses_steams_own_cached_cover_first()
    {
        using var temp = new TempFolder();
        string cover = temp.File("Steam/appcache/librarycache/2246340/e3fc/library_600x900.jpg");

        Assert.Equal(cover, SteamStore.FindLocalSteamCover(Path.Combine(temp.Path, "Steam"), 2246340));
    }

    [Fact]
    public async Task Downloads_cover_and_drm_once_then_uses_the_cache()
    {
        using var temp = new TempFolder();
        FakeWeb web = new FakeWeb()
            .Add("https://store.steampowered.com/api/storesearch", SearchJson)
            .Add("https://api.steampowered.com/IStoreBrowseService/GetItems", ItemsJson)
            .Add("https://store.steampowered.com/api/appdetails", DetailsJson)
            .Add("https://shared.akamai.steamstatic.com/", Encoding.UTF8.GetBytes("JPEG"));
        var game = new Game { Name = "Assassin's Creed Black Flag Resynced", Launcher = Launcher.Ubisoft, Folder = temp.Path };

        var store = new SteamStore(web.Client(), temp.Path);
        string? cover = await store.GetCoverAsync(game, steamFolder: null);
        StoreDrm? drm = await store.GetDrmAsync(game);
        int requestsSoFar = web.Requests.Count;

        // Lookups aren't written one by one, only once after the batch.
        string cacheFile = Path.Combine(temp.Path, "store-cache.json");
        Assert.False(File.Exists(cacheFile));
        await store.SaveCacheAsync();
        DateTime savedAt = File.GetLastWriteTimeUtc(cacheFile);

        // A fresh SteamStore (like the next app start) must not go online again, or write anything.
        var again = new SteamStore(web.Client(), temp.Path);
        var game2 = new Game { Name = game.Name, Launcher = Launcher.Ubisoft, Folder = temp.Path };
        Assert.Equal(cover, await again.GetCoverAsync(game2, steamFolder: null));
        Assert.Equal(drm!.Tags, (await again.GetDrmAsync(game2))!.Tags);
        await again.SaveCacheAsync();
        Assert.Equal(savedAt, File.GetLastWriteTimeUtc(cacheFile));

        Assert.Equal("JPEG", File.ReadAllText(cover!));
        Assert.Equal(3751950, game.SteamAppId);
        Assert.Equal(4, requestsSoFar);
        Assert.Equal(requestsSoFar, web.Requests.Count);
    }
}
