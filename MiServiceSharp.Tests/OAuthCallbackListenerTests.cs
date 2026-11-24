using System.Net;
using System.Net.Sockets;
using System.Text;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

public sealed class OAuthCallbackListenerTests
{
    [Fact]
    public async Task CallbackCanBeReceivedExchangedAndSavedAutomatically()
    {
        using var oauthHttp = new HttpClient(new TestHttp((_, _) => Task.FromResult(TestHttp.Json(
            """{"code":0,"result":{"access_token":"test-access","refresh_token":"test-refresh","expires_in":3600}}"""))));
        var oauth = new MiOAuthClient(oauthHttp);
        var authorization = CreateAuthorization(oauth);
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        using var browser = CreateBrowser();
        var callback = new Uri($"{authorization.RedirectUri}?state={authorization.State}&code=test-code");
        using var response = await browser.GetAsync(callback, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("test-code", await response.Content.ReadAsStringAsync(timeout.Token));
        var token = await oauth.CompleteAuthorizationAsync(authorization, await waiting, timeout.Token);

        var directory = Directory.CreateTempSubdirectory("miservice-oauth-callback-");
        try
        {
            var store = new FileMiOAuthTokenStore(Path.Combine(directory.FullName, ".mi.oauth.json"));
            await store.SaveAuthorizationAsync(authorization, timeout.Token);
            await store.SaveAsync(token, timeout.Token);
            await store.ClearAuthorizationAsync(timeout.Token);
            Assert.Equal("test-access", (await store.LoadAsync(timeout.Token))!.AccessToken);
            Assert.Null(await store.LoadAuthorizationAsync(timeout.Token));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("wrong-state", HttpStatusCode.Forbidden)]
    [InlineData("wrong-path", HttpStatusCode.NotFound)]
    [InlineData("duplicate-state", HttpStatusCode.NotFound)]
    [InlineData("missing-code", HttpStatusCode.BadRequest)]
    public async Task InvalidRequestsAreRejectedWhileValidCallbackStillSucceeds(string kind, HttpStatusCode expected)
    {
        using var oauthHttp = new HttpClient();
        var authorization = CreateAuthorization(new MiOAuthClient(oauthHttp));
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        var invalid = kind switch
        {
            "wrong-state" => $"{authorization.RedirectUri}?state=wrong&code=test",
            "wrong-path" => $"{new Uri(authorization.RedirectUri).GetLeftPart(UriPartial.Authority)}/wrong?state={authorization.State}&code=test",
            "duplicate-state" => $"{authorization.RedirectUri}?state={authorization.State}&state={authorization.State}&code=test",
            _ => $"{authorization.RedirectUri}?state={authorization.State}"
        };
        using var browser = CreateBrowser();
        using var rejected = await browser.GetAsync(invalid, timeout.Token);
        Assert.Equal(expected, rejected.StatusCode);
        var valid = new Uri($"{authorization.RedirectUri}?state={authorization.State}&code=test");
        using var accepted = await browser.GetAsync(valid, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(valid, await waiting);
    }

    [Fact]
    public async Task CancellationAndDisposalReleaseThePort()
    {
        using var http = new HttpClient();
        var authorization = CreateAuthorization(new MiOAuthClient(http));
        var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var stopping = new CancellationTokenSource();
        var waiting = listener.WaitForCallbackAsync(stopping.Token);
        stopping.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await listener.DisposeAsync();
        await listener.DisposeAsync();
        using var rebound = new TcpListener(IPAddress.Loopback, listener.CallbackUri.Port);
        rebound.Start();
    }

    [Fact]
    public async Task FragmentedHttpRequestIsAccepted()
    {
        using var http = new HttpClient();
        var authorization = CreateAuthorization(new MiOAuthClient(http));
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        using var browser = new TcpClient();
        await browser.ConnectAsync(IPAddress.Loopback, listener.CallbackUri.Port, timeout.Token);
        var stream = browser.GetStream();
        var request = $"GET /callback?state={authorization.State}&code=test HTTP/1.1\r\nHost: localhost\r\n\r\n";
        var bytes = Encoding.ASCII.GetBytes(request);
        await stream.WriteAsync(bytes.AsMemory(0, 7), timeout.Token);
        await Task.Delay(20, timeout.Token);
        await stream.WriteAsync(bytes.AsMemory(7), timeout.Token);
        using var reader = new StreamReader(stream);
        Assert.Contains("200 OK", await reader.ReadToEndAsync(timeout.Token));
        Assert.Contains("code=test", (await waiting).Query);
    }

    [Fact]
    public async Task AuthorizationDenialIsReportedWithoutTokenExchange()
    {
        using var http = new HttpClient(new TestHttp((_, _) => throw new InvalidOperationException("Token HTTP must not be called.")));
        var oauth = new MiOAuthClient(http);
        var authorization = CreateAuthorization(oauth);
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        using var browser = CreateBrowser();
        using var response = await browser.GetAsync($"{authorization.RedirectUri}?state={authorization.State}&error=access_denied", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var callback = await waiting;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => oauth.CompleteAuthorizationAsync(authorization, callback, timeout.Token));
        Assert.Contains("access_denied", error.Message);
    }

    [Fact]
    public async Task UnsupportedHttpMethodDoesNotConsumeTheCallback()
    {
        using var http = new HttpClient();
        var authorization = CreateAuthorization(new MiOAuthClient(http));
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        using var browser = CreateBrowser();
        using var rejected = await browser.PostAsync(authorization.RedirectUri, new StringContent(""), timeout.Token);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, rejected.StatusCode);
        using var accepted = await browser.GetAsync($"{authorization.RedirectUri}?state={authorization.State}&code=test", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await waiting;
    }

    [Fact]
    public async Task OversizedHeaderDoesNotConsumeTheCallback()
    {
        using var http = new HttpClient();
        var authorization = CreateAuthorization(new MiOAuthClient(http));
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        using (var oversized = new TcpClient())
        {
            await oversized.ConnectAsync(IPAddress.Loopback, listener.CallbackUri.Port, timeout.Token);
            var stream = oversized.GetStream();
            var header = Encoding.ASCII.GetBytes("GET /callback HTTP/1.1\r\nX-Large: " + new string('x', 16 * 1024));
            await stream.WriteAsync(header, timeout.Token);
            using var reader = new StreamReader(stream);
            Assert.Contains("431 Request Header Fields Too Large", await reader.ReadLineAsync(timeout.Token));
        }
        using var browser = CreateBrowser();
        using var accepted = await browser.GetAsync($"{authorization.RedirectUri}?state={authorization.State}&code=test", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await waiting;
    }

    [Fact]
    public async Task NamedCallbackHostCanBeReachedThroughAnIpAddress()
    {
        using var http = new HttpClient();
        var oauth = new MiOAuthClient(http);
        var available = CreateAuthorization(oauth);
        var port = new Uri(available.RedirectUri).Port;
        var authorization = oauth.CreateAuthorization("123456", $"http://homeassistant.local:{port}/api/webhook/test");
        await using var listener = new MiOAuthCallbackListener(authorization);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync(timeout.Token);
        using var browser = CreateBrowser();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"http://127.0.0.1:{port}/api/webhook/test?state={authorization.State}&code=test");
        request.Headers.Host = $"homeassistant.local:{port}";
        using var response = await browser.SendAsync(request, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("homeassistant.local", (await waiting).Host);
    }

    private static MiOAuthAuthorization CreateAuthorization(MiOAuthClient oauth)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        return oauth.CreateAuthorization("123456", $"http://127.0.0.1:{port}/callback");
    }

    private static HttpClient CreateBrowser() => new(new HttpClientHandler { UseProxy = false });
}
