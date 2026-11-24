using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Examples;

internal static class SubscriptionExamples
{
    public static async Task WatchLightPowerAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var light = devices.Single(device => device.Name == "客厅灯");
        var power = (await light.GetServiceAsync("light", cancellationToken)).GetProperty("on");

        // OAuth 凭证由 micli.exe oauth-login 授权后保存，Provider 负责自动刷新。
        using var oauthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var path = Environment.GetEnvironmentVariable("MI_OAUTH_TOKEN_PATH") ?? FileMiOAuthTokenStore.GetDefaultPath();
        var tokens = new MiOAuthTokenProvider(new MiOAuthClient(oauthHttp), new FileMiOAuthTokenStore(path));
        await using var notifications = client.CreateSubscriptionClient(tokens);
        notifications.Error += (_, error) => Console.Error.WriteLine(error.Exception.Message);

        // 先订阅，再读取初始值，期间收到的通知可能包含相同值。
        await using var subscription = await power.SubscribeAsync(notifications, (change, callbackToken) =>
        {
            Console.WriteLine($"{light.Name} 开关变化 = {change.Value}");
            return Task.CompletedTask;
        }, cancellationToken);

        var initial = await power.GetAsync(cancellationToken);
        Console.WriteLine($"初始开关状态 = {initial}");
        Console.WriteLine("正在监听，按 Ctrl+C 结束。");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public static async Task WatchDeviceAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var light = devices.Single(device => device.Name == "客厅灯");

        using var oauthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var path = Environment.GetEnvironmentVariable("MI_OAUTH_TOKEN_PATH") ?? FileMiOAuthTokenStore.GetDefaultPath();
        var tokens = new MiOAuthTokenProvider(new MiOAuthClient(oauthHttp), new FileMiOAuthTokenStore(path));
        await using var notifications = client.CreateSubscriptionClient(tokens);
        notifications.Error += (_, error) => Console.Error.WriteLine(error.Exception.Message);

        // 同一个 MQTT 连接可同时订阅设备全部属性和事件。
        await using var properties = await light.SubscribePropertiesAsync(notifications, (change, callbackToken) =>
        {
            Console.WriteLine($"属性 {change.Siid}/{change.Piid} = {change.Value}");
            return Task.CompletedTask;
        }, cancellationToken);

        await using var events = await light.SubscribeEventsAsync(notifications, (occurrence, callbackToken) =>
        {
            Console.WriteLine($"事件 {occurrence.Siid}/{occurrence.Eiid} {occurrence.Arguments}");
            return Task.CompletedTask;
        }, cancellationToken);

        Console.WriteLine("正在监听属性与事件，按 Ctrl+C 结束。");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        // 离开作用域时，订阅和连接按 await using 的逆序释放。
    }

    public static async Task WatchSensorsAndCameraAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var door = devices.Single(device => device.Name == "门窗传感器");
        var sensor = devices.Single(device => device.Name == "小米温湿度传感器");
        var gas = devices.Single(device => device.Name == "小米天然气卫士");
        var camera = devices.Single(device => device.Model == "chuangmi.camera.069a01");

        using var oauthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var path = Environment.GetEnvironmentVariable("MI_OAUTH_TOKEN_PATH") ?? FileMiOAuthTokenStore.GetDefaultPath();
        var tokens = new MiOAuthTokenProvider(new MiOAuthClient(oauthHttp), new FileMiOAuthTokenStore(path));
        await using var notifications = client.CreateSubscriptionClient(tokens);
        notifications.Error += (_, error) => Console.Error.WriteLine(error.Exception.Message);

        // 一个连接中注册不同设备的属性和事件订阅。
        await using var doorOpen = await DoorSensorExamples.SubscribeOpenAsync(door, notifications, cancellationToken);
        await using var peopleMotion = await CameraExamples.SubscribePeopleMotionAsync(camera, notifications, cancellationToken);

        var environment = await sensor.GetServiceAsync("temperature-humidity-sensor", cancellationToken);
        await using var temperature = await environment.GetProperty("temperature").SubscribeAsync(notifications, (change, token) =>
        {
            Console.WriteLine($"{sensor.Name} 温度变化={change.Value} ℃");
            return Task.CompletedTask;
        }, cancellationToken);

        var gasService = await gas.GetServiceAsync("gas-sensor", cancellationToken);
        await using var gasStatus = await gasService.GetProperty("status").SubscribeAsync(notifications, (change, token) =>
        {
            Console.WriteLine($"{gas.Name} 状态变化={change.Value}");
            return Task.CompletedTask;
        }, cancellationToken);

        Console.WriteLine("正在监听门磁、温度、燃气和摄像机事件，按 Ctrl+C 结束。");
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
