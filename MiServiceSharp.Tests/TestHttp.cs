using System.Net;
using System.Text;
using MiServiceSharp.Models;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

internal sealed class TestHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => handler(request, cancellationToken);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static async Task<Dictionary<string, string>> FormAsync(HttpRequestMessage request)
        => (await request.Content!.ReadAsStringAsync()).Split('&').Select(part => part.Split('=', 2))
            .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
}

internal sealed class MemoryTokenStore : IMiTokenStore
{
    public MiTokenBundle Token { get; private set; } = new()
    {
        DeviceId = "test-device",
        UserId = 123,
        PassToken = "test-pass",
        Services = new()
        {
            ["micoapi"] = new() { SSecurity = "dGVzdC1zZWN1cml0eQ==", ServiceToken = "mina-token" },
            ["xiaomiio"] = new() { SSecurity = "dGVzdC1zZWN1cml0eQ==", ServiceToken = "miio-token" }
        }
    };
    public int ClearCount { get; private set; }
    public Task<MiTokenBundle?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<MiTokenBundle?>(Token);
    public Task SaveAsync(MiTokenBundle tokenBundle, CancellationToken cancellationToken = default)
    {
        Token = tokenBundle;
        return Task.CompletedTask;
    }
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        return Task.CompletedTask;
    }
}
