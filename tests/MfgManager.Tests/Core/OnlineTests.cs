using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MfgManager.Core.Mod;
using MfgManager.Core.Models;
using MfgManager.Core.Store;

namespace MfgManager.Tests.Core;

// These tests never touch the real internet: FakeWeb answers instead.

public class UpdateCheckerTests
{
    private const string ReleaseJson = """
        {
          "tag_name": "v1.8.0",
          "html_url": "https://github.com/dashdogy/MFG-Unlocker/releases/tag/v1.8.0",
          "assets": [
            { "name": "MFGUnlocker-v1.8.0.zip", "browser_download_url": "https://example.test/MFGUnlocker-v1.8.0.zip", "digest": "sha256:{SHA}" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/SHA256SUMS.txt", "digest": null }
          ]
        }
        """;

    private static byte[] MakeZip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        }
        return memory.ToArray();
    }

    [Theory]
    [InlineData("https://github.com/dashdogy/MFG-Unlocker/releases/tag/v1.8.0", "https://github.com/dashdogy/MFG-Unlocker/releases/tag/v1.8.0")]
    [InlineData("file:///C:/Windows/System32/calc.exe", UpdateChecker.ReleasesPage)]
    [InlineData("http://github.com/x", UpdateChecker.ReleasesPage)]
    [InlineData("https://evil.test/x", UpdateChecker.ReleasesPage)]
    public void Release_page_must_be_on_github(string htmlUrl, string expected)
    {
        var json = $$"""{"tag_name":"v1","html_url":"{{htmlUrl}}","assets":[{"name":"MFGUnlocker.zip","browser_download_url":"file:///C:/x.zip"}]}""";

        var release = UpdateChecker.ParseRelease(json)!;

        Assert.Equal(expected, release.PageUrl);
        Assert.Null(release.ZipUrl);   // only https downloads
    }

    [Fact]
    public void A_zip_bomb_is_refused_before_anything_is_written()
    {
        using var temp = new TempFolder();
        var zipPath = Path.Combine(temp.Path, "bomb.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Zeros compress to almost nothing, so this small zip would unpack to more than the limit.
            using var entry = zip.CreateEntry("huge.bin", CompressionLevel.Fastest).Open();
            var zeros = new byte[1024 * 1024];
            for (var i = 0; i < UpdateChecker.MaxExtractedBytes / zeros.Length + 1; i++) entry.Write(zeros);
        }
        var target = Path.Combine(temp.Path, "out");

        Assert.Throws<InvalidDataException>(() => UpdateChecker.SafeExtract(zipPath, target));

        Assert.False(File.Exists(Path.Combine(target, "huge.bin")));
    }

    [Fact]
    public void A_zip_with_too_many_files_is_refused()
    {
        using var temp = new TempFolder();
        var zipPath = Path.Combine(temp.Path, "many.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (var i = 0; i <= UpdateChecker.MaxZipEntries; i++) zip.CreateEntry($"f{i}.txt");
        }

        Assert.Throws<InvalidDataException>(() => UpdateChecker.SafeExtract(zipPath, Path.Combine(temp.Path, "out")));
    }

    [Fact]
    public void Reads_github_release_json()
    {
        var release = UpdateChecker.ParseRelease(ReleaseJson.Replace("{SHA}", "abc123"))!;

        Assert.Equal(new Version(1, 8, 0), release.Version);
        Assert.Equal("MFGUnlocker-v1.8.0.zip", release.ZipName);
        Assert.Equal("abc123", release.Sha256);
        Assert.Equal("https://example.test/SHA256SUMS.txt", release.ShaSumsUrl);
    }

    [Fact]
    public void Reads_sha256sums_files() =>
        Assert.Equal("ABCDEF", UpdateChecker.ParseShaSums("111  other.zip\nABCDEF *MFGUnlocker-v1.8.0.zip\n", "MFGUnlocker-v1.8.0.zip"));

    [Fact]
    public async Task Downloads_checks_fingerprint_and_unzips()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(("MFGUnlocker.dll", "MOD 1.8.0"), ("README.md", "hi"));
        var sha = Convert.ToHexString(SHA256.HashData(zip));
        var web = new FakeWeb().Add("https://example.test/MFGUnlocker", zip);
        var checker = new UpdateChecker(web.Client(), FakeMod.ReadVersion);

        var mod = await checker.DownloadAsync(UpdateChecker.ParseRelease(ReleaseJson.Replace("{SHA}", sha.ToLower()))!, temp.Path);

        Assert.Equal(new Version(1, 8, 0), mod.Version);
        Assert.Equal(Path.Combine(temp.Path, "MFGUnlocker-v1.8.0", "MFGUnlocker.dll"), mod.DllPath);
        Assert.True(File.Exists(Path.Combine(temp.Path, "MFGUnlocker-v1.8.0.zip")));
    }

    [Fact]
    public async Task Download_with_wrong_fingerprint_is_thrown_away()
    {
        using var temp = new TempFolder();
        var web = new FakeWeb().Add("https://example.test/MFGUnlocker", MakeZip(("MFGUnlocker.dll", "MOD 1.8.0")));
        var checker = new UpdateChecker(web.Client(), FakeMod.ReadVersion);
        var release = UpdateChecker.ParseRelease(ReleaseJson.Replace("{SHA}", "00"))!;

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(release, temp.Path));

        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));   // nothing left behind
    }

    [Fact]
    public async Task Uses_sha256sums_when_github_has_no_digest()
    {
        using var temp = new TempFolder();
        var zip = MakeZip(("MFGUnlocker.dll", "MOD 1.8.0"));
        var sha = Convert.ToHexString(SHA256.HashData(zip));
        var web = new FakeWeb()
            .Add("https://example.test/MFGUnlocker", zip)
            .Add("https://example.test/SHA256SUMS.txt", $"{sha}  MFGUnlocker-v1.8.0.zip");
        var release = UpdateChecker.ParseRelease(ReleaseJson.Replace("\"sha256:{SHA}\"", "null"))!;

        var mod = await new UpdateChecker(web.Client(), FakeMod.ReadVersion).DownloadAsync(release, temp.Path);

        Assert.Equal(new Version(1, 8, 0), mod.Version);
    }

    [Fact]
    public void Zip_with_paths_escaping_the_folder_is_refused()
    {
        using var temp = new TempFolder();
        var zipPath = Path.Combine(temp.Path, "evil.zip");
        File.WriteAllBytes(zipPath, MakeZip(("../../evil.dll", "bad")));

        Assert.Throws<InvalidDataException>(() => UpdateChecker.SafeExtract(zipPath, Path.Combine(temp.Path, "out")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "..", "evil.dll")));
    }

    [Fact]
    public void Fingerprint_of_a_known_file()
    {
        using var temp = new TempFolder();
        var file = temp.File("abc.txt", "abc");

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", UpdateChecker.Sha256OfFile(file));
    }
}

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
    [InlineData("Assassin's Creed Black Flag", 3751950)]   // top result starts with the name
    [InlineData("Some Other Game", null)]
    public void Matches_search_results_by_name(string name, int? expected) =>
        Assert.Equal(expected, SteamStore.PickSearchResult(SearchJson, name));

    [Fact]
    public void Builds_cover_address() =>
        Assert.Equal(
            "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/3751950/36a1/library_capsule.jpg?t=1",
            SteamStore.ParseCoverUrl(ItemsJson));

    [Fact]
    public void Reads_drm_notices()
    {
        var drm = SteamStore.ParseDrm(DetailsJson, 3751950);

        Assert.Equal(["Denuvo", "Ubisoft Connect"], drm.Tags);
        Assert.StartsWith("Denuvo Anti-tamper. 5 a day", drm.Notice);
    }

    [Fact]
    public void Uses_steams_own_cached_cover_first()
    {
        using var temp = new TempFolder();
        var cover = temp.File("Steam/appcache/librarycache/2246340/e3fc/library_600x900.jpg");

        Assert.Equal(cover, SteamStore.FindLocalSteamCover(Path.Combine(temp.Path, "Steam"), 2246340));
    }

    [Fact]
    public async Task Downloads_cover_and_drm_once_then_uses_the_cache()
    {
        using var temp = new TempFolder();
        var web = new FakeWeb()
            .Add("https://store.steampowered.com/api/storesearch", SearchJson)
            .Add("https://api.steampowered.com/IStoreBrowseService/GetItems", ItemsJson)
            .Add("https://store.steampowered.com/api/appdetails", DetailsJson)
            .Add("https://shared.akamai.steamstatic.com/", Encoding.UTF8.GetBytes("JPEG"));
        var game = new Game { Name = "Assassin's Creed Black Flag Resynced", Launcher = Launcher.Ubisoft, Folder = temp.Path };

        var store = new SteamStore(web.Client(), temp.Path);
        var cover = await store.GetCoverAsync(game, steamFolder: null);
        var drm = await store.GetDrmAsync(game);
        var requestsSoFar = web.Requests.Count;

        // Lookups aren't written one by one, only once after the batch.
        var cacheFile = Path.Combine(temp.Path, "store-cache.json");
        Assert.False(File.Exists(cacheFile));
        await store.SaveCacheAsync();
        var savedAt = File.GetLastWriteTimeUtc(cacheFile);

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
