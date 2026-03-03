namespace MiServiceSharp.Auth;

public sealed class MiAccountOptions
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public bool EnableInteractiveVerification { get; init; } = true;

    public int VerificationMaxRetryCount { get; init; } = 20;

    public Func<string, CancellationToken, Task>? NotificationUrlHandler { get; init; }
}
