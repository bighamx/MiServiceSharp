using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class PlugExamples
{
    // chuangmi.plug.m3 的 switch 服务包含开关和温度。
    public static async Task ReadStateAsync(MiioDeviceClient plug, CancellationToken cancellationToken = default)
    {
        var service = await plug.GetServiceAsync("switch", cancellationToken);
        var power = await service.GetProperty("on").GetAsync(cancellationToken);
        var temperature = await service.GetProperty("temperature").GetAsync(cancellationToken);
        Console.WriteLine($"{plug.Name} 开关={power}，温度={temperature} ℃");
    }

    // zimi.powerstrip.v2 额外提供功率读取。
    public static async Task ReadPowerStripAsync(MiioDeviceClient strip, CancellationToken cancellationToken = default)
    {
        await ReadStateAsync(strip, cancellationToken);
        var consumption = await strip.GetServiceAsync("power-consumption", cancellationToken);
        var watts = await consumption.GetProperty("surge-power").GetAsync(cancellationToken);
        Console.WriteLine($"{strip.Name} 当前功率={watts} W");
    }

    public static async Task TurnOnAsync(MiioDeviceClient plug, CancellationToken cancellationToken = default)
    {
        var service = await plug.GetServiceAsync("switch", cancellationToken);
        await service.GetProperty("on").SetAsync(true, cancellationToken);
        var indicator = await plug.GetServiceAsync("indicator-light", cancellationToken);
        await indicator.GetProperty("on").SetAsync(true, cancellationToken);
    }

    // 插排的 toggle 是无参数动作，具体是否提供该动作由型号 Spec 决定。
    public static async Task TogglePowerStripAsync(MiioDeviceClient strip, CancellationToken cancellationToken = default)
    {
        var service = await strip.GetServiceAsync("switch", cancellationToken);
        var response = await service.GetAction("toggle").InvokeAsync(cancellationToken: cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }
}
