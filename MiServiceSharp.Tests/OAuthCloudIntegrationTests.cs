using MiServiceSharp.Auth;
using MiServiceSharp.Storage;
using MiServiceSharp.Subscriptions;

namespace MiServiceSharp.Tests;

public sealed class OAuthCloudIntegrationTests
{
    [MiOAuthIntegrationFact]
    public async Task RefreshTokenAndSubscribeToRealCloud()
    {
        var deviceId = Environment.GetEnvironmentVariable("MI_OAUTH_TEST_DEVICE")!;
        var store = new FileMiOAuthTokenStore(FileMiOAuthTokenStore.GetDefaultPath());
        var current = await store.LoadAsync();
        Assert.NotNull(current);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var oauth = new MiOAuthClient(http);
        var renewed = await oauth.RefreshAsync(current);
        Assert.True(renewed.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.False(string.IsNullOrWhiteSpace(renewed.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(renewed.RefreshToken));
        await store.SaveAsync(renewed);
        var provider = new MiOAuthTokenProvider(oauth, store);
        await using var client = new MiotSubscriptionClient(provider.GetTokenAsync);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var subscription = await client.SubscribePropertiesAsync(deviceId, (_, _) => Task.CompletedTask, timeout.Token);
        Assert.True(client.IsConnected);
    }
}

public sealed class MiOAuthIntegrationFactAttribute : FactAttribute
{
    public MiOAuthIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_MI_OAUTH_INTEGRATION") != "1"
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MI_OAUTH_TEST_DEVICE")))
        {
            Skip = "Set RUN_MI_OAUTH_INTEGRATION=1 and MI_OAUTH_TEST_DEVICE to test real OAuth and MQTT services.";
        }
    }
}
