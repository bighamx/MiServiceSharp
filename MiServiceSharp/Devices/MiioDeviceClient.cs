using System.Text.Json.Nodes;
using MiServiceSharp.Models;
using MiServiceSharp.Services;
using MiServiceSharp.Subscriptions;

namespace MiServiceSharp.Devices;

public sealed class MiioDeviceClient
{
    private readonly SemaphoreSlim _specLock = new(1, 1);
    private MiotSpecDefinition? _spec;

    internal MiioDeviceClient(MiioCloudService service, MiioDevice info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(info.Did);
        Service = service;
        Info = info;
        Did = info.Did;
    }

    internal MiioCloudService Service { get; }
    public MiioDevice Info { get; }
    public string Did { get; }
    public string Name => Info.Name;
    public string Model => Info.Model;

    public Task<IAsyncDisposable> SubscribePropertiesAsync(MiotSubscriptionClient notifications,
        Func<MiotPropertyChanged, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        => notifications.SubscribePropertiesAsync(Did, handler, cancellationToken);

    public Task<IAsyncDisposable> SubscribeEventsAsync(MiotSubscriptionClient notifications,
        Func<MiotDeviceEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
        => notifications.SubscribeEventsAsync(Did, handler, cancellationToken);

    public async Task<MiotSpecDefinition> GetSpecAsync(CancellationToken cancellationToken = default)
    {
        await _specLock.WaitAsync(cancellationToken);
        try
        {
            return _spec ??= await Service.MiotSpecDataAsync(Model, cancellationToken);
        }
        finally
        {
            _specLock.Release();
        }
    }

    public async Task<IReadOnlyList<MiotServiceClient>> GetServicesAsync(CancellationToken cancellationToken = default)
        => (await GetSpecAsync(cancellationToken)).Services.Select(service => new MiotServiceClient(this, service)).ToArray();

    public async Task<MiotServiceClient> GetServiceAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var services = await GetServicesAsync(cancellationToken);
        var matches = services.Where(item => MiotSpecType.Matches(item.Definition.Type, item.Iid, identifier)).ToArray();
        if (matches.Length != 1)
        {
            var available = string.Join(", ", services.Select(item => $"{item.Iid}={item.Name}"));
            throw new ArgumentException($"Service '{identifier}' matched {matches.Length} services on {Name}. Available services are {available}.", nameof(identifier));
        }
        return matches[0];
    }

    public async Task<JsonNode?> GetPropertyAsync(int siid, int piid, CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(siid.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        return await service.GetProperty(piid.ToString(System.Globalization.CultureInfo.InvariantCulture)).GetAsync(cancellationToken);
    }

    public async Task SetPropertyAsync(int siid, int piid, object? value, CancellationToken cancellationToken = default)
    {
        var service = await GetServiceAsync(siid.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        await service.GetProperty(piid.ToString(System.Globalization.CultureInfo.InvariantCulture)).SetAsync(value, cancellationToken);
    }
}
