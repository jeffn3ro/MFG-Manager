using System.Net;

namespace MfgManager.Tests;

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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString();
        Requests.Add(url);
        KeyValuePair<string, byte[]> match = _responses.FirstOrDefault(r => url.StartsWith(r.Key, StringComparison.Ordinal));
        HttpResponseMessage response = match.Value is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(match.Value) };
        return Task.FromResult(response);
    }
}
