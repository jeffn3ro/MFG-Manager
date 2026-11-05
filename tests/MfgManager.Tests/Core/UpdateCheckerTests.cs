using System.IO.Compression;
using System.Security.Cryptography;
using MfgManager.Core.Mod;

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
            foreach ((string name, string content) in entries)
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
        string json = $$"""{"tag_name":"v1","html_url":"{{htmlUrl}}","assets":[{"name":"MFGUnlocker.zip","browser_download_url":"file:///C:/x.zip"}]}""";

        ReleaseInfo release = UpdateChecker.ParseRelease(json)!;

        Assert.Equal(expected, release.PageUrl);
        // Only https downloads.
        Assert.Null(release.ZipUrl);
    }

    [Fact]
    public void A_zip_bomb_is_refused_before_anything_is_written()
    {
        using var temp = new TempFolder();
        string zipPath = Path.Combine(temp.Path, "bomb.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Zeros compress to almost nothing, so this small zip would unpack to more than the limit.
            using Stream entry = zip.CreateEntry("huge.bin", CompressionLevel.Fastest).Open();
            byte[] zeros = new byte[1024 * 1024];
            for (int i = 0; i < UpdateChecker.MaxExtractedBytes / zeros.Length + 1; i++) entry.Write(zeros);
        }
        string target = Path.Combine(temp.Path, "out");

        Assert.Throws<InvalidDataException>(() => UpdateChecker.SafeExtract(zipPath, target));

        Assert.False(File.Exists(Path.Combine(target, "huge.bin")));
    }

    [Fact]
    public void A_zip_with_too_many_files_is_refused()
    {
        using var temp = new TempFolder();
        string zipPath = Path.Combine(temp.Path, "many.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i <= UpdateChecker.MaxZipEntries; i++) zip.CreateEntry($"f{i}.txt");
        }

        Assert.Throws<InvalidDataException>(() => UpdateChecker.SafeExtract(zipPath, Path.Combine(temp.Path, "out")));
    }

    [Fact]
    public void Reads_github_release_json()
    {
        ReleaseInfo release = UpdateChecker.ParseRelease(ReleaseJson.Replace("{SHA}", "abc123"))!;

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
        byte[] zip = MakeZip(("MFGUnlocker.dll", "MOD 1.8.0"), ("README.md", "hi"));
        string sha = Convert.ToHexString(SHA256.HashData(zip));
        FakeWeb web = new FakeWeb().Add("https://example.test/MFGUnlocker", zip);
        var checker = new UpdateChecker(web.Client(), FakeMod.ReadVersion);

        ModPackage mod = await checker.DownloadAsync(UpdateChecker.ParseRelease(ReleaseJson.Replace("{SHA}", sha.ToLowerInvariant()))!, temp.Path);

        Assert.Equal(new Version(1, 8, 0), mod.Version);
        Assert.Equal(Path.Combine(temp.Path, "MFGUnlocker-v1.8.0", "MFGUnlocker.dll"), mod.DllPath);
        Assert.True(File.Exists(Path.Combine(temp.Path, "MFGUnlocker-v1.8.0.zip")));
    }

    [Fact]
    public async Task Download_with_wrong_fingerprint_is_thrown_away()
    {
        using var temp = new TempFolder();
        FakeWeb web = new FakeWeb().Add("https://example.test/MFGUnlocker", MakeZip(("MFGUnlocker.dll", "MOD 1.8.0")));
        var checker = new UpdateChecker(web.Client(), FakeMod.ReadVersion);
        ReleaseInfo release = UpdateChecker.ParseRelease(ReleaseJson.Replace("{SHA}", "00"))!;

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(release, temp.Path));

        // Nothing left behind.
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task Uses_sha256sums_when_github_has_no_digest()
    {
        using var temp = new TempFolder();
        byte[] zip = MakeZip(("MFGUnlocker.dll", "MOD 1.8.0"));
        string sha = Convert.ToHexString(SHA256.HashData(zip));
        FakeWeb web = new FakeWeb()
            .Add("https://example.test/MFGUnlocker", zip)
            .Add("https://example.test/SHA256SUMS.txt", $"{sha}  MFGUnlocker-v1.8.0.zip");
        ReleaseInfo release = UpdateChecker.ParseRelease(ReleaseJson.Replace("\"sha256:{SHA}\"", "null"))!;

        ModPackage mod = await new UpdateChecker(web.Client(), FakeMod.ReadVersion).DownloadAsync(release, temp.Path);

        Assert.Equal(new Version(1, 8, 0), mod.Version);
    }

    [Fact]
    public void Zip_with_paths_escaping_the_folder_is_refused()
    {
        using var temp = new TempFolder();
        string zipPath = Path.Combine(temp.Path, "evil.zip");
        File.WriteAllBytes(zipPath, MakeZip(("../../evil.dll", "bad")));

        Assert.Throws<InvalidDataException>(() => UpdateChecker.SafeExtract(zipPath, Path.Combine(temp.Path, "out")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "..", "evil.dll")));
    }

    [Fact]
    public void Fingerprint_of_a_known_file()
    {
        using var temp = new TempFolder();
        string file = temp.File("abc.txt", "abc");

        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", UpdateChecker.Sha256OfFile(file));
    }
}
