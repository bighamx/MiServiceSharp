namespace MiServiceSharp.Auth;

public sealed record MiOAuthToken
{
    public required string AppId { get; init; }
    public required string RedirectUri { get; init; }
    public required string DeviceId { get; init; }
    public string Region { get; init; } = "cn";
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed record MiOAuthAuthorization(Uri AuthorizationUri, string AppId, string RedirectUri, string Region, string DeviceId, string State);
