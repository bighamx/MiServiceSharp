using System.Net;
using MiServiceSharp.Auth;

namespace MiServiceSharp.Tests;

public sealed class AccountRecoveryTests
{
    private const string Success = """
        {"code":0,"userId":123,"passToken":"new-pass","location":"https://example.invalid/sts?x=1","nonce":1,"ssecurity":"dGVzdC1zZWN1cml0eQ=="}
        """;

    [Fact]
    public async Task FailedLoginPreservesOtherServicesAndExistingIdentity()
    {
        var store = new MemoryTokenStore();
        using var http = new HttpClient(new TestHttp((_, _) => Task.FromResult(TestHttp.Json("{\"code\":70016}"))));
        var account = new MiAccountClient(http, new MiAccountOptions(), store);
        Assert.False(await account.LoginAsync("xiaomiio"));
        Assert.Equal("test-pass", store.Token.PassToken);
        Assert.Equal(2, store.Token.Services.Count);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task CancelledLoginPropagatesCancellationAndPreservesTokens()
    {
        var store = new MemoryTokenStore();
        using var http = new HttpClient(new TestHttp((_, _) => throw new OperationCanceledException()));
        var account = new MiAccountClient(http, new MiAccountOptions(), store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => account.LoginAsync("xiaomiio"));
        Assert.Equal(2, store.Token.Services.Count);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task LoginUsesStoredCredentialsAndSuppressesWebSessionCookie()
    {
        var store = new MemoryTokenStore();
        using var http = new HttpClient(new TestHttp((request, _) =>
        {
            if (request.RequestUri!.Host == "account.xiaomi.com")
            {
                var cookie = string.Join(";", request.Headers.GetValues("Cookie"));
                Assert.Contains("userId=123", cookie);
                Assert.Contains("passToken=test-pass", cookie);
                Assert.Contains("pass_ua=", cookie);
                return Task.FromResult(TestHttp.Json(Success));
            }
            var response = TestHttp.Json("{}");
            response.Headers.Add("Set-Cookie", "serviceToken=new-service; Path=/");
            return Task.FromResult(response);
        }));
        var account = new MiAccountClient(http, new MiAccountOptions(), store);
        Assert.True(await account.LoginAsync("xiaomiio"), account.LastLoginError);
        Assert.Equal("new-service", store.Token.Services["xiaomiio"].ServiceToken);
        Assert.Equal("mina-token", store.Token.Services["micoapi"].ServiceToken);
    }

    [Fact]
    public async Task VerificationChallengeIsReportedWithoutDeletingTokens()
    {
        var store = new MemoryTokenStore();
        using var http = new HttpClient(new TestHttp((_, _) => Task.FromResult(TestHttp.Json(
            "{\"code\":0,\"notificationUrl\":\"https://account.xiaomi.com/test\"}"))));
        var account = new MiAccountClient(http, new MiAccountOptions { EnableInteractiveVerification = false }, store);
        await Assert.ThrowsAsync<NotificationVerificationRequiredException>(() => account.LoginAsync("micoapi"));
        Assert.Equal("mina-token", store.Token.Services["micoapi"].ServiceToken);
        Assert.Equal(0, store.ClearCount);
    }

    [Fact]
    public async Task Http401RefreshesOnlyTheExpiredService()
    {
        var store = new MemoryTokenStore();
        var requests = 0;
        using var http = new HttpClient(new TestHttp((request, _) =>
        {
            if (request.RequestUri!.Host == "account.xiaomi.com")
            {
                Assert.False(store.Token.Services.ContainsKey("xiaomiio"));
                Assert.True(store.Token.Services.ContainsKey("micoapi"));
                return Task.FromResult(TestHttp.Json(Success));
            }
            if (request.RequestUri.Host == "example.invalid")
            {
                var response = TestHttp.Json("{}");
                response.Headers.Add("Set-Cookie", "serviceToken=refreshed; Path=/");
                return Task.FromResult(response);
            }
            return Task.FromResult(++requests == 1
                ? TestHttp.Json("{}", HttpStatusCode.Unauthorized)
                : TestHttp.Json("{\"code\":0,\"result\":{}}"));
        }));
        var account = new MiAccountClient(http, new MiAccountOptions(), store);
        await account.MiRequestAsync("xiaomiio", "https://api.io.mi.com/app/test", (_, _) => null);
        Assert.Equal(2, requests);
        Assert.Equal("mina-token", store.Token.Services["micoapi"].ServiceToken);
    }
}
