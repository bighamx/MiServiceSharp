using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class GatewayExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient gateway, IReadOnlyList<MiioDeviceClient> devices,
        CancellationToken cancellationToken = default)
    {
        var service = await gateway.GetServiceAsync("gateway", cancellationToken);
        var guardMode = await service.GetProperty("guard-mode").GetAsync(cancellationToken);
        Console.WriteLine($"{gateway.Name} 布防状态={guardMode}");

        // 子设备通过自己的 DID 调用云端接口，通过 ParentId 可以查找其所属网关。
        foreach (var child in devices.Where(device => device.Info.ParentId == gateway.Did))
        {
            Console.WriteLine($"  子设备 {child.Name} | {child.Model} | did={child.Did}");
        }
    }

    public static async Task EnableGuardModeAsync(MiioDeviceClient gateway, CancellationToken cancellationToken = default)
    {
        var service = await gateway.GetServiceAsync("gateway", cancellationToken);
        await service.GetProperty("guard-mode").SetAsync(true, cancellationToken);
    }
}
