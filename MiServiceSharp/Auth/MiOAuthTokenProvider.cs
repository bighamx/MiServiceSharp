using MiServiceSharp.Storage;

namespace MiServiceSharp.Auth;

public sealed class MiOAuthTokenProvider(MiOAuthClient client, FileMiOAuthTokenStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<MiOAuthToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var token = await store.LoadAsync(cancellationToken)
                ?? throw new InvalidOperationException("Xiaomi OAuth credentials are required. Run oauth-login <app-id> <registered-redirect-uri> first.");
            if (token.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2))
            {
                token = await client.RefreshAsync(token, cancellationToken);
                await store.SaveAsync(token, cancellationToken);
            }
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
