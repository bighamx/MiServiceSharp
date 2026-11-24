using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class RobotVacuumExamples
{
    // viomi.vacuum.v7。
    public static async Task ReadStateAsync(MiioDeviceClient robot, CancellationToken cancellationToken = default)
    {
        var vacuum = await robot.GetServiceAsync("vacuum", cancellationToken);
        var status = await vacuum.GetProperty("status").GetAsync(cancellationToken);
        var mode = await vacuum.GetProperty("mode").GetAsync(cancellationToken);
        var battery = await robot.GetServiceAsync("battery", cancellationToken);
        var level = await battery.GetProperty("battery-level").GetAsync(cancellationToken);
        Console.WriteLine($"{robot.Name} 状态={status}，清扫模式={mode}，电量={level}%");
    }

    public static async Task StartCleaningAsync(MiioDeviceClient robot, CancellationToken cancellationToken = default)
    {
        var vacuum = await robot.GetServiceAsync("vacuum", cancellationToken);
        await vacuum.GetProperty("mode").SetAsync(1, cancellationToken);
        var response = await vacuum.GetAction("start-sweep").InvokeAsync(cancellationToken: cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }

    public static async Task PauseCleaningAsync(MiioDeviceClient robot, CancellationToken cancellationToken = default)
    {
        var vacuum = await robot.GetServiceAsync("vacuum", cancellationToken);
        var response = await vacuum.GetAction("pause-sweeping").InvokeAsync(cancellationToken: cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }

    public static async Task ReturnToDockAsync(MiioDeviceClient robot, CancellationToken cancellationToken = default)
    {
        var battery = await robot.GetServiceAsync("battery", cancellationToken);
        var response = await battery.GetAction("start-charge").InvokeAsync(cancellationToken: cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }
}
