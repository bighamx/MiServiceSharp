using System.Net;
using System.Net.Sockets;
using System.Text;

namespace MiServiceSharp.Auth;

public sealed class MiOAuthCallbackListener : IAsyncDisposable
{
    private readonly MiOAuthAuthorization _authorization;
    private readonly TcpListener _listener;
    private bool _started;
    private bool _disposed;

    public MiOAuthCallbackListener(MiOAuthAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        _authorization = authorization;
        CallbackUri = new Uri(authorization.RedirectUri);
        if (CallbackUri.Scheme != Uri.UriSchemeHttp || !string.IsNullOrEmpty(CallbackUri.UserInfo))
        {
            throw new ArgumentException("Automatic OAuth callbacks require an HTTP redirect URI.", nameof(authorization));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CallbackUri.Port);

        var address = IPAddress.TryParse(CallbackUri.DnsSafeHost, out var literal)
            ? literal : CallbackUri.IsLoopback ? IPAddress.Loopback : IPAddress.IPv6Any;
        _listener = new TcpListener(address, CallbackUri.Port);
        if (address.Equals(IPAddress.IPv6Any))
        {
            _listener.Server.DualMode = true;
        }
    }

    public Uri CallbackUri { get; }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _listener.Start();
        _started = true;
    }

    public async Task<Uri> WaitForCallbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            throw new InvalidOperationException("Start the OAuth callback listener before waiting for a callback.");
        }

        while (true)
        {
            using var connection = await _listener.AcceptTcpClientAsync(cancellationToken);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var stream = connection.GetStream();
                var callback = await ReadCallbackAsync(stream, requestTimeout.Token);
                if (callback is not null)
                {
                    return callback;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A stalled connection expires while the listener continues waiting.
            }
            catch (IOException)
            {
                // Browsers can close speculative connections before sending a request.
            }
        }
    }

    private async Task<Uri?> ReadCallbackAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var length = 0;
        var headerEnd = -1;
        while (length < buffer.Length && headerEnd < 0)
        {
            var received = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (received == 0)
            {
                return null;
            }
            length += received;
            headerEnd = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
        }
        if (headerEnd < 0)
        {
            await RespondAsync(stream, 431, "Request Header Fields Too Large", "请求头过长。", cancellationToken);
            return null;
        }

        var header = Encoding.ASCII.GetString(buffer, 0, headerEnd);
        var line = header.Split("\r\n", 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (line.Length != 3 || line[0] != "GET")
        {
            await RespondAsync(stream, 405, "Method Not Allowed", "授权回调需要 GET 请求。", cancellationToken);
            return null;
        }
        if (!line[1].StartsWith('/') || line[1].StartsWith("//", StringComparison.Ordinal)
            || !Uri.TryCreate(CallbackUri, line[1], out var callback))
        {
            await RespondAsync(stream, 400, "Bad Request", "回调地址格式错误。", cancellationToken);
            return null;
        }

        Dictionary<string, string> query;
        try
        {
            query = MiOAuthClient.ValidateAuthorizationCallback(_authorization, callback);
        }
        catch (ArgumentException)
        {
            await RespondAsync(stream, 404, "Not Found", "授权回调地址不匹配。", cancellationToken);
            return null;
        }
        catch (InvalidOperationException)
        {
            await RespondAsync(stream, 403, "Forbidden", "授权状态校验失败。", cancellationToken);
            return null;
        }
        if ((!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            && !query.ContainsKey("error"))
        {
            await RespondAsync(stream, 400, "Bad Request", "回调缺少授权码。", cancellationToken);
            return null;
        }

        try
        {
            await RespondAsync(stream, 200, "OK", "授权回调已收到，请返回终端查看处理结果。", cancellationToken);
        }
        catch (IOException)
        {
            // The authorization code can still be processed if the browser closes its response stream.
        }
        return callback;
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string reason, string message,
        CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\">" +
            "<title>MiServiceSharp OAuth</title><p>" + message + "</p></html>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n" +
            "Content-Security-Policy: default-src 'none'\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _listener.Stop();
        }
        return ValueTask.CompletedTask;
    }
}
