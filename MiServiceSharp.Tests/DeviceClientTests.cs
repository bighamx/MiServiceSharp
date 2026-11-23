using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using MiServiceSharp.Auth;
using MiServiceSharp.Services;

namespace MiServiceSharp.Tests;

public sealed class DeviceClientTests
{
    [Fact]
    public async Task SpeakerObjectsKeepTheirOwnDeviceIdsDuringConcurrentOperations()
    {
        var sent = new ConcurrentQueue<(string Device, string Method)>();
        using var http = new HttpClient(new TestHttp(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return TestHttp.Json("""
                    {"code":0,"data":[{"deviceID":"a","name":"First"},{"deviceID":"b","name":"Second"}]}
                    """);
            }
            var form = await TestHttp.FormAsync(request);
            sent.Enqueue((form["deviceId"], form["method"]));
            return TestHttp.Json("{\"code\":0,\"data\":{\"code\":0}}");
        }));
        var account = new MiAccountClient(http, new MiAccountOptions(), new MemoryTokenStore());
        var devices = await new MinaService(account).GetDevicesAsync();
        devices[0].Info.DeviceId = "modified-metadata";
        await Task.WhenAll(devices[0].PauseAsync(), devices[1].StopAsync());
        Assert.Contains(("a", "player_play_operation"), sent);
        Assert.Contains(("b", "player_play_operation"), sent);
        Assert.DoesNotContain(sent, item => item.Device == "modified-metadata");
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => devices[0].SetVolumeAsync(101));
        Assert.Equal(2, sent.Count);
    }

    [Fact]
    public async Task SameCategoryModelsUseTheirOwnSpecIds()
    {
        using var fixture = new MiotFixture();
        var devices = await fixture.Service.GetDevicesAsync();
        foreach (var device in devices)
        {
            var service = (await device.GetServicesAsync()).Single(item => item.Name == "speaker");
            var property = service.Properties.Single(item => item.Name == "volume");
            Assert.Equal(55, (await property.GetAsync())!.GetValue<int>());
            await property.SetAsync(50);
        }
        Assert.Contains(fixture.Requests, node => node["did"]!.GetValue<string>() == "a" && node["siid"]!.GetValue<int>() == 9 && node["piid"]!.GetValue<int>() == 7);
        Assert.Contains(fixture.Requests, node => node["did"]!.GetValue<string>() == "b" && node["siid"]!.GetValue<int>() == 22 && node["piid"]!.GetValue<int>() == 3);
        Assert.Equal(2, fixture.SpecRequests);
    }

    [Fact]
    public async Task PropertyPermissionsTypesRangesAndEnumsAreValidatedBeforeSending()
    {
        using var fixture = new MiotFixture();
        var device = (await fixture.Service.GetDevicesAsync())[0];
        var service = (await device.GetServicesAsync()).Single();
        var volume = service.Properties.Single(item => item.Name == "volume");
        await Assert.ThrowsAsync<ArgumentException>(() => volume.SetAsync(true));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => volume.SetAsync(101));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => volume.SetAsync(10.5));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => volume.SetAsync(52));
        await Assert.ThrowsAsync<ArgumentException>(() => service.Properties.Single(item => item.Name == "mode").SetAsync(1));
        await Assert.ThrowsAsync<NotSupportedException>(() => service.Properties.Single(item => item.Name == "read-only").SetAsync(0));
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task ActionUsesModelDefinitionAndKeepsOutputValues()
    {
        using var fixture = new MiotFixture();
        var device = (await fixture.Service.GetDevicesAsync())[0];
        var action = (await device.GetServicesAsync()).Single().Actions.Single();
        await Assert.ThrowsAsync<ArgumentException>(() => action.InvokeAsync());
        await Assert.ThrowsAsync<ArgumentException>(() => action.InvokeAsync([123]));
        var result = await action.InvokeAsync(["test"]);
        Assert.Equal("ok", result["out"]![0]!.GetValue<string>());
        var sent = Assert.Single(fixture.Requests);
        Assert.Equal(9, sent["siid"]!.GetValue<int>());
        Assert.Equal(11, sent["aiid"]!.GetValue<int>());
        Assert.Equal("test", sent["in"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task FailedSpecFetchCanRetryAndSuccessfulSpecIsCached()
    {
        using var fixture = new MiotFixture { FailFirstSpec = true };
        var device = (await fixture.Service.GetDevicesAsync())[0];
        await Assert.ThrowsAsync<HttpRequestException>(() => device.GetSpecAsync());
        var spec = await device.GetSpecAsync();
        Assert.Same(spec, await device.GetSpecAsync());
        Assert.Equal(2, fixture.SpecRequests);
    }

    [Fact]
    public async Task LowLevelNullPropertyValueRemainsJsonNull()
    {
        using var fixture = new MiotFixture();
        Assert.Equal(0, await fixture.Service.MiotSetPropAsync("a", (9, 7), (object?)null));
        Assert.Null(Assert.Single(fixture.Requests)["value"]);
    }

    [Fact]
    public async Task CapabilitySelectorsAcceptIdsNamesAndFullUrns()
    {
        using var fixture = new MiotFixture();
        var device = (await fixture.Service.GetDevicesAsync())[0];
        var service = await device.GetServiceAsync("9");
        Assert.Equal("speaker", service.Name);
        Assert.True(service.GetProperty("volume").CanNotify);
        Assert.Equal(7, service.GetProperty("7").Iid);
        Assert.Equal(7, service.GetProperty("volume").Iid);
        Assert.Equal(7, service.GetProperty(service.GetProperty("volume").Definition.Type).Iid);
        Assert.Equal(11, service.GetAction("11").Iid);
        Assert.Equal(55, (await device.GetPropertyAsync(9, 7))!.GetValue<int>());
        var error = Assert.Throws<ArgumentException>(() => service.GetProperty("missing"));
        Assert.Contains("7=volume", error.Message);
    }

    [Fact]
    public async Task FloatingPointPropertiesSupportValuesBeyondDecimalRange()
    {
        using var fixture = new MiotFixture();
        var device = (await fixture.Service.GetDevicesAsync())[0];
        var service = await device.GetServiceAsync("speaker");
        await service.GetProperty("large-float").SetAsync(1e29);
        Assert.Equal(1e29, Assert.Single(fixture.Requests)["value"]!.GetValue<double>());
    }

    private sealed class MiotFixture : IDisposable
    {
        private readonly HttpClient _http;
        public MiioCloudService Service { get; }
        public List<JsonNode> Requests { get; } = [];
        public int SpecRequests { get; private set; }
        public bool FailFirstSpec { get; init; }

        public MiotFixture()
        {
            _http = new HttpClient(new TestHttp(HandleAsync));
            Service = new MiioCloudService(new MiAccountClient(_http, new MiAccountOptions(), new MemoryTokenStore()), specHttpClient: _http);
        }

        private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                SpecRequests++;
                if (FailFirstSpec && SpecRequests == 1) return TestHttp.Json("{}", HttpStatusCode.ServiceUnavailable);
                var second = request.RequestUri!.Query.Contains("model-b", StringComparison.Ordinal);
                return TestHttp.Json(Spec(second ? 22 : 9, second ? 3 : 7));
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("device_list", StringComparison.Ordinal))
            {
                return TestHttp.Json("""
                    {"code":0,"result":{"list":[
                      {"did":"a","name":"First","model":"urn:miot-spec-v2:device:speaker:model-a"},
                      {"did":"b","name":"Second","model":"urn:miot-spec-v2:device:speaker:model-b"}
                    ]}}
                    """);
            }
            var form = await TestHttp.FormAsync(request);
            var parameters = JsonNode.Parse(form["data"])!["params"]!;
            Requests.Add((parameters is JsonArray array ? array[0]! : parameters).DeepClone());
            return TestHttp.Json(request.RequestUri.AbsolutePath.Split('/').Last() switch
            {
                "get" => "{\"code\":0,\"result\":[{\"code\":0,\"value\":55}]}",
                "set" => "{\"code\":0,\"result\":[{\"code\":0}]}",
                "action" => "{\"code\":0,\"result\":{\"code\":0,\"out\":[\"ok\"]}}",
                _ => throw new InvalidOperationException("Unexpected test request.")
            });
        }

        private static string Spec(int siid, int piid) => $$"""
            {"type":"urn:test","services":[{"iid":{{siid}},"type":"urn:miot-spec-v2:service:speaker:test","properties":[
              {"iid":{{piid}},"type":"urn:miot-spec-v2:property:volume:test","format":"uint8","access":["read","write","notify"],"value-range":[0,100,5]},
              {"iid":98,"type":"urn:miot-spec-v2:property:mode:test","format":"uint8","access":["read","write"],"value-list":[{"value":0},{"value":2}]},
              {"iid":99,"type":"urn:miot-spec-v2:property:read-only:test","format":"uint8","access":["read"]},
              {"iid":100,"type":"urn:miot-spec-v2:property:text:test","format":"string","access":["write"]},
              {"iid":101,"type":"urn:miot-spec-v2:property:large-float:test","format":"float","access":["write"],"value-range":[0,1e30,1]}
            ],"actions":[{"iid":11,"type":"urn:miot-spec-v2:action:announce:test","in":[100],"out":[]}]}]}
            """;

        public void Dispose() => _http.Dispose();
    }
}
