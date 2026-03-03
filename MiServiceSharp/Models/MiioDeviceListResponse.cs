using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiServiceSharp.Models;

public sealed class MiioDeviceListResponse
{
    [JsonPropertyName("list")]
    public List<MiioDevice> List { get; set; } = [];
}

public sealed class MiioResponse<T>
{
    [JsonPropertyName("result")]
    public T Result { get; set; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonIgnore]
    public T Payload => Result;
}

public sealed class MinaResponse<T>
{
    [JsonPropertyName("data")]
    public T Data { get; set; } = default!;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonIgnore]
    public T Payload => Data;
}

public sealed class MiotGetPropItem
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }
}

public sealed class MiotSetPropItem
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
}

public sealed class MiotActionResult
{
    [JsonPropertyName("code")]
    public int Code { get; set; }
}
