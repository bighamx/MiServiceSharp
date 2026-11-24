using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class RangeHoodExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient hood, CancellationToken cancellationToken = default)
    {
        var service = await hood.GetServiceAsync("hood", cancellationToken);
        var power = await service.GetProperty("on").GetAsync(cancellationToken);
        Console.WriteLine($"{hood.Name} 开关={power}");
        // 此设备关闭时，风速属性会返回云端错误，开启后再读取。
        if (power?.GetValue<bool>() == true)
        {
            var level = await service.GetProperty("fan-level").GetAsync(cancellationToken);
            Console.WriteLine($"风速档位={level}");
        }
    }

    public static async Task SetHighSpeedAsync(MiioDeviceClient hood, CancellationToken cancellationToken = default)
    {
        var service = await hood.GetServiceAsync("hood", cancellationToken);
        // viomi.hood.h1 的风速 0=低档、1=高档、2=爆炒。
        await service.GetProperty("fan-level").SetAsync(1, cancellationToken);
        await service.GetProperty("on").SetAsync(true, cancellationToken);
    }
}
