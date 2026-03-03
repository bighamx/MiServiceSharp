using MiServiceSharp.Services;

namespace MiServiceSharp.Tests;

public sealed class MiioSignTests
{
    [Fact]
    public void SignData_WithFixedNonce_ShouldGenerateStableSignatureFields()
    {
        var uri = "/home/device_list";
        var payload = "{\"getVirtualModel\":false,\"getHuamiDevices\":0}";
        var ssecurity = "dGVzdC1zc2VjdXJpdHk=";
        var nonce = "MDEyMzQ1Njc4OWFi";

        var result = MiioCloudService.SignData(uri, payload, ssecurity, nonce);

        Assert.Equal(nonce, result["_nonce"]);
        Assert.Equal(payload, result["data"]);
        Assert.True(result.ContainsKey("signature"));
        Assert.False(string.IsNullOrWhiteSpace(result["signature"]));
    }

    [Fact]
    public void SignNonce_ShouldReturnBase64String()
    {
        var ssecurity = "dGVzdC1zc2VjdXJpdHk=";
        var nonce = "MDEyMzQ1Njc4OWFi";

        var signed = MiioCloudService.SignNonce(ssecurity, nonce);

        Assert.False(string.IsNullOrWhiteSpace(signed));
        var bytes = Convert.FromBase64String(signed);
        Assert.NotEmpty(bytes);
    }
}
