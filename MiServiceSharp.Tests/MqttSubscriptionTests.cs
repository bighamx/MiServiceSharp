using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MiServiceSharp.Subscriptions;
using MiServiceSharp.Auth;
using MiServiceSharp.Services;
using MQTTnet;
using MQTTnet.Server;

namespace MiServiceSharp.Tests;

public sealed class MqttSubscriptionTests
{
    [Fact]
    public async Task PropertyPushesAreRoutedAndDuplicateListenersShareBrokerSubscription()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        var first = Channel.CreateUnbounded<MiotPropertyChanged>();
        var second = Channel.CreateUnbounded<MiotPropertyChanged>();
        await using var firstHandle = await client.SubscribePropertyAsync("test-device", 2, 1,
            (message, _) => { first.Writer.TryWrite(message); return Task.CompletedTask; });
        await using var secondHandle = await client.SubscribePropertyAsync("test-device", 2, 1,
            (message, _) => { second.Writer.TryWrite(message); return Task.CompletedTask; });
        Assert.Equal(1, broker.SubscribeCount);
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"did":"test-device","siid":2,"piid":1,"value":null}}
            """);
        Assert.Null((await ReadAsync(first.Reader)).Value);
        Assert.Null((await ReadAsync(second.Reader)).Value);
        await firstHandle.DisposeAsync();
        Assert.Equal(0, broker.UnsubscribeCount);
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":42}}
            """);
        Assert.Equal(42, (await ReadAsync(second.Reader)).Value!.GetValue<int>());
        Assert.False(first.Reader.TryRead(out _));
        await secondHandle.DisposeAsync();
        Assert.Equal(1, broker.UnsubscribeCount);
    }

    [Fact]
    public async Task WildcardDeviceIdWithSlashAndEventNotificationsAreSupported()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        var properties = Channel.CreateUnbounded<MiotPropertyChanged>();
        var events = Channel.CreateUnbounded<MiotDeviceEvent>();
        await using var propertyHandle = await client.SubscribePropertiesAsync("huami.12/34",
            (message, _) => { properties.Writer.TryWrite(message); return Task.CompletedTask; });
        await using var eventHandle = await client.SubscribeEventsAsync("test-device",
            (message, _) => { events.Writer.TryWrite(message); return Task.CompletedTask; });
        await broker.PublishAsync("device/huami.12/34/up/properties_changed/8/9", """
            {"params":{"siid":8,"piid":9,"value":true}}
            """);
        var property = await ReadAsync(properties.Reader);
        Assert.Equal("huami.12/34", property.DeviceId);
        Assert.True(property.Value!.GetValue<bool>());
        await broker.PublishAsync("device/test-device/up/event_occured/3/4", """
            {"params":{"siid":3,"eiid":4,"arguments":[{"piid":1,"value":"opened"}]}}
            """);
        var occurrence = await ReadAsync(events.Reader);
        Assert.Equal(4, occurrence.Eiid);
        Assert.Equal("opened", occurrence.Arguments[0]!["value"]!.GetValue<string>());
    }

    [Fact]
    public async Task InvalidFramesAndFailingCallbacksDoNotStopReception()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        var messages = Channel.CreateUnbounded<MiotPropertyChanged>();
        var errors = Channel.CreateUnbounded<Exception>();
        client.Error += (_, error) => errors.Writer.TryWrite(error.Exception);
        await using var failing = await client.SubscribePropertyAsync("test-device", 2, 1,
            (_, _) => throw new InvalidOperationException("test callback failed"));
        await using var working = await client.SubscribePropertyAsync("test-device", 2, 1,
            (message, _) => { messages.Writer.TryWrite(message); return Task.CompletedTask; });
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", "not json");
        Assert.IsType<FormatException>(await ReadAsync(errors.Reader));
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"did":"different-device","siid":2,"piid":1,"value":1}}
            """);
        Assert.IsType<FormatException>(await ReadAsync(errors.Reader));
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":7}}
            """);
        Assert.Equal(7, (await ReadAsync(messages.Reader)).Value!.GetValue<int>());
        Assert.IsType<InvalidOperationException>(await ReadAsync(errors.Reader));
    }

    [Fact]
    public async Task BrokerDisconnectReconnectsAndRestoresSubscriptions()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        var messages = Channel.CreateUnbounded<MiotPropertyChanged>();
        await using var handle = await client.SubscribePropertyAsync("test-device", 2, 1,
            (message, _) => { messages.Writer.TryWrite(message); return Task.CompletedTask; });
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.Server.ClientSubscribedTopicAsync += _ =>
        {
            restored.TrySetResult();
            return Task.CompletedTask;
        };
        var connection = Assert.Single(await broker.Server.GetClientsAsync());
        await connection.DisconnectAsync();
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(client.IsConnected);
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":88}}
            """);
        Assert.Equal(88, (await ReadAsync(messages.Reader)).Value!.GetValue<int>());
    }

    [Fact]
    public async Task CallbackCanUnsubscribeItselfAndAddAnotherSubscription()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IAsyncDisposable? handle = null;
        IAsyncDisposable? added = null;
        handle = await client.SubscribePropertyAsync("test-device", 2, 1, async (_, token) =>
        {
            await handle!.DisposeAsync();
            added = await client.SubscribePropertyAsync("test-device", 2, 2, (_, _) => Task.CompletedTask, token);
            finished.TrySetResult();
        });
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":1}}
            """);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(2, broker.SubscribeCount);
        Assert.Equal(1, broker.UnsubscribeCount);
        await added!.DisposeAsync();
    }

    [Fact]
    public async Task ClientCanBeDisposedFromInsideNotificationCallback()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handle = await client.SubscribePropertyAsync("test-device", 2, 1, async (_, _) =>
        {
            await client.DisposeAsync();
            finished.TrySetResult();
        });
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":1}}
            """);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task RetainedPushIsDeliveredWhenSubscriptionIsCreated()
    {
        await using var broker = await Broker.StartAsync();
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":99}}
            """, retain: true);
        await using var client = broker.CreateClient();
        var first = new TaskCompletionSource<MiotPropertyChanged>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var handle = await client.SubscribePropertyAsync("test-device", 2, 1,
            (message, _) => { first.TrySetResult(message); return Task.CompletedTask; });
        Assert.Equal(99, (await first.Task.WaitAsync(TimeSpan.FromSeconds(8))).Value!.GetValue<int>());
    }

    [Fact]
    public async Task TokenRenewalReconnectsUsingFreshCredentials()
    {
        await using var broker = await Broker.StartAsync();
        var passwords = new ConcurrentQueue<string>();
        broker.Server.ValidatingConnectionAsync += args =>
        {
            passwords.Enqueue(args.Password);
            return Task.CompletedTask;
        };
        var token = OAuthTests.Token();
        var reads = 0;
        await using var client = broker.CreateClient(_ => Task.FromResult(Interlocked.Increment(ref reads) == 1
            ? token with { ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(800) }
            : token with { AccessToken = "renewed-access" }));
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.Server.ClientSubscribedTopicAsync += _ =>
        {
            if (broker.SubscribeCount > 1) restored.TrySetResult();
            return Task.CompletedTask;
        };
        await using var handle = await client.SubscribePropertyAsync("test-device", 2, 1, (_, _) => Task.CompletedTask);
        await restored.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Contains("renewed-access", passwords);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task RefreshFailureKeepsCurrentConnectionReceivingMessages()
    {
        await using var broker = await Broker.StartAsync();
        var reads = 0;
        var token = OAuthTests.Token();
        await using var client = broker.CreateClient(_ => Interlocked.Increment(ref reads) == 1
            ? Task.FromResult(token with { ExpiresAt = DateTimeOffset.UtcNow.AddMilliseconds(800) })
            : throw new InvalidOperationException("test refresh failed"));
        var error = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Error += (_, _) => error.TrySetResult();
        var messages = Channel.CreateUnbounded<MiotPropertyChanged>();
        await using var handle = await client.SubscribePropertyAsync("test-device", 2, 1,
            (message, _) => { messages.Writer.TryWrite(message); return Task.CompletedTask; });
        await error.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(client.IsConnected);
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", """
            {"params":{"siid":2,"piid":1,"value":43}}
            """);
        Assert.Equal(43, (await ReadAsync(messages.Reader)).Value!.GetValue<int>());
    }

    [Fact]
    public async Task BoundPropertySubscribesUsingItsDeviceAndModelIds()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient();
        using var http = new HttpClient(new TestHttp((request, _) => Task.FromResult(TestHttp.Json(
            request.Method == HttpMethod.Get ? """
                {"services":[{"iid":9,"type":"urn:miot-spec-v2:service:speaker:test","properties":[
                  {"iid":7,"type":"urn:miot-spec-v2:property:volume:test","format":"uint8","access":["read","notify"]},
                  {"iid":8,"type":"urn:miot-spec-v2:property:read-only:test","format":"bool","access":["read"]}
                ]}]}
                """ : """
                {"code":0,"result":{"list":[{"did":"test-device","name":"Test","model":"urn:miot-spec-v2:device:speaker:test"}]}}
                """))));
        var cloud = new MiioCloudService(new MiAccountClient(http, new MiAccountOptions(), new MemoryTokenStore()), specHttpClient: http);
        var device = Assert.Single(await cloud.GetDevicesAsync());
        var service = await device.GetServiceAsync("speaker");
        var messages = Channel.CreateUnbounded<MiotPropertyChanged>();
        await using var handle = await service.GetProperty("volume").SubscribeAsync(client,
            (message, _) => { messages.Writer.TryWrite(message); return Task.CompletedTask; });
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetProperty("read-only").SubscribeAsync(client, (_, _) => Task.CompletedTask));
        await broker.PublishAsync("device/test-device/up/properties_changed/9/7", """
            {"params":{"siid":9,"piid":7,"value":35}}
            """);
        var change = await ReadAsync(messages.Reader);
        Assert.Equal(9, change.Siid);
        Assert.Equal(7, change.Piid);
        Assert.Equal(35, change.Value!.GetValue<int>());
        Assert.Equal(1, broker.SubscribeCount);
    }

    [Fact]
    public async Task FullBufferReportsErrorAndReceptionContinues()
    {
        await using var broker = await Broker.StartAsync();
        await using var client = broker.CreateClient(bufferCapacity: 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var values = Channel.CreateUnbounded<int>();
        client.Error += (_, args) => error.TrySetResult(args.Exception);
        await using var handle = await client.SubscribePropertyAsync("test-device", 2, 1, async (change, cancellationToken) =>
        {
            var value = change.Value!.GetValue<int>();
            if (value == 0)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            values.Writer.TryWrite(value);
        });
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", "{\"params\":{\"siid\":2,\"piid\":1,\"value\":0}}");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        for (var value = 1; value <= 2; value++)
            await broker.PublishAsync("device/test-device/up/properties_changed/2/1", $"{{\"params\":{{\"siid\":2,\"piid\":1,\"value\":{value}}}}}");
        Assert.Contains("buffer is full", (await error.Task.WaitAsync(TimeSpan.FromSeconds(8))).Message);
        release.TrySetResult();
        Assert.Equal(0, await ReadAsync(values.Reader));
        Assert.Equal(1, await ReadAsync(values.Reader));
        await broker.PublishAsync("device/test-device/up/properties_changed/2/1", "{\"params\":{\"siid\":2,\"piid\":1,\"value\":3}}");
        Assert.Equal(3, await ReadAsync(values.Reader));
    }

    private static async Task<T> ReadAsync<T>(ChannelReader<T> reader)
        => await reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(8));

    private sealed class Broker : IAsyncDisposable
    {
        public required MqttServer Server { get; init; }
        public required int Port { get; init; }
        private int _subscribeCount;
        private int _unsubscribeCount;
        public int SubscribeCount => Volatile.Read(ref _subscribeCount);
        public int UnsubscribeCount => Volatile.Read(ref _unsubscribeCount);

        public static async Task<Broker> StartAsync()
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
                .WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointPort(port).Build());
            var broker = new Broker { Server = server, Port = port };
            server.ClientSubscribedTopicAsync += _ => { Interlocked.Increment(ref broker._subscribeCount); return Task.CompletedTask; };
            server.ClientUnsubscribedTopicAsync += _ => { Interlocked.Increment(ref broker._unsubscribeCount); return Task.CompletedTask; };
            await server.StartAsync();
            return broker;
        }

        public MiotSubscriptionClient CreateClient(Func<CancellationToken, Task<MiOAuthToken>>? provider = null, int bufferCapacity = 1024)
        {
            var token = OAuthTests.Token();
            return new MiotSubscriptionClient(provider ?? (_ => Task.FromResult(token)), new MiotSubscriptionOptions
            {
                BrokerHost = "127.0.0.1",
                Port = Port,
                UseTls = false,
                ReconnectDelay = TimeSpan.FromMilliseconds(40),
                MaximumReconnectDelay = TimeSpan.FromMilliseconds(200),
                ConnectionTimeout = TimeSpan.FromSeconds(3),
                NotificationBufferCapacity = bufferCapacity
            });
        }

        public Task PublishAsync(string topic, string payload, bool retain = false)
            => Server.InjectApplicationMessage(new InjectedMqttApplicationMessage(new MqttApplicationMessageBuilder()
                .WithTopic(topic).WithPayload(payload).WithRetainFlag(retain).Build())
            { SenderClientId = "test-publisher" });

        public async ValueTask DisposeAsync()
        {
            await Server.StopAsync();
            Server.Dispose();
        }
    }
}
