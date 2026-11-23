using System.Globalization;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MiServiceSharp.Auth;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace MiServiceSharp.Subscriptions;

public sealed class MiotSubscriptionClient : IAsyncDisposable
{
    private readonly IMqttClient _mqtt = new MqttClientFactory().CreateMqttClient();
    private readonly Func<CancellationToken, Task<MiOAuthToken>> _tokenProvider;
    private readonly MiotSubscriptionOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, Dictionary<Guid, Listener>> _listeners = new(StringComparer.Ordinal);
    private readonly Channel<bool> _reconnect = Channel.CreateBounded<bool>(1);
    private readonly Channel<Delivery> _deliveries;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AsyncLocal<bool> _insideCallback = new();
    private readonly Task _monitorTask;
    private readonly Task _dispatchTask;
    private long _refreshAtTicks = DateTime.MaxValue.Ticks;
    private volatile bool _everConnected;
    private int _disposed;

    public MiotSubscriptionClient(Func<CancellationToken, Task<MiOAuthToken>> tokenProvider, MiotSubscriptionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
        _options = options ?? new MiotSubscriptionOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.NotificationBufferCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.Port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.Port, 65535);
        if (_options.ConnectionTimeout <= TimeSpan.Zero || _options.ReconnectDelay <= TimeSpan.Zero
            || _options.MaximumReconnectDelay < _options.ReconnectDelay
            || _options.ConnectionTimeout.TotalMilliseconds > uint.MaxValue - 1
            || _options.MaximumReconnectDelay.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentException("Connection and reconnect timeouts must be positive and ordered.", nameof(options));
        }
        _deliveries = Channel.CreateBounded<Delivery>(new BoundedChannelOptions(_options.NotificationBufferCapacity)
        {
            SingleReader = true, FullMode = BoundedChannelFullMode.Wait
        });
        _mqtt.ApplicationMessageReceivedAsync += ReceiveAsync;
        _mqtt.DisconnectedAsync += _ =>
        {
            if (_everConnected && Volatile.Read(ref _disposed) == 0) _reconnect.Writer.TryWrite(true);
            return Task.CompletedTask;
        };
        _monitorTask = MonitorAsync();
        _dispatchTask = DispatchAsync();
    }

    public bool IsConnected => _mqtt.IsConnected;
    public event EventHandler<MiotSubscriptionErrorEventArgs>? Error;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IAsyncDisposable> SubscribePropertyAsync(string deviceId, int siid, int piid,
        Func<MiotPropertyChanged, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateDeviceId(deviceId);
        ValidateIid(siid);
        ValidateIid(piid);
        return RegisterAsync($"device/{deviceId}/up/properties_changed/{siid}/{piid}",
            (message, token) => handler(new MiotPropertyChanged(message.DeviceId, message.Siid, message.Iid,
                message.Parameters["value"]?.DeepClone(), message.ReceivedAt), token), cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribePropertiesAsync(string deviceId,
        Func<MiotPropertyChanged, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateDeviceId(deviceId);
        return RegisterAsync($"device/{deviceId}/up/properties_changed/#",
            (message, token) => handler(new MiotPropertyChanged(message.DeviceId, message.Siid, message.Iid,
                message.Parameters["value"]?.DeepClone(), message.ReceivedAt), token), cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeEventAsync(string deviceId, int siid, int eiid,
        Func<MiotDeviceEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateDeviceId(deviceId);
        ValidateIid(siid);
        ValidateIid(eiid);
        return RegisterAsync($"device/{deviceId}/up/event_occured/{siid}/{eiid}",
            (message, token) => handler(new MiotDeviceEvent(message.DeviceId, message.Siid, message.Iid,
                (JsonArray)message.Parameters["arguments"]!.DeepClone(), message.ReceivedAt), token), cancellationToken);
    }

    public Task<IAsyncDisposable> SubscribeEventsAsync(string deviceId,
        Func<MiotDeviceEvent, CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateDeviceId(deviceId);
        return RegisterAsync($"device/{deviceId}/up/event_occured/#",
            (message, token) => handler(new MiotDeviceEvent(message.DeviceId, message.Siid, message.Iid,
                (JsonArray)message.Parameters["arguments"]!.DeepClone(), message.ReceivedAt), token), cancellationToken);
    }

    private async Task<IAsyncDisposable> RegisterAsync(string topic, Func<Notification, CancellationToken, Task> callback, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        var listener = new Listener(callback);
        try
        {
            ThrowIfDisposed();
            await ConnectCoreAsync(cancellationToken);
            bool newTopic;
            lock (_sync)
            {
                newTopic = !_listeners.TryGetValue(topic, out var group);
                if (newTopic) _listeners[topic] = group = [];
                group!.Add(listener.Id, listener);
            }
            try
            {
                if (newTopic) await SubscribeTopicAsync(topic, cancellationToken);
            }
            catch
            {
                if (RemoveListener(topic, listener) && _mqtt.IsConnected)
                {
                    try { await UnsubscribeTopicAsync(topic); }
                    catch (Exception error) { ReportError(error); }
                }
                throw;
            }
            return new Subscription(this, topic, listener);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken, MiOAuthToken? renewedToken = null)
    {
        if (_mqtt.IsConnected) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(_options.ConnectionTimeout);
        var token = renewedToken ?? await GetUsableTokenAsync(timeout.Token);
        var host = _options.BrokerHost ?? token.Region + "-ha.mqtt.io.mi.com";
        var builder = new MqttClientOptionsBuilder().WithClientId(token.DeviceId)
            .WithTcpServer(host, _options.Port).WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCredentials(token.AppId, token.AccessToken).WithCleanStart(true)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30)).WithTimeout(_options.ConnectionTimeout);
        if (_options.UseTls) builder.WithTlsOptions(tls => tls.UseTls());
        var result = await _mqtt.ConnectAsync(builder.Build(), timeout.Token);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException($"MQTT connection was rejected (code={result.ResultCode}).");
        }
        try
        {
            string[] topics;
            lock (_sync) topics = _listeners.Keys.ToArray();
            foreach (var topic in topics) await SubscribeTopicAsync(topic, timeout.Token);
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(_options.ConnectionTimeout);
            try { await _mqtt.DisconnectAsync(new MqttClientDisconnectOptions(), cleanup.Token); }
            catch (Exception error) { ReportError(error); }
            throw;
        }
        var remaining = token.ExpiresAt - DateTimeOffset.UtcNow;
        var refreshMargin = TimeSpan.FromTicks(Math.Min(TimeSpan.FromMinutes(2).Ticks, Math.Max(0, remaining.Ticks / 5)));
        Volatile.Write(ref _refreshAtTicks, (token.ExpiresAt.UtcDateTime - refreshMargin).Ticks);
        _everConnected = true;
        _reconnect.Writer.TryWrite(true);
    }

    private async Task SubscribeTopicAsync(string topic, CancellationToken cancellationToken)
    {
        var options = new MqttClientSubscribeOptionsBuilder().WithTopicFilter(filter =>
            filter.WithTopic(topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)).Build();
        var result = await _mqtt.SubscribeAsync(options, cancellationToken);
        if (result.Items.Count != 1 || (int)result.Items.Single().ResultCode >= 128)
        {
            throw new InvalidOperationException("The MQTT broker rejected the subscription.");
        }
    }

    private async Task<MiOAuthToken> GetUsableTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _tokenProvider(cancellationToken);
        MiOAuthClient.ValidateRegion(token.Region);
        ArgumentException.ThrowIfNullOrWhiteSpace(token.AppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(token.AccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(token.DeviceId);
        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The Xiaomi OAuth Access Token has expired.");
        }
        return token;
    }

    private async Task UnsubscribeTopicAsync(string topic)
    {
        using var timeout = new CancellationTokenSource(_options.ConnectionTimeout);
        await _mqtt.UnsubscribeAsync(new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(topic).Build(), timeout.Token);
    }

    private async Task UnregisterAsync(string topic, Listener listener)
    {
        listener.Active = false;
        if (Volatile.Read(ref _disposed) != 0) return;
        await _gate.WaitAsync();
        try
        {
            if (RemoveListener(topic, listener) && _mqtt.IsConnected && Volatile.Read(ref _disposed) == 0)
            {
                await UnsubscribeTopicAsync(topic);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool RemoveListener(string topic, Listener listener)
    {
        listener.Active = false;
        lock (_sync)
        {
            if (!_listeners.TryGetValue(topic, out var group)) return false;
            group.Remove(listener.Id);
            if (group.Count > 0) return false;
            _listeners.Remove(topic);
            return true;
        }
    }

    private Task ReceiveAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0) return Task.CompletedTask;
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var property = topic.LastIndexOf("/up/properties_changed/", StringComparison.Ordinal)
                > topic.LastIndexOf("/up/event_occured/", StringComparison.Ordinal);
            var kind = property ? "properties_changed" : "event_occured";
            var marker = $"/up/{kind}/";
            var markerIndex = topic.LastIndexOf(marker, StringComparison.Ordinal);
            if (!topic.StartsWith("device/", StringComparison.Ordinal) || markerIndex <= 7)
            {
                throw new FormatException("Unexpected Xiaomi notification topic.");
            }
            var deviceId = topic[7..markerIndex];
            var parts = topic[(markerIndex + marker.Length)..].Split('/');
            if (parts.Length != 2) throw new FormatException("Unexpected Xiaomi notification topic.");
            var siid = ParseIid(parts[0]);
            var iid = ParseIid(parts[1]);
            var body = JsonNode.Parse(args.ApplicationMessage.Payload.ToArray()) as JsonObject;
            var parameters = body?["params"] as JsonObject;
            if (parameters is null || ParseIid(parameters["siid"]?.ToString()) != siid
                || ParseIid(parameters[property ? "piid" : "eiid"]?.ToString()) != iid
                || (property ? !parameters.ContainsKey("value") : parameters["arguments"] is not JsonArray)
                || (parameters["did"] is JsonNode did && did.ToString() != deviceId))
            {
                throw new FormatException("Invalid Xiaomi notification payload.");
            }
            var message = new Notification(deviceId, siid, iid, parameters, DateTimeOffset.UtcNow);
            Listener[] listeners;
            lock (_sync)
            {
                var wildcard = $"device/{deviceId}/up/{kind}/#";
                listeners = _listeners.Where(pair => pair.Key == topic || pair.Key == wildcard)
                    .SelectMany(pair => pair.Value.Values).ToArray();
            }
            foreach (var listener in listeners)
            {
                if (listener.Active && !_deliveries.Writer.TryWrite(new Delivery(listener, message)))
                {
                    ReportError(new InvalidOperationException("The Xiaomi notification buffer is full."));
                }
            }
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
        {
            ReportError(new FormatException("An invalid Xiaomi notification was ignored."));
        }
        return Task.CompletedTask;
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (var delivery in _deliveries.Reader.ReadAllAsync(_lifetime.Token))
            {
                if (!delivery.Listener.Active) continue;
                try
                {
                    _insideCallback.Value = true;
                    await delivery.Listener.Callback(delivery.Message, _lifetime.Token);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception error) { ReportError(error); }
                finally { _insideCallback.Value = false; }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task MonitorAsync()
    {
        var delay = _options.ReconnectDelay;
        try
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var wake = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                var untilRefresh = new DateTime(Volatile.Read(ref _refreshAtTicks), DateTimeKind.Utc) - DateTime.UtcNow;
                wake.CancelAfter(untilRefresh <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromTicks(Math.Min(untilRefresh.Ticks, TimeSpan.FromDays(1).Ticks)));
                try { await _reconnect.Reader.ReadAsync(wake.Token); }
                catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested) { }
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!_everConnected) continue;
                var refresh = DateTime.UtcNow.Ticks >= Volatile.Read(ref _refreshAtTicks);
                if (_mqtt.IsConnected && !refresh) continue;
                await Task.Delay(delay, _lifetime.Token);
                await _gate.WaitAsync(_lifetime.Token);
                try
                {
                    MiOAuthToken? renewed = null;
                    if (_mqtt.IsConnected && refresh)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                        timeout.CancelAfter(_options.ConnectionTimeout);
                        renewed = await GetUsableTokenAsync(timeout.Token);
                        await _mqtt.DisconnectAsync(new MqttClientDisconnectOptions(), _lifetime.Token);
                    }
                    await ConnectCoreAsync(_lifetime.Token, renewed);
                    delay = _options.ReconnectDelay;
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    ReportError(error);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, _options.MaximumReconnectDelay.TotalMilliseconds));
                    _reconnect.Writer.TryWrite(true);
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void ReportError(Exception error)
    {
        var handlers = Error;
        if (handlers is null) return;
        foreach (EventHandler<MiotSubscriptionErrorEventArgs> handler in handlers.GetInvocationList())
        {
            try { handler(this, new MiotSubscriptionErrorEventArgs(error)); }
            catch { /* Diagnostics must not terminate MQTT reception. */ }
        }
    }

    private static int ParseIid(string? value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed : throw new FormatException("An MIoT instance ID must be a positive integer.");

    private static void ValidateIid(int value) => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);

    private static void ValidateDeviceId(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (deviceId.IndexOfAny(['+', '#', '\0']) >= 0)
            throw new ArgumentException("A device ID cannot contain MQTT wildcards or null characters.", nameof(deviceId));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _lifetime.CancelAsync();
        _deliveries.Writer.TryComplete();
        await _monitorTask;
        await _gate.WaitAsync();
        try
        {
            lock (_sync)
            {
                foreach (var listener in _listeners.Values.SelectMany(group => group.Values)) listener.Active = false;
                _listeners.Clear();
            }
            if (_mqtt.IsConnected)
            {
                using var timeout = new CancellationTokenSource(_options.ConnectionTimeout);
                try { await _mqtt.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token); }
                catch (Exception error) { ReportError(error); }
            }
            _mqtt.Dispose();
        }
        finally { _gate.Release(); }
        if (!_insideCallback.Value)
        {
            try { await _dispatchTask.WaitAsync(_options.ConnectionTimeout); }
            catch (TimeoutException error) { ReportError(error); }
        }
        _lifetime.Dispose();
    }

    private sealed class Listener(Func<Notification, CancellationToken, Task> callback)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public volatile bool Active = true;
        public Func<Notification, CancellationToken, Task> Callback { get; } = callback;
    }

    private sealed record Notification(string DeviceId, int Siid, int Iid, JsonObject Parameters, DateTimeOffset ReceivedAt);
    private sealed record Delivery(Listener Listener, Notification Message);

    private sealed class Subscription(MiotSubscriptionClient owner, string topic, Listener listener) : IAsyncDisposable
    {
        private int _disposed;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) await owner.UnregisterAsync(topic, listener);
        }
    }
}
