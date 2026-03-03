using System.Text.Json.Serialization;

namespace MiServiceSharp.Models;

public sealed class MiioDevice
{
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("longitude")]
    public string Longitude { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public string Latitude { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("pid")]
    public string Pid { get; set; } = string.Empty;

    [JsonPropertyName("localip")]
    public string LocalIp { get; set; } = string.Empty;

    [JsonPropertyName("mac")]
    public string Mac { get; set; } = string.Empty;

    [JsonPropertyName("ssid")]
    public string Ssid { get; set; } = string.Empty;

    [JsonPropertyName("bssid")]
    public string Bssid { get; set; } = string.Empty;

    [JsonPropertyName("parent_id")]
    public string ParentId { get; set; } = string.Empty;

    [JsonPropertyName("parent_model")]
    public string ParentModel { get; set; } = string.Empty;

    [JsonPropertyName("show_mode")]
    public int ShowMode { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("adminFlag")]
    public int AdminFlag { get; set; }

    [JsonPropertyName("shareFlag")]
    public int ShareFlag { get; set; }

    [JsonPropertyName("permitLevel")]
    public int PermitLevel { get; set; }

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("desc")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("desc_new")]
    public string DescNew { get; set; } = string.Empty;

    [JsonPropertyName("desc_time")]
    public List<long> DescTime { get; set; } = [];

    [JsonPropertyName("extra")]
    public MiioDeviceExtra Extra { get; set; } = new();

    [JsonPropertyName("prop")]
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("event")]
    public Dictionary<string, string> Events { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("method")]
    public List<MiioDeviceMethod> Methods { get; set; } = [];

    [JsonPropertyName("uid")]
    public long Uid { get; set; }

    [JsonPropertyName("pd_id")]
    public int PdId { get; set; }

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    [JsonPropertyName("p2p_id")]
    public string P2pId { get; set; } = string.Empty;

    [JsonPropertyName("rssi")]
    public int Rssi { get; set; }

    [JsonPropertyName("family_id")]
    public long FamilyId { get; set; }

    [JsonPropertyName("reset_flag")]
    public int ResetFlag { get; set; }

    [JsonPropertyName("internet_ip")]
    public string InternetIp { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, object?> AdditionalData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MiioDeviceExtra
{
    [JsonPropertyName("isSetPincode")]
    public int IsSetPincode { get; set; }

    [JsonPropertyName("pincodeType")]
    public int PincodeType { get; set; }

    [JsonPropertyName("fw_version")]
    public string FwVersion { get; set; } = string.Empty;

    [JsonPropertyName("needVerifyCode")]
    public int NeedVerifyCode { get; set; }

    [JsonPropertyName("isPasswordEncrypt")]
    public int IsPasswordEncrypt { get; set; }

    [JsonPropertyName("mcu_version")]
    public string McuVersion { get; set; } = string.Empty;
}

public sealed class MiioDeviceMethod
{
    [JsonPropertyName("allow_values")]
    public string AllowValues { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
