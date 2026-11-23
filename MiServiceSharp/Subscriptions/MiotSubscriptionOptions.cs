namespace MiServiceSharp.Subscriptions;

public sealed record MiotSubscriptionOptions
{
    public string? BrokerHost { get; init; }
    public int Port { get; init; } = 8883;
    public bool UseTls { get; init; } = true;
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaximumReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);
    public int NotificationBufferCapacity { get; init; } = 1024;
}
