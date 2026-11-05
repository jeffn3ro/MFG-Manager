using System.Net;
using MfgManager.Core.Mod;

namespace MfgManager.Tests;

/// <summary>A throw-away folder for one test. Deleted again when the test ends.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MfgManagerTests", Guid.NewGuid().ToString("N"));

    public TempFolder() => Directory.CreateDirectory(Path);

    /// <summary>Creates a file (and its folders) with some text in it. Returns its full path.</summary>
    public string File(string relativePath, string content = "")
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));   // also turns / into \
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public string Folder(string relativePath) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath)).FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
    }
}

/// <summary>
/// The tests don't use the real MFGUnlocker.dll. Instead a "fake mod" is a text file containing
/// "MOD 1.7.0", and this reader treats such files as the mod with that version.
/// </summary>
public static class FakeMod
{
    public static Version? ReadVersion(string path)
    {
        var text = System.IO.File.ReadAllText(path);
        return text.StartsWith("MOD ") ? ModPackage.ParseVersion(text[4..]) : null;
    }

    public static ModPackage Create(TempFolder temp, string version) =>
        new(temp.File($"downloads/MFGUnlocker-v{version}/MFGUnlocker.dll", $"MOD {version}"), new Version(version));
}

/// <summary>Stands in for the internet: answers each web address with canned content and counts requests.</summary>
public sealed class FakeWeb : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _responses = [];
    public List<string> Requests { get; } = [];

    public FakeWeb Add(string urlStart, string body) => Add(urlStart, System.Text.Encoding.UTF8.GetBytes(body));

    public FakeWeb Add(string urlStart, byte[] body)
    {
        _responses[urlStart] = body;
        return this;
    }

    public HttpClient Client() => new(this);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        var match = _responses.FirstOrDefault(r => url.StartsWith(r.Key));
        var response = match.Value is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(match.Value) };
        return Task.FromResult(response);
    }
}
