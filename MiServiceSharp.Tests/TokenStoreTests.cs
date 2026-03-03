using MiServiceSharp.Models;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

public sealed class TokenStoreTests
{
    [Fact]
    public async Task FileTokenStore_RoundTrip_ShouldPersistData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"miservice-token-{Guid.NewGuid():N}.json");
        var store = new FileMiTokenStore(path);

        var token = new MiTokenBundle
        {
            DeviceId = "ABCDEF0123456789",
            UserId = 123456789,
            PassToken = "pass-token"
        };
        token.Services["micoapi"] = new ServiceCredential
        {
            SSecurity = "sec",
            ServiceToken = "svc"
        };

        await store.SaveAsync(token);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal("ABCDEF0123456789", loaded!.DeviceId);
        Assert.Equal(123456789, loaded.UserId);
        Assert.Equal("pass-token", loaded.PassToken);
        Assert.Equal("sec", loaded.Services["micoapi"].SSecurity);
        Assert.Equal("svc", loaded.Services["micoapi"].ServiceToken);

        await store.ClearAsync();
        Assert.False(File.Exists(path));
    }
}
