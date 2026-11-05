using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MfgManager.Core.Mod;

/// <summary>
/// Checks the mod's GitHub page for a newer version and downloads it.
/// Every download is checked against the SHA-256 fingerprint GitHub publishes for it,
/// so a broken or tampered download is thrown away instead of used.
/// </summary>
/// <param name="readModVersion">How to tell if a DLL is the mod. Tests swap this for a fake.</param>
public sealed partial class UpdateChecker(HttpClient http, Func<string, Version?>? readModVersion = null)
{
    public const string Repository = "dashdogy/MFG-Unlocker";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";

    // Far above the real sizes (the mod is about 14 MB). They stop a bad download from filling the disk.
    public const long MaxDownloadBytes = 512L * 1024 * 1024;
    public const long MaxExtractedBytes = 1024L * 1024 * 1024;
    public const int MaxZipEntries = 10_000;

    [GeneratedRegex("""^MFGUnlocker.*\.zip$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModZipName { get; }

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancel = default)
    {
        string json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", cancel);
        return ParseRelease(json);
    }

    /// <summary>
    /// Downloads the release zip into <paramref name="targetFolder"/> (the app's Mods folder), checks its fingerprint,
    /// unzips it next to it and returns the MFGUnlocker.dll inside.
    /// </summary>
    public async Task<ModPackage> DownloadAsync(ReleaseInfo release, string targetFolder, IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (release.ZipUrl is null || release.ZipName is null)
            throw new InvalidOperationException("This release has no MFGUnlocker zip to download.");

        string? expectedSha = release.Sha256;
        if (expectedSha is null && release.ShaSumsUrl is not null)
            expectedSha = ParseShaSums(await http.GetStringAsync(release.ShaSumsUrl, cancel), release.ZipName);
        if (expectedSha is null)
            throw new InvalidOperationException("GitHub doesn't list a fingerprint for this download, so it can't be checked. Download it yourself from " + ReleasesPage);

        Directory.CreateDirectory(targetFolder);
        string zipPath = Path.Combine(targetFolder, Path.GetFileName(release.ZipName));
        string partPath = zipPath + ".part";
        try
        {
            await DownloadFileAsync(release.ZipUrl, partPath, progress, cancel);
            string actualSha = Sha256OfFile(partPath);
            if (!actualSha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The download doesn't match GitHub's fingerprint, so it was deleted. Try again later.");
            File.Move(partPath, zipPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partPath)) File.Delete(partPath);
        }

        string extractFolder = Path.Combine(targetFolder, Path.GetFileNameWithoutExtension(zipPath));
        SafeExtract(zipPath, extractFolder);
        return ModPackage.FindNewestIn(extractFolder, readModVersion)
            ?? throw new InvalidDataException("The download didn't contain MFGUnlocker.dll.");
    }

    // ---------------- small helpers, public so the unit tests can check them ----------------

    /// <summary>Reads GitHub's "latest release" JSON.</summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("tag_name", out JsonElement tagElement)) return null;

        string tag = tagElement.GetString() ?? "";
        string? zipName = null;
        string? zipUrl = null;
        string? sha = null;
        string? shaSumsUrl = null;

        foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            string? url = IsHttps(asset.GetProperty("browser_download_url").GetString());
            if (ModZipName.IsMatch(name))
            {
                zipName = name;
                zipUrl = url;
                // GitHub writes it as "sha256:<hex>".
                if (asset.TryGetProperty("digest", out JsonElement digest) && digest.GetString() is { } d && d.StartsWith("sha256:", StringComparison.Ordinal))
                    sha = d["sha256:".Length..];
            }
            else if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                shaSumsUrl = url;
            }
        }

        // The page is opened in your browser, so it must really be on GitHub.
        string page = IsHttps(root.GetProperty("html_url").GetString()) is { } p && p.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ? p : ReleasesPage;
        return new ReleaseInfo(ModPackage.ParseVersion(tag), tag, page, zipName, zipUrl, sha, shaSumsUrl);
    }

    /// <summary>The address if it's a proper https:// web address, otherwise null (so a file path or "http://" is never used).</summary>
    public static string? IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.AbsoluteUri : null;

    /// <summary>Finds a file's fingerprint in a "SHA256SUMS.txt" file (lines like "&lt;hex&gt;  name.zip").</summary>
    public static string? ParseShaSums(string text, string fileName)
    {
        foreach (string line in text.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*').Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }
        return null;
    }

    public static string Sha256OfFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>
    /// Unzips, but refuses any entry that would land outside the target folder
    /// (a known trick where a zip contains paths like "..\..\Windows\file.dll").
    /// </summary>
    public static void SafeExtract(string zipPath, string targetFolder)
    {
        string root = PathUtil.Normalize(targetFolder) + Path.DirectorySeparatorChar;
        using ZipArchive zip = ZipFile.OpenRead(zipPath);

        // Checked before anything is written, so a "zip bomb" can't fill the disk.
        if (zip.Entries.Count > MaxZipEntries || zip.Entries.Sum(entry => entry.Length) > MaxExtractedBytes)
            throw new InvalidDataException("The zip is far bigger than expected, so it wasn't extracted.");

        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The zip contains an unsafe path ({entry.FullName}), so it wasn't extracted.");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private async Task DownloadFileAsync(string url, string path, IProgress<double>? progress, CancellationToken cancel)
    {
        using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        if (total > MaxDownloadBytes) throw new InvalidDataException("The download is far bigger than expected, so it was stopped.");

        await using Stream input = await response.Content.ReadAsStreamAsync(cancel);
        await using FileStream output = File.Create(path);
        byte[] buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancel)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancel);
            done += read;
            if (done > MaxDownloadBytes) throw new InvalidDataException("The download is far bigger than expected, so it was stopped.");
            if (total > 0) progress?.Report((double)done / total.Value);
        }
    }
}
