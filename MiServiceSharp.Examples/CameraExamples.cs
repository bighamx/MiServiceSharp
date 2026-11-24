using MiServiceSharp.Devices;
using MiServiceSharp.Subscriptions;

namespace MiServiceSharp.Examples;

internal static class CameraExamples
{
    // chuangmi.camera.069a01。
    public static async Task ReadStateAsync(MiioDeviceClient camera, CancellationToken cancellationToken = default)
    {
        var controls = await camera.GetServiceAsync("camera-control", cancellationToken);
        var power = await controls.GetProperty("on").GetAsync(cancellationToken);
        var nightShot = await controls.GetProperty("night-shot").GetAsync(cancellationToken);
        var tracking = await controls.GetProperty("motion-tracking").GetAsync(cancellationToken);
        var card = await camera.GetServiceAsync("memory-card-management", cancellationToken);
        var cardStatus = await card.GetProperty("status").GetAsync(cancellationToken);
        Console.WriteLine($"{camera.Name} 开关={power}，夜视={nightShot}，移动追踪={tracking}，存储卡状态={cardStatus}");
    }

    public static async Task ConfigureMotionDetectionAsync(MiioDeviceClient camera, CancellationToken cancellationToken = default)
    {
        var controls = await camera.GetServiceAsync("camera-control", cancellationToken);
        await controls.GetProperty("night-shot").SetAsync(2, cancellationToken);
        var detection = await camera.GetServiceAsync("motion-detection", cancellationToken);
        await detection.GetProperty("motion-detection").SetAsync(true, cancellationToken);
        await detection.GetProperty("detection-sensitivity").SetAsync(1, cancellationToken);
    }

    public static Task<IAsyncDisposable> SubscribePeopleMotionAsync(MiioDeviceClient camera,
        MiotSubscriptionClient notifications, CancellationToken cancellationToken = default)
    {
        // 本型号 ai-scene 服务 IID=15，people-motion 事件 EIID=2。
        return notifications.SubscribeEventAsync(camera.Did, 15, 2, (occurrence, callbackToken) =>
        {
            Console.WriteLine($"{camera.Name} 人形移动事件 {occurrence.Arguments}");
            return Task.CompletedTask;
        }, cancellationToken);
    }
}
