using MiServiceSharp.Devices;
using MiServiceSharp.Subscriptions;

namespace MiServiceSharp.Examples;

internal static class DoorSensorExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient door, CancellationToken cancellationToken = default)
    {
        var magnet = await door.GetServiceAsync("magnet-sensor", cancellationToken);
        var contact = await magnet.GetProperty("contact-state").GetAsync(cancellationToken);
        Console.WriteLine($"{door.Name} 触点状态原始值={contact}");
    }

    public static Task<IAsyncDisposable> SubscribeOpenAsync(MiioDeviceClient door,
        MiotSubscriptionClient notifications, CancellationToken cancellationToken = default)
    {
        // lumi.sensor_magnet.v2 的 magnet-sensor 服务 IID=2，open 事件 EIID=2。
        return notifications.SubscribeEventAsync(door.Did, 2, 2, (occurrence, callbackToken) =>
        {
            Console.WriteLine($"{door.Name} 收到开门事件 {occurrence.Arguments}");
            return Task.CompletedTask;
        }, cancellationToken);
    }
}
