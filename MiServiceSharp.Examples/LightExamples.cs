using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class LightExamples
{
    // yeelink.light.ceiling5，服务 light，亮度 1~100，色温 2700~5700 K。
    public static async Task ReadStateAsync(MiioDeviceClient light, CancellationToken cancellationToken = default)
    {
        var service = await light.GetServiceAsync("light", cancellationToken);
        var power = await service.GetProperty("on").GetAsync(cancellationToken);
        var brightness = await service.GetProperty("brightness").GetAsync(cancellationToken);
        var colorTemperature = await service.GetProperty("color-temperature").GetAsync(cancellationToken);
        Console.WriteLine($"{light.Name} 开关={power}，亮度={brightness}%，色温={colorTemperature} K");
    }

    public static async Task SetLightingAsync(MiioDeviceClient light, CancellationToken cancellationToken = default)
    {
        var service = await light.GetServiceAsync("light", cancellationToken);
        await service.GetProperty("on").SetAsync(true, cancellationToken);
        await service.GetProperty("brightness").SetAsync(50, cancellationToken);
        await service.GetProperty("color-temperature").SetAsync(4000, cancellationToken);
        await ReadStateAsync(light, cancellationToken);
    }

    public static async Task ToggleAsync(MiioDeviceClient light, CancellationToken cancellationToken = default)
    {
        var service = await light.GetServiceAsync("light", cancellationToken);
        var toggle = service.GetAction("toggle");
        var response = await toggle.InvokeAsync(cancellationToken: cancellationToken);
        Console.WriteLine($"切换灯开关 {response.ToJsonString()}");
    }
}
