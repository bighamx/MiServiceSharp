using System.Text.Json.Serialization;

namespace MiServiceSharp.Models;

public sealed class MiTokenBundle
{
    public string DeviceId { get; set; } = string.Empty;

    public long UserId { get; set; } 

    public string PassToken { get; set; } = string.Empty;

    public Dictionary<string, ServiceCredential> Services { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsLoggedIn => (UserId>0) && !string.IsNullOrWhiteSpace(PassToken);
}
