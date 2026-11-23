using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiServiceSharp.Models;

public sealed class MiotSpecDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("services")]
    public List<MiotSpecService> Services { get; set; } = [];
}

public sealed class MiotSpecService
{
    [JsonIgnore]
    public string Name => MiotSpecType.GetName(Type);

    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public List<MiotSpecProperty> Properties { get; set; } = [];

    [JsonPropertyName("actions")]
    public List<MiotSpecAction> Actions { get; set; } = [];

    [JsonPropertyName("events")]
    public List<MiotSpecEvent> Events { get; set; } = [];
}

public sealed class MiotSpecProperty
{
    [JsonIgnore]
    public string Name => MiotSpecType.GetName(Type);

    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("access")]
    public List<string> Access { get; set; } = [];

    [JsonPropertyName("unit")]
    public string Unit { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public int? Source { get; set; }

    [JsonPropertyName("value-range")]
    public List<JsonElement> ValueRange { get; set; } = [];

    [JsonPropertyName("value-list")]
    public List<MiotSpecValueItem> ValueList { get; set; } = [];

    [JsonPropertyName("gatt-access")]
    public List<string> GattAccess { get; set; } = [];
}

public sealed class MiotSpecAction
{
    [JsonIgnore]
    public string Name => MiotSpecType.GetName(Type);

    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("in")]
    public List<int> In { get; set; } = [];

    [JsonPropertyName("out")]
    public List<int> Out { get; set; } = [];
}

public sealed class MiotSpecValueItem
{
    [JsonPropertyName("value")]
    public JsonElement Value { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class MiotSpecEvent
{
    [JsonIgnore]
    public string Name => MiotSpecType.GetName(Type);

    [JsonPropertyName("iid")]
    public int Iid { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public List<int> Arguments { get; set; } = [];
}

internal static class MiotSpecType
{
    public static bool Matches(string type, int iid, string identifier)
        => type == identifier || GetName(type) == identifier
            || (int.TryParse(identifier, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var selected) && selected == iid);

    public static string GetName(string type)
    {
        var parts = type.Split(':');
        return parts.Length > 3 ? parts[3] : type;
    }
}
