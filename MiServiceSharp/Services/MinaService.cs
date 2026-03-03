using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp.Auth;
using MiServiceSharp.Models;

namespace MiServiceSharp.Services;

public sealed class MinaService
{
    private static readonly HashSet<string> UsePlayMusicApiHardware =
    [
        "LX04", "LX05", "L05B", "L05C", "L06", "L06A", "X08A", "X10A", "X08C", "X08E", "X8F", "X4B", "OH2", "OH2P", "X6A"
    ];

    private readonly MiAccountClient _account;
    private readonly Dictionary<string, string> _deviceHardwareMap = new(StringComparer.OrdinalIgnoreCase);

    public MinaService(MiAccountClient account)
    {
        _account = account;
    }

    public async Task<JsonNode> MinaRequestAsync(string uri, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        var payload = await MinaRequestAsync<JsonNode>(uri, data, cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException($"Mina response missing payload: {uri}");
        }

        return payload;
    }

    public async Task<T> MinaRequestAsync<T>(string uri, Dictionary<string, string>? data = null, CancellationToken cancellationToken = default)
    {
        var requestId = "app_ios_" + GenerateRandom(30);
        if (data is null)
        {
            uri += (uri.Contains('?') ? "&" : "?") + "requestId=" + requestId;
        }
        else
        {
            data["requestId"] = requestId;
        }

        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = "MiHome/6.0.103 (com.xiaomi.mihome; iOS 14.4.0)"
        };

        var raw = await _account.MiRequestAsync(
            "micoapi",
            "https://api2.mina.mi.com" + uri,
            (_, _) => data,
            headers,
            cancellationToken: cancellationToken);

        var envelope = raw.Deserialize<MinaResponse<T>>();
        if (envelope is null)
        {
            throw new InvalidOperationException($"Mina response envelope invalid: {raw}");
        }

        if (envelope.Payload is null)
        {
            throw new InvalidOperationException($"Mina response missing payload: {raw}");
        }

        return envelope.Payload;
    }

    public async Task<IReadOnlyList<MinaDevice>> DeviceListAsync(int master = 0, CancellationToken cancellationToken = default)
    {
        var devices = await MinaRequestAsync<List<MinaDevice>>($"/admin/v2/device_list?master={master}", null, cancellationToken);
        if (devices.Count == 0)
        {
            return [];
        }

        foreach (var device in devices)
        {
            if (!string.IsNullOrWhiteSpace(device.Alias))
            {
                device.Name = device.Alias;
            }

            if (!string.IsNullOrWhiteSpace(device.DeviceId) && !string.IsNullOrWhiteSpace(device.Hardware))
            {
                _deviceHardwareMap[device.DeviceId] = device.Hardware;
            }
        }

        return devices;
    }

    public Task<JsonNode> UbusRequestAsync(string deviceId, string method, string path, object message, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, string>
        {
            ["deviceId"] = deviceId,
            ["message"] = JsonSerializer.Serialize(message),
            ["method"] = method,
            ["path"] = path
        };

        return MinaRequestAsync("/remote/ubus", payload, cancellationToken);
    }

    public Task<JsonNode> TextToSpeechAsync(string deviceId, string text, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "text_to_speech", "mibrain", new { text }, cancellationToken);

    public Task<JsonNode> PlayerSetVolumeAsync(string deviceId, int volume, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "player_set_volume", "mediaplayer", new { volume, media = "app_ios" }, cancellationToken);

    public Task<JsonNode> PlayerPauseAsync(string deviceId, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "player_play_operation", "mediaplayer", new { action = "pause", media = "app_ios" }, cancellationToken);

    public Task<JsonNode> PlayerStopAsync(string deviceId, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "player_play_operation", "mediaplayer", new { action = "stop", media = "app_ios" }, cancellationToken);

    public Task<JsonNode> PlayerPlayAsync(string deviceId, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "player_play_operation", "mediaplayer", new { action = "play", media = "app_ios" }, cancellationToken);

    public Task<JsonNode> PlayerGetStatusAsync(string deviceId, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "player_get_play_status", "mediaplayer", new { media = "app_ios" }, cancellationToken);

    public Task<JsonNode> PlayerSetLoopAsync(string deviceId, int type = 1, CancellationToken cancellationToken = default)
        => UbusRequestAsync(deviceId, "player_set_loop", "mediaplayer", new { media = "common", type }, cancellationToken);

    public async Task<JsonNode> PlayByUrlAsync(string deviceId, string url, int type = 2, CancellationToken cancellationToken = default)
    {
        if (!_deviceHardwareMap.TryGetValue(deviceId, out var hardware))
        {
            await DeviceListAsync(cancellationToken: cancellationToken);
            _deviceHardwareMap.TryGetValue(deviceId, out hardware);
        }

        if (!string.IsNullOrWhiteSpace(hardware) && UsePlayMusicApiHardware.Contains(hardware))
        {
            return await PlayByMusicUrlAsync(deviceId, url, type, cancellationToken: cancellationToken);
        }

        return await UbusRequestAsync(deviceId, "player_play_url", "mediaplayer", new { url, type, media = "app_ios" }, cancellationToken);
    }

    public Task<JsonNode> PlayByMusicUrlAsync(string deviceId, string url, int type = 2, string audioId = "1582971365183456177", string id = "355454500", CancellationToken cancellationToken = default)
    {
        var audioType = type == 1 ? "MUSIC" : string.Empty;
        var music = new
        {
            payload = new
            {
                audio_type = audioType,
                audio_items = new[]
                {
                    new
                    {
                        item_id = new
                        {
                            audio_id = audioId,
                            cp = new { album_id = "-1", episode_index = 0, id, name = "xiaowei" }
                        },
                        stream = new { url }
                    }
                },
                list_params = new { listId = "-1", loadmore_offset = 0, origin = "xiaowei", type = "MUSIC" }
            },
            play_behavior = "REPLACE_ALL"
        };

        return UbusRequestAsync(
            deviceId,
            "player_play_music",
            "mediaplayer",
            new { startaudioid = audioId, music = JsonSerializer.Serialize(music) },
            cancellationToken);
    }

    private static string GenerateRandom(int length)
    {
        const string source = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = Random.Shared;
        return new string(Enumerable.Range(0, length).Select(_ => source[random.Next(source.Length)]).ToArray());
    }
}
