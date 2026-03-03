using System.Text.Json.Serialization;

namespace MiServiceSharp.Models;

public sealed class MinaDevice
{
    [JsonPropertyName("deviceID")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("alias")]
    public string Alias { get; set; } = string.Empty;

    [JsonPropertyName("current")]
    public bool Current { get; set; }

    [JsonPropertyName("presence")]
    public string Presence { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("miotDID")]
    public string MiotDid { get; set; } = string.Empty;

    [JsonPropertyName("hardware")]
    public string Hardware { get; set; } = string.Empty;

    [JsonPropertyName("romVersion")]
    public string RomVersion { get; set; } = string.Empty;

    [JsonPropertyName("romChannel")]
    public string RomChannel { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public Dictionary<string, int> Capabilities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("remoteCtrlType")]
    public string RemoteCtrlType { get; set; } = string.Empty;

    [JsonPropertyName("deviceSNProfile")]
    public string DeviceSnProfile { get; set; } = string.Empty;

    [JsonPropertyName("deviceProfile")]
    public string DeviceProfile { get; set; } = string.Empty;

    [JsonPropertyName("brokerEndpoint")]
    public string BrokerEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("brokerIndex")]
    public int BrokerIndex { get; set; }

    [JsonPropertyName("mac")]
    public string Mac { get; set; } = string.Empty;

    [JsonPropertyName("ssid")]
    public string Ssid { get; set; } = string.Empty;
}
