using System.Text.Json.Nodes;
using MiServiceSharp.Models;
using MiServiceSharp.Services;

namespace MiServiceSharp.Devices;

public sealed class MinaDeviceClient
{
    private readonly MinaService _service;

    internal MinaDeviceClient(MinaService service, MinaDevice info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(info.DeviceId);
        _service = service;
        Info = info;
        DeviceId = info.DeviceId;
    }

    public MinaDevice Info { get; }
    public string DeviceId { get; }
    public string Name => Info.Name;
    public string MiotDid => Info.MiotDid;

    public Task<JsonNode> PauseAsync(CancellationToken cancellationToken = default)
        => _service.PlayerPauseAsync(DeviceId, cancellationToken);

    public Task<JsonNode> StopAsync(CancellationToken cancellationToken = default)
        => _service.PlayerStopAsync(DeviceId, cancellationToken);

    public Task<JsonNode> PlayAsync(CancellationToken cancellationToken = default)
        => _service.PlayerPlayAsync(DeviceId, cancellationToken);

    public Task<JsonNode> SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
        => _service.PlayerSetVolumeAsync(DeviceId, volume, cancellationToken);

    public Task<JsonNode> SpeakAsync(string text, CancellationToken cancellationToken = default)
        => _service.TextToSpeechAsync(DeviceId, text, cancellationToken);

    public Task<JsonNode> PlayUrlAsync(string url, CancellationToken cancellationToken = default)
        => _service.PlayByUrlAsync(DeviceId, url, cancellationToken: cancellationToken);

    public Task<JsonNode> SetLoopAsync(int type, CancellationToken cancellationToken = default)
        => _service.PlayerSetLoopAsync(DeviceId, type, cancellationToken);

    public async Task<JsonNode> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await _service.PlayerGetStatusAsync(DeviceId, cancellationToken);
        if (status["info"] is JsonValue info && info.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            return JsonNode.Parse(text) ?? status;
        }
        return status;
    }
}
