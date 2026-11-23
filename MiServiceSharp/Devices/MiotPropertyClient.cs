using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp.Models;
using MiServiceSharp.Subscriptions;

namespace MiServiceSharp.Devices;

public sealed class MiotPropertyClient
{
    private readonly MiioDeviceClient _device;
    private readonly int _siid;

    internal MiotPropertyClient(MiioDeviceClient device, int siid, MiotSpecProperty definition)
    {
        _device = device;
        _siid = siid;
        Definition = definition;
        Iid = definition.Iid;
    }

    public int Iid { get; }
    public string Name => Definition.Name;
    public MiotSpecProperty Definition { get; }
    public bool CanRead => Definition.Access.Contains("read");
    public bool CanWrite => Definition.Access.Contains("write");
    public bool CanNotify => Definition.Access.Contains("notify");

    public Task<IAsyncDisposable> SubscribeAsync(MiotSubscriptionClient notifications,
        Func<MiotPropertyChanged, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        if (!CanNotify)
        {
            throw new NotSupportedException($"Property {Name} does not support notifications.");
        }
        return notifications.SubscribePropertyAsync(_device.Did, _siid, Iid, handler, cancellationToken);
    }

    public Task<JsonNode?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (!CanRead)
        {
            throw new NotSupportedException($"Property {Name} does not support reading.");
        }
        return _device.Service.MiotGetValueAsync(_device.Did, _siid, Iid, cancellationToken);
    }

    public async Task SetAsync(object? value, CancellationToken cancellationToken = default)
    {
        if (!CanWrite)
        {
            throw new NotSupportedException($"Property {Name} does not support writing.");
        }
        MiotValueValidator.Validate(Definition, JsonSerializer.SerializeToNode(value));
        var code = await _device.Service.MiotSetPropAsync(_device.Did, (_siid, Iid), value, cancellationToken);
        if (code != 0)
        {
            throw new InvalidOperationException($"MIoT property write failed (code={code}).");
        }
    }
}
