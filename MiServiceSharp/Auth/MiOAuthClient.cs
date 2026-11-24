using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MiServiceSharp.Auth;

public sealed class MiOAuthClient(HttpClient httpClient)
{
    public MiOAuthAuthorization CreateAuthorization(string appId, string redirectUri, string region = "cn")
    {
        ValidateAppId(appId);
        ValidateRegion(region);
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("An absolute HTTP or HTTPS OAuth redirect URI is required.", nameof(redirectUri));
        }
        if (appId == "2882303761520251711" && redirect.GetLeftPart(UriPartial.Authority) == "http://homeassistant.local:8123"
            && redirect.AbsolutePath == "/" && string.IsNullOrEmpty(redirect.Query))
        {
            var webhookId = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(RandomNumberGenerator.GetBytes(8))
                .ToString(CultureInfo.InvariantCulture);
            redirectUri = "http://homeassistant.local:8123/api/webhook/" + webhookId;
        }
        var deviceId = "ha." + Guid.NewGuid().ToString("N");
        var state = Convert.ToHexString(SHA1.HashData(System.Text.Encoding.UTF8.GetBytes("d=" + deviceId))).ToLowerInvariant();
        var query = new Dictionary<string, string>
        {
            ["client_id"] = appId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["device_id"] = deviceId,
            ["state"] = state,
            ["skip_confirm"] = "false"
        };
        var url = "https://account.xiaomi.com/oauth2/authorize?" + string.Join("&",
            query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        return new MiOAuthAuthorization(new Uri(url), appId, redirectUri, region, deviceId, state);
    }

    public async Task<MiOAuthToken> CompleteAuthorizationAsync(MiOAuthAuthorization authorization, Uri callbackUri, CancellationToken cancellationToken = default)
    {
        var query = ValidateAuthorizationCallback(authorization, callbackUri);
        if (query.TryGetValue("error", out var error))
        {
            throw new InvalidOperationException($"Xiaomi OAuth authorization failed ({error}).");
        }
        if (!query.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("The OAuth callback does not contain an authorization code.");
        }
        var data = new JsonObject
        {
            ["client_id"] = ValidateAppId(authorization.AppId),
            ["redirect_uri"] = authorization.RedirectUri,
            ["device_id"] = authorization.DeviceId,
            ["code"] = code
        };
        return await RequestTokenAsync(authorization.AppId, authorization.RedirectUri, authorization.Region, authorization.DeviceId, data, cancellationToken);
    }

    public Task<MiOAuthToken> RefreshAsync(MiOAuthToken token, CancellationToken cancellationToken = default)
    {
        var data = new JsonObject
        {
            ["client_id"] = ValidateAppId(token.AppId),
            ["redirect_uri"] = token.RedirectUri,
            ["refresh_token"] = token.RefreshToken
        };
        return RequestTokenAsync(token.AppId, token.RedirectUri, token.Region, token.DeviceId, data, cancellationToken);
    }

    private async Task<MiOAuthToken> RequestTokenAsync(string appId, string redirectUri, string region, string deviceId, JsonObject data, CancellationToken cancellationToken)
    {
        ValidateRegion(region);
        var host = region == "cn" ? "ha.api.io.mi.com" : region + ".ha.api.io.mi.com";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://{host}/app/v2/ha/oauth/get_token?data={Uri.EscapeDataString(data.ToJsonString())}");
        request.Headers.TryAddWithoutValidation("User-Agent", $"MiServiceSharp/1.0 client/{deviceId}");
        request.Content = new ByteArrayContent([]);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Xiaomi OAuth token request failed (HTTP {(int)response.StatusCode}).");
        }
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Xiaomi returned an invalid OAuth response.");
        }
        var result = body?["result"];
        if (body?["code"]?.GetValue<int>() != 0)
        {
            var errorCode = result?["error"]?.ToString() ?? "unknown";
            if (errorCode == "96013")
            {
                throw new InvalidOperationException("Xiaomi rejected the authorization code (96013). Start a new OAuth authorization.");
            }
            throw new InvalidOperationException($"Xiaomi OAuth authorization failed (code={body?["code"]}, error={errorCode}).");
        }
        if (result is null
            || string.IsNullOrWhiteSpace(result["access_token"]?.GetValue<string>())
            || string.IsNullOrWhiteSpace(result["refresh_token"]?.GetValue<string>())
            || result["expires_in"]?.GetValue<long>() is not > 0)
        {
            throw new InvalidOperationException("Xiaomi OAuth authorization failed or returned incomplete credentials.");
        }
        return new MiOAuthToken
        {
            AppId = appId,
            RedirectUri = redirectUri,
            DeviceId = deviceId,
            Region = region,
            AccessToken = result["access_token"]!.GetValue<string>(),
            RefreshToken = result["refresh_token"]!.GetValue<string>(),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(result["expires_in"]!.GetValue<long>())
        };
    }

    internal static void ValidateRegion(string region)
    {
        if (region is not ("cn" or "de" or "i2" or "ru" or "sg" or "us"))
        {
            throw new ArgumentException("Unsupported Xiaomi cloud region.", nameof(region));
        }
    }

    private static long ValidateAppId(string appId)
    {
        if (!long.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException("A numeric Xiaomi OAuth application ID is required.", nameof(appId));
        }
        return value;
    }

    internal static Dictionary<string, string> ValidateAuthorizationCallback(MiOAuthAuthorization authorization, Uri callbackUri)
    {
        var expected = new Uri(authorization.RedirectUri);
        if (!string.Equals(callbackUri.GetLeftPart(UriPartial.Path), expected.GetLeftPart(UriPartial.Path), StringComparison.Ordinal))
        {
            throw new ArgumentException("The OAuth callback URI does not match the registered redirect URI.", nameof(callbackUri));
        }
        var query = ParseQuery(callbackUri.Query);
        if (!query.TryGetValue("state", out var state)
            || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(state), System.Text.Encoding.UTF8.GetBytes(authorization.State)))
        {
            throw new InvalidOperationException("The OAuth state does not match this authorization request.");
        }
        return query;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty;
            if (!result.TryAdd(key, value))
            {
                throw new ArgumentException("The OAuth callback contains duplicate parameters.", nameof(query));
            }
        }
        return result;
    }
}
