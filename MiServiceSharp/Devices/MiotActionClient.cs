using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp.Models;

namespace MiServiceSharp.Devices;

public sealed class MiotActionClient
{
    private readonly MiioDeviceClient _device;
    private readonly int _siid;

    internal MiotActionClient(MiioDeviceClient device, MiotSpecService service, MiotSpecAction definition)
    {
        _device = device;
        _siid = service.Iid;
        Iid = definition.Iid;
        Definition = definition;
        InputProperties = definition.In.Select(iid => service.Properties.Single(property => property.Iid == iid)).ToArray();
    }

    public int Iid { get; }
    public string Name => Definition.Name;
    public MiotSpecAction Definition { get; }
    public IReadOnlyList<MiotSpecProperty> InputProperties { get; }

    public Task<JsonNode> InvokeAsync(IEnumerable<object?>? arguments = null, CancellationToken cancellationToken = default)
    {
        var values = arguments?.ToArray() ?? [];
        if (values.Length != InputProperties.Count)
        {
            throw new ArgumentException($"Action {Name} requires {InputProperties.Count} arguments.", nameof(arguments));
        }
        for (var index = 0; index < values.Length; index++)
        {
            MiotValueValidator.Validate(InputProperties[index], JsonSerializer.SerializeToNode(values[index]));
        }
        return _device.Service.InvokeMiotActionAsync(_device.Did, _siid, Iid, values, cancellationToken);
    }
}
