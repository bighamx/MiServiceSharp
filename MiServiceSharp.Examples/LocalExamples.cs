using System.Text.Json.Nodes;
using MiServiceSharp.Protocol.LocalMiio;

namespace MiServiceSharp.Examples;

internal static class LocalExamples
{
    public static async Task InfoAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        // 从账号设备清单获取 IP 和 Token，再直接访问该设备。
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var light = devices.Single(device => device.Name == "客厅灯");
        await using var local = new LocalMiioClient(light.Info.LocalIp, light.Info.Token);
        var response = await local.SendCommandAsync("miIO.info", cancellationToken: cancellationToken);

        // 设备信息可能包含凭证，这里选择输出型号与固件字段。
        Console.WriteLine($"型号 = {response["result"]?["model"]}");
        Console.WriteLine($"固件 = {response["result"]?["fw_ver"]}");
        Console.WriteLine($"错误 = {response["error"]}");
    }

    public static async Task ReadMiotAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var purifier = devices.Single(device => device.Name == "空净");
        await using var local = new LocalMiioClient(purifier.Info.LocalIp, purifier.Info.Token);

        // zhimi.airpurifier.va1 的开关为 SIID=2、PIID=2，以该型号 Spec 为准。
        // 本地请求的 did 用于在响应中对应查询项。
        var parameters = new JsonArray(new JsonObject
        {
            ["did"] = "2-2",
            ["siid"] = 2,
            ["piid"] = 2
        });
        var response = await local.SendCommandAsync("get_properties", parameters, cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }

    public static async Task ReadLegacyPowerAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        var light = devices.Single(device => device.Name == "客厅灯");
        await using var local = new LocalMiioClient(light.Info.LocalIp, light.Info.Token);

        // 该灯的局域网接口使用传统 miIO 的 get_prop 和字符串属性名。
        var response = await local.SendCommandAsync("get_prop", new JsonArray("power"), cancellationToken);
        Console.WriteLine(response.ToJsonString());
    }
}
