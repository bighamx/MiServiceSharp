using MiServiceSharp.Devices;

namespace MiServiceSharp.Examples;

internal static class DeviceExamples
{
    public static async Task<IReadOnlyList<MiioDeviceClient>> ListAsync(MiServiceFacade client, CancellationToken cancellationToken = default)
    {
        var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken);
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Name} | did={device.Did} | {device.Model} | online={device.Info.IsOnline}");
        }

        var speakers = await client.Mina.GetDevicesAsync(cancellationToken);
        foreach (var speaker in speakers)
        {
            Console.WriteLine($"{speaker.Name} | deviceId={speaker.DeviceId} | miotDid={speaker.MiotDid}");
        }
        return devices;
    }

    public static async Task DescribeAsync(MiioDeviceClient device, CancellationToken cancellationToken = default)
    {
        // 按设备自身型号加载 Spec，枚举属性、动作与事件。
        Console.WriteLine($"{device.Name} | {device.Model}");
        foreach (var service in await device.GetServicesAsync(cancellationToken))
        {
            Console.WriteLine($"Service {service.Iid} {service.Name}");
            foreach (var property in service.Properties)
            {
                Console.WriteLine($"  Property {property.Iid} {property.Name} | {property.Definition.Format} " +
                    $"| R={property.CanRead} W={property.CanWrite} N={property.CanNotify}");
            }
            foreach (var action in service.Actions)
            {
                var inputs = string.Join(", ", action.InputProperties.Select(property => property.Name));
                var outputs = string.Join(", ", action.Definition.Out);
                Console.WriteLine($"  Action {action.Iid} {action.Name} | inputs=[{inputs}] | outputIids=[{outputs}]");
            }
            foreach (var occurrence in service.Definition.Events)
            {
                Console.WriteLine($"  Event {occurrence.Iid} {occurrence.Type}");
            }
        }
    }
}
