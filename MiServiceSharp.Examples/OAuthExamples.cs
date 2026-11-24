using System.Diagnostics;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Examples;

internal static class OAuthExamples
{
    public static async Task AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var oauth = new MiOAuthClient(http);
        var authorization = oauth.CreateAuthorization(
            "2882303761520251711", "http://homeassistant.local:8123", "cn");
        var path = Environment.GetEnvironmentVariable("MI_OAUTH_TOKEN_PATH") ?? FileMiOAuthTokenStore.GetDefaultPath();
        var store = new FileMiOAuthTokenStore(path);
        await store.SaveAuthorizationAsync(authorization, cancellationToken);

        // 回调域名应解析到运行本示例的设备，浏览器应能访问该端口。
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        Console.WriteLine($"等待授权回调 {listener.CallbackUri}");
        Process.Start(new ProcessStartInfo(authorization.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });

        // 用户在浏览器确认授权后，直接接收回调并校验 state。
        var callback = await listener.WaitForCallbackAsync(cancellationToken);
        var token = await oauth.CompleteAuthorizationAsync(authorization, callback, cancellationToken);
        await store.SaveAsync(token, cancellationToken);
        await store.ClearAuthorizationAsync(cancellationToken);
        Console.WriteLine($"授权凭证已保存到 {store.FilePath}");
    }
}
