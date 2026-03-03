using MiServiceSharp.Auth;
using MiServiceSharp.Protocol.LocalMiio;
using MiServiceSharp.Services;

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

    public LocalMiioClient CreateLocalMiioClient(string host, string tokenHex, int port = 54321, int timeoutMs = 3000)
        => new(host, tokenHex, port, timeoutMs);
}
