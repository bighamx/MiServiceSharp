using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class AirConditionerExamples
{
    // xiaomi.aircondition.mc6，空调、风扇和环境信息分属三个服务。
    public static async Task ReadStateAsync(MiioDeviceClient airConditioner, CancellationToken cancellationToken = default)
    {
        var cooling = await airConditioner.GetServiceAsync("air-conditioner", cancellationToken);
        var power = await cooling.GetProperty("on").GetAsync(cancellationToken);
        var mode = await cooling.GetProperty("mode").GetAsync(cancellationToken);
        var target = await cooling.GetProperty("target-temperature").GetAsync(cancellationToken);
        var environment = await airConditioner.GetServiceAsync("environment", cancellationToken);
        var temperature = await environment.GetProperty("temperature").GetAsync(cancellationToken);
        var fan = await airConditioner.GetServiceAsync("fan-control", cancellationToken);
        var speed = await fan.GetProperty("fan-level").GetAsync(cancellationToken);
        Console.WriteLine($"{airConditioner.Name} 开关={power}，模式={mode}，目标={target} ℃，室温={temperature} ℃，风速={speed}");
    }

    public static async Task SetCoolingAsync(MiioDeviceClient airConditioner, CancellationToken cancellationToken = default)
    {
        var cooling = await airConditioner.GetServiceAsync("air-conditioner", cancellationToken);
        // 此型号的制冷枚举为 2，温度范围为 16~31 ℃，步长为 0.5 ℃。
        await cooling.GetProperty("mode").SetAsync(2, cancellationToken);
        await cooling.GetProperty("target-temperature").SetAsync(26.0, cancellationToken);
        var fan = await airConditioner.GetServiceAsync("fan-control", cancellationToken);
        await fan.GetProperty("fan-level").SetAsync(0, cancellationToken);
        await fan.GetProperty("vertical-swing").SetAsync(true, cancellationToken);
        await cooling.GetProperty("on").SetAsync(true, cancellationToken);
    }
}
