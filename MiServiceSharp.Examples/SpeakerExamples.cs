namespace MiServiceSharp.Examples;

internal static class SpeakerExamples
{
    public static async Task StatusAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var speakers = await client.Mina.GetDevicesAsync(cancellationToken);
        var speaker = speakers.Single(device => device.Name == "客厅红外音箱");
        var status = await speaker.GetStatusAsync(cancellationToken);
        Console.WriteLine($"{speaker.Name} {status.ToJsonString()}");
    }

    public static async Task SetVolumeAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var speakers = await client.Mina.GetDevicesAsync(cancellationToken);
        var speaker = speakers.Single(device => device.Name == "客厅红外音箱");

        const int volume = 40;
        await speaker.SetVolumeAsync(volume, cancellationToken);
        var status = await speaker.GetStatusAsync(cancellationToken);
        Console.WriteLine($"{speaker.Name} {status.ToJsonString()}");
    }

    public static async Task SpeakAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var speakers = await client.Mina.GetDevicesAsync(cancellationToken);
        var speaker = speakers.Single(device => device.Name == "客厅红外音箱");

        var response = await speaker.SpeakAsync("你好，这是 MiNA 语音播报示例。", cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }
}
