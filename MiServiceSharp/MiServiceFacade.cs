using MiServiceSharp.Auth;
using MiServiceSharp.Protocol.LocalMiio;
using MiServiceSharp.Services;
using MiServiceSharp.Subscriptions;

namespace MiServiceSharp;

public sealed class MiServiceFacade
{
    public MiServiceFacade(MiAccountClient account)
    {
        Account = account;
        Mina = new MinaService(account);
        MiioCloud = new MiioCloudService(account);
    }

    public MiAccountClient Account { get; }

    public MinaService Mina { get; }

    public MiioCloudService MiioCloud { get; }

    public MiotSubscriptionClient CreateSubscriptionClient(MiOAuthTokenProvider tokenProvider, MiotSubscriptionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        return new MiotSubscriptionClient(tokenProvider.GetTokenAsync, options);
    }

    public LocalMiioClient CreateLocalMiioClient(string host, string tokenHex, int port = 54321, int timeoutMs = 3000)
        => new(host, tokenHex, port, timeoutMs);
}
