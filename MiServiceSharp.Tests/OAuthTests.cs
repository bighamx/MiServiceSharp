using System.Net;
using System.Text.Json.Nodes;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

public sealed class OAuthTests
{
    [Fact]
    public void OfficialApplicationUsesWebhookRedirectAndDeviceBoundState()
    {
        using var http = new HttpClient();
        var client = new MiOAuthClient(http);
        var request = client.CreateAuthorization("2882303761520251711", "http://homeassistant.local:8123");
        Assert.StartsWith("http://homeassistant.local:8123/api/webhook/", request.RedirectUri);
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes("d=" + request.DeviceId))).ToLowerInvariant();
        Assert.Equal(expected, request.State);
        Assert.Contains("skip_confirm=false", request.AuthorizationUri.Query);
        Assert.NotEqual(request.State, client.CreateAuthorization("2882303761520251711", "http://homeassistant.local:8123").State);
    }

    [Fact]
    public async Task PendingAuthorizationCanResumeAfterProcessRestart()
    {
        var directory = Directory.CreateTempSubdirectory("miservice-oauth-pending-");
        try
        {
            var path = Path.Combine(directory.FullName, ".mi.oauth.json");
            using var http = new HttpClient();
            var request = new MiOAuthClient(http).CreateAuthorization("123456", "http://localhost:8123/callback");
            await new FileMiOAuthTokenStore(path).SaveAuthorizationAsync(request);
            var restartedStore = new FileMiOAuthTokenStore(path);
            Assert.Equal(request, await restartedStore.LoadAuthorizationAsync());
            Assert.Null(await restartedStore.LoadAsync());
            await restartedStore.ClearAuthorizationAsync();
            Assert.Null(await restartedStore.LoadAuthorizationAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task AuthorizationValidatesCallbackStateAndExchangesCode()
    {
        using var http = new HttpClient(new TestHttp((request, _) =>
        {
            Assert.Equal("ha.api.io.mi.com", request.RequestUri!.Host);
            var data = JsonNode.Parse(Uri.UnescapeDataString(request.RequestUri.Query[6..]))!;
            Assert.Equal(123456, data["client_id"]!.GetValue<long>());
            Assert.Equal("test-code", data["code"]!.GetValue<string>());
            Assert.StartsWith("ha.", data["device_id"]!.GetValue<string>());
            return Task.FromResult(TestHttp.Json("""
                {"code":0,"result":{"access_token":"test-access","refresh_token":"test-refresh","expires_in":3600}}
                """));
        }));
        var client = new MiOAuthClient(http);
        var authorization = client.CreateAuthorization("123456", "http://localhost:8123/callback");
        Assert.Contains("response_type=code", authorization.AuthorizationUri.Query);
        var callback = new Uri($"http://localhost:8123/callback?state={authorization.State}&code=test-code");
        var token = await client.CompleteAuthorizationAsync(authorization, callback);
        Assert.Equal("test-access", token.AccessToken);
        Assert.Equal("test-refresh", token.RefreshToken);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task CallbackWithInvalidStateOrRedirectIsRejectedBeforeHttp()
    {
        using var http = new HttpClient(new TestHttp((_, _) => throw new InvalidOperationException("HTTP must not be called.")));
        var client = new MiOAuthClient(http);
        var authorization = client.CreateAuthorization("123456", "http://localhost:8123/callback");
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAuthorizationAsync(authorization,
            new Uri("http://localhost:8123/callback?state=wrong&code=test")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CompleteAuthorizationAsync(authorization,
            new Uri($"http://example.invalid/callback?state={authorization.State}&code=test")));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CompleteAuthorizationAsync(authorization,
            new Uri($"http://localhost:8123/callback?state={authorization.State}&state={authorization.State}&code=test")));
    }

    [Fact]
    public async Task ConcurrentTokenRequestsRefreshOnceAndPersistCredentials()
    {
        var directory = Directory.CreateTempSubdirectory("miservice-oauth-test-");
        try
        {
            var store = new FileMiOAuthTokenStore(Path.Combine(directory.FullName, ".mi.oauth.json"));
            await store.SaveAsync(Token() with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) });
            var refreshes = 0;
            using var http = new HttpClient(new TestHttp((request, _) =>
            {
                Interlocked.Increment(ref refreshes);
                Assert.Equal("de.ha.api.io.mi.com", request.RequestUri!.Host);
                var data = JsonNode.Parse(Uri.UnescapeDataString(request.RequestUri.Query[6..]))!;
                Assert.Equal("old-refresh", data["refresh_token"]!.GetValue<string>());
                return Task.FromResult(TestHttp.Json("""
                    {"code":0,"result":{"access_token":"new-access","refresh_token":"new-refresh","expires_in":7200}}
                    """));
            }));
            var provider = new MiOAuthTokenProvider(new MiOAuthClient(http), store);
            var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => provider.GetTokenAsync()));
            Assert.Equal(1, refreshes);
            Assert.All(tokens, token => Assert.Equal("new-access", token.AccessToken));
            Assert.Equal("new-refresh", (await store.LoadAsync())!.RefreshToken);
            Assert.Single(directory.GetFiles());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RefreshFailurePreservesStoredTokenAndHidesResponseBody()
    {
        var directory = Directory.CreateTempSubdirectory("miservice-oauth-test-");
        try
        {
            var store = new FileMiOAuthTokenStore(Path.Combine(directory.FullName, ".mi.oauth.json"));
            var original = Token() with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
            await store.SaveAsync(original);
            using var http = new HttpClient(new TestHttp((_, _) => Task.FromResult(
                TestHttp.Json("{\"sensitive\":\"test-secret\"}", HttpStatusCode.Unauthorized))));
            var provider = new MiOAuthTokenProvider(new MiOAuthClient(http), store);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync());
            Assert.DoesNotContain("test-secret", error.Message);
            Assert.Equal(original, await store.LoadAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    internal static MiOAuthToken Token() => new()
    {
        AppId = "123456",
        RedirectUri = "http://localhost:8123/callback",
        DeviceId = "ha." + Guid.NewGuid().ToString("N"),
        Region = "de",
        AccessToken = "old-access",
        RefreshToken = "old-refresh",
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
    };
}
