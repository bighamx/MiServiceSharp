using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiServiceSharp.Protocol.LocalMiio;

public sealed class LocalMiioClient : IAsyncDisposable
{
    private readonly byte[] _token;
    private readonly UdpClient _udpClient;
    private uint _deviceId;
    private uint _baseStamp;
    private DateTimeOffset _handshakeAt;
    private int _idCounter = 1;

    public LocalMiioClient(string host, string tokenHex, int port = 54321, int timeoutMs = 3000)
    {
        Host = host;
        Port = port;
        TimeoutMs = timeoutMs;
        _token = Convert.FromHexString(tokenHex);
        if (_token.Length != 16)
        {
            throw new ArgumentException("Token must be 32 hex chars (16 bytes).", nameof(tokenHex));
        }

        _udpClient = new UdpClient();
        _udpClient.Connect(host, port);
    }

    public string Host { get; }

    public int Port { get; }

    public int TimeoutMs { get; set; }

    public bool IsInitialized => _deviceId != 0;

    public async Task<(uint DeviceId, uint Stamp)> HandshakeAsync(CancellationToken cancellationToken = default)
    {
        var hello = MiioCrypto.BuildHelloPacket();
        cancellationToken.ThrowIfCancellationRequested();
        await _udpClient.SendAsync(hello, hello.Length);

        var response = await ReceiveAsync(cancellationToken);
        if (response.Buffer.Length < 32 || response.Buffer[0] != 0x21 || response.Buffer[1] != 0x31)
        {
            throw new InvalidOperationException("Invalid hello response.");
        }

        _deviceId = MiioCrypto.ReadUInt32BigEndian(response.Buffer, 8);
        _baseStamp = MiioCrypto.ReadUInt32BigEndian(response.Buffer, 12);
        _handshakeAt = DateTimeOffset.UtcNow;
        return (_deviceId, _baseStamp);
    }

    public async Task<JsonNode> SendCommandAsync(string method, JsonArray? parameters = null, CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
        {
            await HandshakeAsync(cancellationToken);
        }

        var payload = new JsonObject
        {
            ["id"] = Interlocked.Increment(ref _idCounter),
            ["method"] = method,
            ["params"] = parameters ?? new JsonArray()
        };

        var json = payload.ToJsonString();
        var encrypted = MiioCrypto.Encrypt(_token, Encoding.UTF8.GetBytes(json));
        var packet = MiioCrypto.BuildPacket(_deviceId, ComputeStamp(), _token, encrypted);
        cancellationToken.ThrowIfCancellationRequested();
        await _udpClient.SendAsync(packet, packet.Length);

        var response = await ReceiveAsync(cancellationToken);
        if (response.Buffer.Length <= 32)
        {
            throw new InvalidOperationException("No payload in local miIO response.");
        }

        var encryptedPayload = response.Buffer[32..];
        var plain = MiioCrypto.Decrypt(_token, encryptedPayload);
        return JsonNode.Parse(Encoding.UTF8.GetString(plain)) ?? throw new InvalidOperationException("Invalid JSON response.");
    }

    private uint ComputeStamp()
    {
        var deltaSeconds = (uint)Math.Max(0, (DateTimeOffset.UtcNow - _handshakeAt).TotalSeconds);
        return _baseStamp + deltaSeconds;
    }

    private async Task<UdpReceiveResult> ReceiveAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeoutMs);
        return await _udpClient.ReceiveAsync(cts.Token);
    }

    public ValueTask DisposeAsync()
    {
        _udpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
