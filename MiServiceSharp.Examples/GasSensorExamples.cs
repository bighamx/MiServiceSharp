using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class GasSensorExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient sensor, CancellationToken cancellationToken = default)
    {
        var service = await sensor.GetServiceAsync("gas-sensor", cancellationToken);
        var status = await service.GetProperty("status").GetAsync(cancellationToken);
        var concentration = await service.GetProperty("gas-concentration").GetAsync(cancellationToken);
        // 本型号状态 1 为 Normal，具体枚举可查看属性 Definition.ValueList。
        Console.WriteLine($"{sensor.Name} 状态={status}，浓度={concentration}");
    }
}
