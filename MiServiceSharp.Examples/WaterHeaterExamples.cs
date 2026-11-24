using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class WaterHeaterExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient heater, CancellationToken cancellationToken = default)
    {
        var service = await heater.GetServiceAsync("water-heater", cancellationToken);
        var temperature = await service.GetProperty("temperature").GetAsync(cancellationToken);
        var target = await service.GetProperty("target-temperature").GetAsync(cancellationToken);
        var status = await service.GetProperty("status").GetAsync(cancellationToken);
        Console.WriteLine($"{heater.Name} 当前温度={temperature} ℃，目标温度={target} ℃，状态={status}");
    }

    public static async Task SetTargetTemperatureAsync(MiioDeviceClient heater, CancellationToken cancellationToken = default)
    {
        var service = await heater.GetServiceAsync("water-heater", cancellationToken);
        // viomi.waterheater.u3 的目标温度范围为 30~65 ℃。
        await service.GetProperty("target-temperature").SetAsync(40, cancellationToken);
    }
}
