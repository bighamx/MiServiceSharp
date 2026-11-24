using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class TemperatureHumidityExamples
{
    public static async Task ReadStateAsync(MiioDeviceClient sensor, CancellationToken cancellationToken = default)
    {
        var environment = await sensor.GetServiceAsync("temperature-humidity-sensor", cancellationToken);
        var temperature = await environment.GetProperty("temperature").GetAsync(cancellationToken);
        var humidity = await environment.GetProperty("relative-humidity").GetAsync(cancellationToken);
        Console.WriteLine($"{sensor.Name} 温度={temperature} ℃，湿度={humidity}%");
    }
}
