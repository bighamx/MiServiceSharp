using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp.Models;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Auth;

public sealed class MiAccountClient
{
    private readonly HttpClient _httpClient;
    private readonly MiAccountOptions _options;
    private readonly IMiTokenStore _tokenStore;
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    public MiAccountClient(HttpClient httpClient, MiAccountOptions options, IMiTokenStore tokenStore)
    {
        _httpClient = httpClient;
        _options = options;
        _tokenStore = tokenStore;
    }

    public MiTokenBundle? TokenBundle { get; private set; }

    public string? LastLoginError { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            TokenBundle ??= await _tokenStore.LoadAsync(cancellationToken);
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<bool> LoginAsync(string sid, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sid);
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            LastLoginError = null;
            TokenBundle ??= await _tokenStore.LoadAsync(cancellationToken)
                ?? new MiTokenBundle { DeviceId = GenerateRandom(16).ToUpperInvariant() };

            var first = await ServiceLoginAsync($"serviceLogin?sid={Uri.EscapeDataString(sid)}&_json=true", null, cancellationToken);
            var authResponse = first;
            Dictionary<string, string>? form = null;
            if ((first["code"]?.GetValue<int>() ?? -1) != 0 && !NeedNotificationVerification(first))
            {
                if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
                {
                    throw new InvalidOperationException("Set MI_USER and MI_PASS to refresh the login.");
                }
                form = new Dictionary<string, string>
                {
                    ["_json"] = "true",
                    ["qs"] = first["qs"]?.GetValue<string>() ?? string.Empty,
                    ["sid"] = first["sid"]?.GetValue<string>() ?? sid,
                    ["_sign"] = first["_sign"]?.GetValue<string>() ?? string.Empty,
                    ["callback"] = first["callback"]?.GetValue<string>() ?? string.Empty,
                    ["user"] = _options.Username,
                    ["hash"] = ComputeMd5Upper(_options.Password)
                };
                authResponse = await ServiceLoginAsync("serviceLoginAuth2", form, cancellationToken);
            }

            authResponse = await ResolveNotificationChallengeAsync(sid, form, authResponse, cancellationToken);
            if ((authResponse["code"]?.GetValue<int>() ?? -1) != 0)
            {
                throw new InvalidOperationException($"Xiaomi login failed (code={authResponse["code"]}).");
            }
            foreach (var key in new[] { "userId", "passToken", "location", "nonce", "ssecurity" })
            {
                if (string.IsNullOrWhiteSpace(authResponse[key]?.ToString()))
                {
                    throw new InvalidOperationException($"Login response missing {key}.");
                }
            }

            var location = authResponse["location"]!.GetValue<string>();
            var nonce = authResponse["nonce"]!.ToString();
            var ssecurity = authResponse["ssecurity"]!.GetValue<string>();
            var serviceToken = await SecurityTokenServiceAsync(location, nonce, ssecurity, cancellationToken);
            var updated = CloneBundle(TokenBundle);
            updated.UserId = long.Parse(authResponse["userId"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture);
            updated.PassToken = authResponse["passToken"]!.GetValue<string>();
            updated.Services[sid] = new ServiceCredential { SSecurity = ssecurity, ServiceToken = serviceToken };
            await _tokenStore.SaveAsync(updated, cancellationToken);
            TokenBundle = updated;
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (NotificationVerificationRequiredException)
        {
            LastLoginError = "Xiaomi security verification is required.";
            throw;
        }
        catch (Exception error)
        {
            LastLoginError = error is InvalidOperationException ? error.Message : $"Login failed ({error.GetType().Name}).";
            return false;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private static MiTokenBundle CloneBundle(MiTokenBundle source) => new()
    {
        DeviceId = source.DeviceId,
        UserId = source.UserId,
        PassToken = source.PassToken,
        Services = source.Services.ToDictionary(
            pair => pair.Key,
            pair => new ServiceCredential { SSecurity = pair.Value.SSecurity, ServiceToken = pair.Value.ServiceToken },
            StringComparer.OrdinalIgnoreCase)
    };

    private async Task InvalidateServiceAsync(string sid, string expiredToken, CancellationToken cancellationToken)
    {
        await _loginLock.WaitAsync(cancellationToken);
        try
        {
            if (TokenBundle is not null
                && TokenBundle.Services.TryGetValue(sid, out var current)
                && current.ServiceToken == expiredToken)
            {
                var updated = CloneBundle(TokenBundle);
                updated.Services.Remove(sid);
                await _tokenStore.SaveAsync(updated, cancellationToken);
                TokenBundle = updated;
            }
        }
        finally
        {
            _loginLock.Release();
        }
    }

    public async Task<JsonNode> MiRequestAsync(
        string sid,
        string url,
        Func<MiTokenBundle, Dictionary<string, string>, Dictionary<string, string>?> formBuilder,
        Dictionary<string, string>? headers = null,
        bool allowRelogin = true,
        CancellationToken cancellationToken = default)
    {
        if (TokenBundle is null)
        {
            await InitializeAsync(cancellationToken);
        }

        if (TokenBundle is null || !TokenBundle.Services.ContainsKey(sid))
        {
            var loginOk = await LoginAsync(sid, cancellationToken);
            if (!loginOk || TokenBundle is null)
            {
                throw new InvalidOperationException(LastLoginError ?? "Login failed.");
            }
        }

        var bundle = TokenBundle;
        var credential = bundle.Services[sid];
        var cookieMap = new Dictionary<string, string>
        {
            ["userId"] = bundle.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["serviceToken"] = credential.ServiceToken
        };

        var form = formBuilder(bundle, cookieMap);
        string responseText;
        try
        {
            responseText = await SendRequestAsync(url, form, cookieMap, headers, cancellationToken);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.Unauthorized && allowRelogin)
        {
            await InvalidateServiceAsync(sid, credential.ServiceToken, cancellationToken);
            return await MiRequestAsync(sid, url, formBuilder, headers, false, cancellationToken);
        }

        JsonNode? json;
        try
        {
            json = JsonNode.Parse(responseText);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Xiaomi returned a non-JSON response.");
        }

        if (json is null)
        {
            throw new InvalidOperationException("Empty response.");
        }

        var code = json["code"]?.GetValue<int>() ?? -1;
        if (code == 0)
        {
            return json;
        }

        var message = json["message"]?.GetValue<string>() ?? string.Empty;
        if (allowRelogin && message.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            await InvalidateServiceAsync(sid, credential.ServiceToken, cancellationToken);
            return await MiRequestAsync(sid, url, formBuilder, headers, false, cancellationToken);
        }

        throw new InvalidOperationException($"MiRequest failed (code={code}): {message}");
    }

    private async Task<JsonNode> ServiceLoginAsync(
        string uri,
        Dictionary<string, string>? formData,
        CancellationToken cancellationToken)
    {
        if (TokenBundle is null)
        {
            throw new InvalidOperationException("Token bundle not initialized.");
        }

        var url = $"https://account.xiaomi.com/pass/{uri}";

        var cookies = new Dictionary<string, string>
        {
            ["sdkVersion"] = "3.9",
            ["deviceId"] = TokenBundle.DeviceId,
            ["pass_ua"] = string.Empty,
            ["passToken"] = TokenBundle.PassToken
        };
        if (TokenBundle.UserId > 0)
        {
            cookies["userId"] = TokenBundle.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var responseText = await SendRequestAsync(
            url,
            formData,
            cookies,
            new Dictionary<string, string> { ["User-Agent"] = PickUserAgent() },
            cancellationToken);

        if (responseText.StartsWith("&&&START&&&", StringComparison.Ordinal))
        {
            responseText = responseText[11..];
        }

        return JsonNode.Parse(responseText) ?? throw new InvalidOperationException("Invalid login response.");
    }

    private async Task<JsonNode> ResolveNotificationChallengeAsync(
        string sid,
        Dictionary<string, string>? auth2Form,
        JsonNode authResponse,
        CancellationToken cancellationToken)
    {
        if (!NeedNotificationVerification(authResponse))
        {
            return authResponse;
        }

        await _tokenStore.SaveAsync(TokenBundle!, cancellationToken);
        var maxRetry = Math.Max(1, _options.VerificationMaxRetryCount);
        for (var i = 1; i <= maxRetry; i++)
        {
            var notificationUrl = authResponse["notificationUrl"]?.GetValue<string>() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(notificationUrl))
            {
                await NotifyVerificationUrlAsync(notificationUrl, i, maxRetry, cancellationToken);
            }

            authResponse = await ServiceLoginAsync($"serviceLogin?sid={Uri.EscapeDataString(sid)}&_json=true", null, cancellationToken);
            if ((authResponse["code"]?.GetValue<int>() ?? -1) != 0 && auth2Form is not null)
            {
                authResponse = await ServiceLoginAsync("serviceLoginAuth2", auth2Form, cancellationToken);
            }
            if (!NeedNotificationVerification(authResponse))
            {
                return authResponse;
            }

            if ((authResponse["code"]?.GetValue<int>() ?? -1) != 0)
            {
                return authResponse;
            }
        }

        throw new InvalidOperationException("notificationUrl 验证未完成或超时，请完成手机验证后重试登录。");
    }

    private async Task NotifyVerificationUrlAsync(string notificationUrl, int attempt, int maxRetry, CancellationToken cancellationToken)
    {
        if (_options.NotificationUrlHandler is not null)
        {
            await _options.NotificationUrlHandler(notificationUrl, cancellationToken);
            return;
        }

        if (_options.EnableInteractiveVerification && HasInteractiveConsole())
        {
            Console.WriteLine($"\n[MiServiceSharp] 检测到登录二次验证（{attempt}/{maxRetry}）。");
            Console.WriteLine("请在浏览器中打开以下链接，完成手机短信验证码验证后回到这里继续：");
            Console.WriteLine(notificationUrl);
            Console.WriteLine("完成后按回车继续...");
            await Console.In.ReadLineAsync(cancellationToken);
            return;
        }

        throw new NotificationVerificationRequiredException(notificationUrl);
    }

    private static bool NeedNotificationVerification(JsonNode response)
    {
        var notificationUrl = response["notificationUrl"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(notificationUrl))
        {
            return false;
        }

        var location = response["location"]?.GetValue<string>() ?? string.Empty;
        var ssecurity = response["ssecurity"]?.GetValue<string>() ?? string.Empty;
        var nonce = response["nonce"]?.ToString() ?? string.Empty;
        var userId = response["userId"]?.ToString() ?? string.Empty;
        var passToken = response["passToken"]?.GetValue<string>() ?? string.Empty;

        var hasCompletedTokenPayload =
            !string.IsNullOrWhiteSpace(location)
            && !string.IsNullOrWhiteSpace(ssecurity)
            && !string.IsNullOrWhiteSpace(nonce)
            && !string.IsNullOrWhiteSpace(userId)
            && !string.IsNullOrWhiteSpace(passToken);

        return !hasCompletedTokenPayload;
    }

    private static bool HasInteractiveConsole()
    {
        try
        {
            return Environment.UserInteractive
                && !Console.IsInputRedirected
                && !Console.IsOutputRedirected
                && !Console.IsErrorRedirected;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> SecurityTokenServiceAsync(
        string location,
        string nonce,
        string ssecurity,
        CancellationToken cancellationToken)
    {
        var nsec = $"nonce={nonce}&{ssecurity}";
        var clientSign = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(nsec)));
        var finalUrl = $"{location}&clientSign={WebUtility.UrlEncode(clientSign)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, finalUrl);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            throw new InvalidOperationException("serviceToken not found in Set-Cookie headers.");
        }

        foreach (var setCookie in setCookies)
        {
            var token = ExtractCookieValue(setCookie, "serviceToken");
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        throw new InvalidOperationException("serviceToken not found.");
    }

    private async Task<string> SendRequestAsync(
        string url,
        Dictionary<string, string>? formData,
        Dictionary<string, string>? cookies,
        Dictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(formData is null ? HttpMethod.Get : HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("User-Agent", PickUserAgent());

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.Remove(header.Key);
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        if (cookies is not null && cookies.Count > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(cookies));
        }

        if (formData is not null)
        {
            request.Content = new FormUrlEncodedContent(formData);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string ComputeMd5Upper(string value)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static string BuildCookieHeader(Dictionary<string, string> cookieMap)
    {
        return string.Join("; ", cookieMap.Where(static pair => !string.IsNullOrWhiteSpace(pair.Key)).Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    private static string ExtractCookieValue(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(cookieName, StringComparison.OrdinalIgnoreCase))
            {
                return kv[1];
            }
        }

        return string.Empty;
    }

    private static string PickUserAgent() => "APP/com.xiaomi.mihome APPV/11.3.203 iosPassportSDK/4.2.50 iOS/26.3.1";

    private static string GenerateRandom(int length)
    {
        const string source = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Range(0, length).Select(_ => source[RandomNumberGenerator.GetInt32(source.Length)]).ToArray());
    }
}
