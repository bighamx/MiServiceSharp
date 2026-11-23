using MiServiceSharp.Auth;
using MiServiceSharp.Services;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

public sealed class IntegrationSmokeTests
{
    [MiIntegrationFact]
    public async Task Login_And_Query_DeviceLists_ShouldWork()
    {
        var store = new FileMiTokenStore(FileMiTokenStore.GetDefaultPath());
        using var httpClient = new HttpClient();
        var account = new MiAccountClient(httpClient, new MiAccountOptions
        {
            Username = Environment.GetEnvironmentVariable("MI_USER") ?? string.Empty,
            Password = Environment.GetEnvironmentVariable("MI_PASS") ?? string.Empty,
            EnableInteractiveVerification = false
        }, store);
        Assert.True(await account.LoginAsync("micoapi"), account.LastLoginError);
        Assert.True(await account.LoginAsync("xiaomiio"), account.LastLoginError);
        Assert.NotEmpty(await new MinaService(account).GetDevicesAsync());
        Assert.NotEmpty(await new MiioCloudService(account).GetDevicesAsync());
    }
}

public sealed class MiIntegrationFactAttribute : FactAttribute
{
    public MiIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("RUN_MI_INTEGRATION") != "1")
        {
            Skip = "Set RUN_MI_INTEGRATION=1 to test a real Xiaomi account.";
        }
    }
}
