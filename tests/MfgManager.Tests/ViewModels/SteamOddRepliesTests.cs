using MfgManager.Core.Models;

namespace MfgManager.Tests.ViewModels;

/// <summary>
/// A reply from Steam can be valid JSON and still make no sense, if Steam changes its format or the reply is damaged.
/// Any program can also change the cache file. That must only cost a cover: the rest of start-up,
/// including the check for mod updates, still has to run.
/// </summary>
public class SteamOddRepliesTests
{
    private const string Search = "https://store.steampowered.com/api/storesearch";
    private const string Cover = "https://api.steampowered.com/IStoreBrowseService";
    private const string Details = "https://store.steampowered.com/api/appdetails";
    private const string CoverImage = "https://shared.akamai.steamstatic.com/";
    private const string GitHub = "https://api.github.com/";

    // A number no real game has, so the result can't depend on what's in this computer's Steam folder.
    private const int AppId = 999999901;
    private const string GoodSearch = """{"items":[{"id":999999901,"name":"Cool Game"}]}""";
    private const string NoCover = """{"response":{"store_items":[]}}""";
    private const string GoodDetails = """{"999999901":{"data":{}}}""";
    private const string NewerMod = """{"tag_name":"v9.0.0","html_url":"https://github.com/x","assets":[]}""";

    public static readonly TheoryData<string, string, string> OddReplies = new()
    {
        { """{"items":[{"name":"Cool Game"}]}""", NoCover, GoodDetails },
        { """{"items":[{"id":"abc","name":"Cool Game"}]}""", NoCover, GoodDetails },
        { """{"items":[{"id":1.5,"name":"Cool Game"}]}""", NoCover, GoodDetails },
        { """{"items":5}""", NoCover, GoodDetails },
        { """{"items":["x"]}""", NoCover, GoodDetails },
        { "[1,2]", NoCover, GoodDetails },
        { "null", NoCover, GoodDetails },
        { GoodSearch, """{"response":{"store_items":[{"assets":{"asset_url_format":null,"library_capsule":"x"}}]}}""", GoodDetails },
        { GoodSearch, """{"response":{"store_items":[{"assets":{"asset_url_format":5,"library_capsule":"x"}}]}}""", GoodDetails },
        { GoodSearch, """{"response":{"store_items":5}}""", GoodDetails },
        { GoodSearch, """{"response":{"store_items":["x"]}}""", GoodDetails },
        { GoodSearch, "[1,2]", GoodDetails },
        { GoodSearch, NoCover, """{"999999901":{"data":{"drm_notice":5}}}""" },
        { GoodSearch, NoCover, """{"999999901":5}""" },
        { GoodSearch, NoCover, "[1,2]" },
    };

    private static AppSetup Setup(string search, string cover, string details)
    {
        var s = new AppSetup();
        s.ScannerGames.Add(s.MakeGame("Cool Game", Launcher.Steam));
        s.Web.Add(Search, search).Add(Cover, cover).Add(Details, details).Add(GitHub, NewerMod);
        return s;
    }

    private static async Task Assert_start_up_finishes(AppSetup s)
    {
        Exception? problem = await Record.ExceptionAsync(() => s.Vm.InitializeAsync());

        Assert.True(problem is null, $"start-up stopped with {problem?.GetType().Name}: {problem?.Message}");
        Assert.Equal(["Cool Game"], s.Library.Games.Select(g => g.Name));
        Assert.NotNull(s.Vm.Mod.AvailableUpdate);
    }

    [Theory, MemberData(nameof(OddReplies))]
    public async Task A_strange_reply_doesnt_stop_start_up(string search, string cover, string details)
    {
        using AppSetup s = Setup(search, cover, details);

        await Assert_start_up_finishes(s);
    }

    [Theory]
    [InlineData("""{"AppIds":null,"Drm":null}""")]
    [InlineData("""{"AppIds":{},"Drm":null}""")]
    [InlineData("""{"AppIds":null,"Drm":{}}""")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("not json")]
    public async Task A_damaged_cache_file_is_replaced_instead_of_stopping_start_up(string cacheFile)
    {
        using AppSetup s = Setup(GoodSearch, NoCover, GoodDetails);
        string file = s.Temp.File("cache/store-cache.json", cacheFile);

        await Assert_start_up_finishes(s);

        // The lookup still worked, and the file is good again for the next start.
        Assert.NotNull(s.Library.Games[0].StoreDrm);
        Assert.Contains("Cool Game", File.ReadAllText(file));
    }

    [Fact]
    public async Task A_cover_that_cant_be_saved_doesnt_stop_start_up()
    {
        const string validCover = """{"response":{"store_items":[{"assets":{"asset_url_format":"steam/${FILENAME}","library_capsule":"x.jpg"}}]}}""";
        using AppSetup s = Setup(GoodSearch, validCover, GoodDetails);
        s.Web.Add(CoverImage, "JPEG");
        // A folder with the cover's file name, so saving the file is refused.
        s.Temp.Folder($"cache/art/{AppId}.jpg");

        await Assert_start_up_finishes(s);
    }

    [Fact]
    public async Task A_strange_reply_only_costs_that_game()
    {
        using var s = new AppSetup();
        s.ScannerGames.Add(s.MakeGame("Bad Game", Launcher.Steam));
        s.ScannerGames.Add(s.MakeGame("Cool Game", Launcher.Steam));
        s.Web.Add($"{Search}/?term=Bad", """{"items":5}""").Add(Search, GoodSearch).Add(Cover, NoCover).Add(Details, GoodDetails);

        await s.Library.ScanAsync(null);

        Assert.Null(s.Library.Games.Single(g => g.Name == "Bad Game").StoreDrm);
        Assert.NotNull(s.Library.Games.Single(g => g.Name == "Cool Game").StoreDrm);
    }

    [Fact]
    public async Task A_strange_reply_isnt_remembered_so_steam_is_asked_again_next_time()
    {
        using AppSetup s = Setup("""{"items":5}""", NoCover, GoodDetails);

        await s.Library.ScanAsync(null);
        await s.Library.ScanAsync(null);

        Assert.Equal(2, s.Web.Requests.Count(url => url.StartsWith(Search, StringComparison.Ordinal)));
    }
}
