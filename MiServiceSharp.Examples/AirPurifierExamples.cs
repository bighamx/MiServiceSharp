using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class AirPurifierExamples
{
    // zhimi.airpurifier.va1，设备状态、空气质量和滤芯分属不同服务。
    public static async Task ReadStateAsync(MiioDeviceClient purifier, CancellationToken cancellationToken = default)
    {
        var service = await purifier.GetServiceAsync("air-purifier", cancellationToken);
        var power = await service.GetProperty("on").GetAsync(cancellationToken);
        var mode = await service.GetProperty("mode").GetAsync(cancellationToken);
        var environment = await purifier.GetServiceAsync("environment", cancellationToken);
        var pm25 = await environment.GetProperty("pm2.5-density").GetAsync(cancellationToken);
        var humidity = await environment.GetProperty("relative-humidity").GetAsync(cancellationToken);
        var filter = await purifier.GetServiceAsync("filter", cancellationToken);
        var life = await filter.GetProperty("filter-life-level").GetAsync(cancellationToken);
        Console.WriteLine($"{purifier.Name} 开关={power}，模式={mode}，PM2.5={pm25}，湿度={humidity}%，滤芯剩余={life}%");
    }

    public static async Task SetAutoModeAsync(MiioDeviceClient purifier, CancellationToken cancellationToken = default)
    {
        var service = await purifier.GetServiceAsync("air-purifier", cancellationToken);
        await service.GetProperty("mode").SetAsync(0, cancellationToken);
        await service.GetProperty("on").SetAsync(true, cancellationToken);
    }

    public static async Task EnableChildLockAsync(MiioDeviceClient purifier, CancellationToken cancellationToken = default)
    {
        var controls = await purifier.GetServiceAsync("physical-controls-locked", cancellationToken);
        await controls.GetProperty("physical-controls-locked").SetAsync(true, cancellationToken);
    }
}
