using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class FreshAirExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient freshAir, CancellationToken cancellationToken = default)
    {
        var service = await freshAir.GetServiceAsync("air-fresh", cancellationToken);
        var power = await service.GetProperty("on").GetAsync(cancellationToken);
        var environment = await freshAir.GetServiceAsync("environment", cancellationToken);
        var pm25 = await environment.GetProperty("pm2.5-density").GetAsync(cancellationToken);
        var co2 = await environment.GetProperty("co2-density").GetAsync(cancellationToken);
        Console.WriteLine($"{freshAir.Name} 开关={power}，PM2.5={pm25}，CO₂={co2} ppm");

        // dmaker.airfresh.t2017 有两个同名 filter 服务，通过 IID 分别定位。
        var primaryFilter = await freshAir.GetServiceAsync("4", cancellationToken);
        var secondaryFilter = await freshAir.GetServiceAsync("5", cancellationToken);
        var primaryLife = await primaryFilter.GetProperty("filter-life-level").GetAsync(cancellationToken);
        var secondaryLife = await secondaryFilter.GetProperty("filter-life-level").GetAsync(cancellationToken);
        Console.WriteLine($"滤芯服务 4 剩余={primaryLife}%，滤芯服务 5 剩余={secondaryLife}%");
    }

    public static async Task SetFavoriteModeAsync(MiioDeviceClient freshAir, CancellationToken cancellationToken = default)
    {
        var service = await freshAir.GetServiceAsync("air-fresh", cancellationToken);
        await service.GetProperty("mode").SetAsync(2, cancellationToken);
        var favorite = await freshAir.GetServiceAsync("air-fresh-favorite", cancellationToken);
        // 本型号枚举 6 对应风量 180，其他型号应检查各自的 value-list。
        await favorite.GetProperty("fan-level").SetAsync(6, cancellationToken);
        await service.GetProperty("on").SetAsync(true, cancellationToken);
    }
}
