using System.Text.Json.Nodes;

namespace MiServiceSharp.Subscriptions;

public sealed record MiotPropertyChanged(string DeviceId, int Siid, int Piid, JsonNode? Value, DateTimeOffset ReceivedAt);

public sealed record MiotDeviceEvent(string DeviceId, int Siid, int Eiid, JsonArray Arguments, DateTimeOffset ReceivedAt);

public sealed class MiotSubscriptionErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
