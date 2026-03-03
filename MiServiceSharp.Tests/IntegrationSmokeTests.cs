using MiServiceSharp.Auth;
using MiServiceSharp.Services;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

public sealed class IntegrationSmokeTests
{
    [Fact]
    public async Task Login_And_Query_DeviceLists_ShouldWork()
    {
        var run = IsTruthy(GetEnv("RUN_MI_INTEGRATION"));
        if (!run)
        {
            return;
        }

        var user = GetEnv("MI_USER") ?? string.Empty;
        var pass = GetEnv("MI_PASS") ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(user));
        Assert.False(string.IsNullOrWhiteSpace(pass));

        var tokenPath = Path.Combine(Path.GetTempPath(), $"miservice-integration.json");
        var store = new FileMiTokenStore(tokenPath);
        using var httpClient = new HttpClient();
        var account = new MiAccountClient(httpClient, new MiAccountOptions
        {
            Username = user,
            Password = pass,
            EnableInteractiveVerification = true
        }, store);

        await account.InitializeAsync();
        var loginMina = await account.LoginAsync("micoapi");
        var loginMiio = await account.LoginAsync("xiaomiio");

        Assert.True(loginMina, "micoapi 登录失败，可能需要先完成 notificationUrl 的手机验证。");
        Assert.True(loginMiio, "xiaomiio 登录失败，可能需要先完成 notificationUrl 的手机验证。");

        var mina = new MinaService(account);
        var miio = new MiioCloudService(account);
        var minaDevices = await mina.DeviceListAsync();
        var miioDevices = await miio.DeviceListAsync();

        var d = minaDevices.FirstOrDefault(x => x.Name.Contains("客厅"));
        mina.TextToSpeechAsync(d.DeviceId, "你好，小芮芮");


        Assert.NotNull(minaDevices);
        Assert.NotNull(miioDevices);
    }

    private static string? GetEnv(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
            ?? Environment.GetEnvironmentVariable(name);
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" => true,
            "true" => true,
            "yes" => true,
            "on" => true,
            _ => false
        };
    }
}
