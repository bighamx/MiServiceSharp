namespace MiServiceSharp.Examples;

internal static class ActionExamples
{
    public static async Task SpeakTextAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var speaker = devices.Single(device => device.Name == "客厅红外音箱");
        var service = await speaker.GetServiceAsync("intelligent-speaker", cancellationToken);
        var playText = service.GetAction("play-text");

        // InputProperties 定义参数顺序。该型号的 play-text 接收一个文本参数。
        object?[] arguments = ["你好，这是 MIoT 动作调用示例。"];
        var response = await playText.InvokeAsync(arguments, cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }

    public static async Task PausePlaybackAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var speaker = devices.Single(device => device.Name == "客厅红外音箱");
        var service = await speaker.GetServiceAsync("play-control", cancellationToken);
        var pause = service.GetAction("pause");

        // 无参数动作直接调用 InvokeAsync。
        var response = await pause.InvokeAsync(cancellationToken: cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }
}
