using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp.Auth;
using MiServiceSharp.Models;

namespace MiServiceSharp.Services;

public sealed class MiioCloudService
{
    private const string MiotSpecInstancesUrl = "http://miot-spec.org/miot-spec-v2/instances?status=all";
    private const string MiotSpecInstanceUrl = "http://miot-spec.org/miot-spec-v2/instance?type=";
    private static readonly HttpClient MiotSpecHttpClient = new();

    private readonly MiAccountClient _account;
    private readonly string _server;

    public MiioCloudService(MiAccountClient account, string? region = null)
    {
        _account = account;
        _server = "https://" + (string.IsNullOrWhiteSpace(region) || region == "cn" ? string.Empty : region + ".") + "api.io.mi.com/app";
    }

    public async Task<JsonNode> MiioRequestAsync(string uri, JsonNode data, CancellationToken cancellationToken = default)
    {
        var payload = await MiioRequestAsync<JsonNode>(uri, data, cancellationToken);
        if (payload is null)
        {
            throw new InvalidOperationException($"MiIO response missing payload: {uri}");
        }

        return payload;
    }

    public async Task<T> MiioRequestAsync<T>(string uri, JsonNode data, CancellationToken cancellationToken = default)
    {
        var headers = new Dictionary<string, string>
        {
            ["User-Agent"] = "iOS-14.4-6.0.103-iPhone12,3--D7744744F7AF32F0544445285880DD63E47D9BE9-8816080-84A3F44E137B71AE-iPhone",
            ["x-xiaomi-protocal-flag-cli"] = "PROTOCAL-HTTP2"
        };

        var response = await _account.MiRequestAsync(
            "xiaomiio",
            _server + uri,
            (tokenBundle, cookies) =>
            {
                cookies["PassportDeviceId"] = tokenBundle.DeviceId;
                var credential = tokenBundle.Services["xiaomiio"];
                return SignData(uri, data.ToJsonString(), credential.SSecurity);
            },
            headers,
            cancellationToken: cancellationToken);

        var envelope = response.Deserialize<MiioResponse<T>>();
        if (envelope is null)
        {
            throw new InvalidOperationException($"MiIO response envelope invalid: {response}");
        }

        if (envelope.Payload is null)
        {
            throw new InvalidOperationException($"MiIO response missing payload: {response}");
        }

        return envelope.Payload;
    }

    public Task<JsonNode> HomeRequestAsync(string did, string method, JsonArray parameters, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["id"] = 1,
            ["method"] = method,
            ["accessKey"] = "IOS00026747c5acafc2",
            ["params"] = parameters
        };

        return MiioRequestAsync($"/home/rpc/{did}", payload, cancellationToken);
    }

    public Task<JsonNode> MiotRequestAsync(string cmd, JsonNode parameters, CancellationToken cancellationToken = default)
        => MiioRequestAsync($"/miotspec/{cmd}", new JsonObject { ["params"] = parameters }, cancellationToken);

    public Task<T> MiotRequestAsync<T>(string cmd, JsonNode parameters, CancellationToken cancellationToken = default)
        => MiioRequestAsync<T>($"/miotspec/{cmd}", new JsonObject { ["params"] = parameters }, cancellationToken);

    public async Task<IReadOnlyList<JsonNode?>> HomeGetPropsAsync(string did, IEnumerable<string> props, CancellationToken cancellationToken = default)
    {
        var parameters = new JsonArray(props.Select(static prop => JsonValue.Create(prop)).ToArray());
        var result = await HomeRequestAsync(did, "get_prop", parameters, cancellationToken);
        return ReadJsonArray(result).ToList();
    }

    public async Task<JsonNode?> HomeGetPropAsync(string did, string prop, CancellationToken cancellationToken = default)
    {
        var values = await HomeGetPropsAsync(did, [prop], cancellationToken);
        return values.Count > 0 ? values[0] : null;
    }

    public async Task<IReadOnlyList<int>> HomeSetPropsAsync(string did, IEnumerable<(string Prop, JsonNode? Value)> props, CancellationToken cancellationToken = default)
    {
        var results = new List<int>();
        foreach (var (prop, value) in props)
        {
            var code = await HomeSetPropAsync(did, prop, value, cancellationToken);
            results.Add(code);
        }

        return results;
    }

    public async Task<int> HomeSetPropAsync(string did, string prop, JsonNode? value, CancellationToken cancellationToken = default)
    {
        JsonArray parameters;
        if (value is JsonArray arr)
        {
            parameters = arr;
        }
        else
        {
            parameters = new JsonArray(value ?? JsonValue.Create((string?)null));
        }

        var result = await HomeRequestAsync(did, "set_" + prop, parameters, cancellationToken);
        var first = ReadJsonArray(result).FirstOrDefault();
        return ParseSetPropResult(first);
    }

    public async Task<IReadOnlyList<string?>> MiotGetPropsAsync(string did, IEnumerable<(int Siid, int Piid)> iids, CancellationToken cancellationToken = default)
    {
        var parameters = new JsonArray(
            iids.Select(static item =>
                (JsonNode)new JsonObject
                {
                    ["did"] = string.Empty,
                    ["siid"] = item.Siid,
                    ["piid"] = item.Piid
                }).ToArray());

        foreach (var node in parameters)
        {
            if (node is JsonObject obj)
            {
                obj["did"] = did;
            }
        }

        var result = await MiotRequestAsync<List<MiotGetPropItem>>("prop/get", parameters, cancellationToken);
        var list = new List<string?>();
        foreach (var item in result)
        {
            if (item.Code != 0)
            {
                list.Add(null);
                continue;
            }

            list.Add((item.Value.ToString()));
        }

        return list;
    }

    public async Task<string?> MiotGetPropAsync(string did, (int Siid, int Piid) iid, CancellationToken cancellationToken = default)
    {
        var values = await MiotGetPropsAsync(did, [iid], cancellationToken);
        return values.Count > 0 ? values[0] : null;
    }

    public async Task<IReadOnlyList<int>> MiotSetPropsAsync(
        string did,
        IEnumerable<(int Siid, int Piid, object Value)> props,
        CancellationToken cancellationToken = default)
    {
        var parameters = new JsonArray(
            props.Select(item =>
                (JsonNode)new JsonObject
                {
                    ["did"] = did,
                    ["siid"] = item.Siid,
                    ["piid"] = item.Piid,
                    ["value"] = JsonSerializer.SerializeToNode(item.Value)
                }).ToArray());

        var result = await MiotRequestAsync<List<MiotSetPropItem>>("prop/set", parameters, cancellationToken);
        var list = new List<int>();
        foreach (var item in result)
        {
            list.Add(item.Code);
        }

        return list;
    }

    public Task<IReadOnlyList<int>> MiotSetPropsAsync(
        string did,
        IEnumerable<(int Siid, int Piid, JsonNode? Value)> props,
        CancellationToken cancellationToken = default)
        => MiotSetPropsAsync(
            did,
            props.Select(static item => (item.Siid, item.Piid, (object?)item.Value ?? string.Empty)),
            cancellationToken);

    public async Task<IReadOnlyList<int>> MiotSetPropMultiAsync(
        string did,
        IEnumerable<(int Siid, int Piid, List<object> Value)> props,
        CancellationToken cancellationToken = default)
    {
        var parameters = new JsonArray(
            props.Select(item =>
                (JsonNode)new JsonObject
                {
                    ["did"] = did,
                    ["siid"] = item.Siid,
                    ["piid"] = item.Piid,
                    ["value"] = JsonSerializer.SerializeToNode(item.Value)
                }).ToArray());

        var result = await MiotRequestAsync<List<MiotSetPropItem>>("prop/set", parameters, cancellationToken);
        var list = new List<int>();
        foreach (var item in result)
        {
            list.Add(item.Code);
        }

        return list;
    }


    public async Task<int> MiotSetPropAsync(string did, (int Siid, int Piid) iid, object? value, CancellationToken cancellationToken = default)
    {
        var result = await MiotSetPropsAsync(did, [(iid.Siid, iid.Piid, value ?? string.Empty)], cancellationToken);
        return result.Count > 0 ? result[0] : -1;
    }

    public Task<int> MiotSetPropAsync(string did, (int Siid, int Piid) iid, JsonNode? value, CancellationToken cancellationToken = default)
        => MiotSetPropAsync(did, iid, (object?)value, cancellationToken);

    public async Task<int> MiotSetPropAsync(string did, (int Siid, int Piid) iid, List<object> value, CancellationToken cancellationToken = default)
    {
        var result = await MiotSetPropMultiAsync(did, [(iid.Siid, iid.Piid, value)], cancellationToken);
        return result.Count > 0 ? result[0] : -1;
    }

    public async Task<int> MiotActionAsync(string did, (int Siid, int Aiid) iid, List<string>? args = null, CancellationToken cancellationToken = default)
    {
        var argsArray = new JsonArray((args ?? []).Select(static x => JsonValue.Create(x)).ToArray());
        return await MiotActionAsync(did, iid, argsArray, cancellationToken);
    }

    public async Task<int> MiotActionAsync(string did, (int Siid, int Aiid) iid, JsonArray? args, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["did"] = did,
            ["siid"] = iid.Siid,
            ["aiid"] = iid.Aiid,
            ["in"] = args ?? new JsonArray()
        };

        var result = await MiotRequestAsync<MiotActionResult>("action", payload, cancellationToken);
        return result.Code;
    }

    public async Task<MiotSpecDefinition> MiotSpecDataAsync(string? type = null, CancellationToken cancellationToken = default)
    {
        var finalType = await ResolveSingleMiotSpecTypeAsync(type, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, MiotSpecInstanceUrl + Uri.EscapeDataString(finalType));
        using var response = await MiotSpecHttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var instance = JsonSerializer.Deserialize<MiotSpecDefinition>(responseText);
        if (instance is null)
        {
            throw new InvalidOperationException($"MiotSpec response parse failed: {finalType}");
        }

        if (string.IsNullOrWhiteSpace(instance.Type))
        {
            instance.Type = finalType;
        }

        return instance;
    }

    public async Task<string> MiotSpecTextAsync(string? type = null, string? format = null, CancellationToken cancellationToken = default)
    {
        var instance = await MiotSpecDataAsync(type, cancellationToken);
        var node = JsonSerializer.SerializeToNode(instance) ?? new JsonObject();
        return RenderMiotSpecText(node, instance.Type, format);
    }



    private async Task<string> ResolveSingleMiotSpecTypeAsync(string? type, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(type) && type.StartsWith("urn", StringComparison.OrdinalIgnoreCase))
        {
            return type;
        }

        var all = await LoadAllMiotSpecModelsAsync(cancellationToken);
        var matched = MatchMiotSpecModels(all, type);
        if (matched.Count == 1)
        {
            return matched.Values.First();
        }

        if (matched.Count == 0)
        {
            throw new InvalidOperationException($"No MiotSpec model matched: {type}");
        }

        var candidates = string.Join(", ", matched.Keys.OrderBy(static x => x).Take(10));
        throw new InvalidOperationException($"Multiple MiotSpec models matched: {type}. Candidates: {candidates}");
    }

    public async Task<IReadOnlyList<MiioDevice>> DeviceListAsync(
        string? name = null,
        bool getVirtualModel = false,
        int getHuamiDevices = 0,
        CancellationToken cancellationToken = default)
    {
        var result = await MiioRequestAsync<MiioDeviceListResponse>(
            "/home/device_list",
            new JsonObject
            {
                ["getVirtualModel"] = getVirtualModel,
                ["getHuamiDevices"] = getHuamiDevices
            },
            cancellationToken);

        var list = result.List;
        if (list is null || list.Count == 0)
        {
            return [];
        }

        var output = new List<MiioDevice>();
        foreach (var device in list)
        {
            if (device is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name)
                || name.Equals("full", StringComparison.OrdinalIgnoreCase)
                || device.Did.Contains(name, StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                output.Add(device);
            }
        }

        return output;
    }

    public static string SignNonce(string ssecurity, string nonce)
    {
        using var sha = SHA256.Create();
        var sBytes = Convert.FromBase64String(ssecurity);
        var nBytes = Convert.FromBase64String(nonce);
        var merged = new byte[sBytes.Length + nBytes.Length];
        Buffer.BlockCopy(sBytes, 0, merged, 0, sBytes.Length);
        Buffer.BlockCopy(nBytes, 0, merged, sBytes.Length, nBytes.Length);
        return Convert.ToBase64String(sha.ComputeHash(merged));
    }

    public static Dictionary<string, string> SignData(string uri, string jsonData, string ssecurity, string? nonce = null)
    {
        nonce ??= BuildNonce();
        var signedNonce = SignNonce(ssecurity, nonce);
        var message = $"{uri}&{signedNonce}&{nonce}&data={jsonData}";
        using var hmac = new HMACSHA256(Convert.FromBase64String(signedNonce));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));

        return new Dictionary<string, string>
        {
            ["_nonce"] = nonce,
            ["data"] = jsonData,
            ["signature"] = signature
        };
    }

    private static string BuildNonce()
    {
        var randomBytes = new byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var minute = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);
        var minuteBytes = BitConverter.GetBytes(minute);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(minuteBytes);
        }

        var nonceBytes = new byte[12];
        Buffer.BlockCopy(randomBytes, 0, nonceBytes, 0, 8);
        Buffer.BlockCopy(minuteBytes, 0, nonceBytes, 8, 4);
        return Convert.ToBase64String(nonceBytes);
    }

    private static IEnumerable<JsonNode?> ReadJsonArray(JsonNode node)
    {
        if (node is JsonArray arr)
        {
            return arr;
        }

        return [];
    }

    private static int ParseSetPropResult(JsonNode? node)
    {
        if (node is null)
        {
            return -1;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return string.Equals(text, "ok", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
            }

            if (value.TryGetValue<int>(out var number))
            {
                return number;
            }
        }

        return -1;
    }

    private static async Task<Dictionary<string, string>> LoadAllMiotSpecModelsAsync(CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(Path.GetTempPath(), "miservice_miot_specs.json");

        try
        {
            if (File.Exists(cachePath))
            {
                var cachedText = await File.ReadAllTextAsync(cachePath, cancellationToken);
                var cached = JsonSerializer.Deserialize<Dictionary<string, string>>(cachedText);
                if (cached is not null && cached.Count > 0)
                {
                    return cached;
                }
            }
        }
        catch
        {
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, MiotSpecInstancesUrl);
        using var response = await MiotSpecHttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = JsonNode.Parse(responseText);
        var instances = json?["instances"] as JsonArray;

        var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (instances is not null)
        {
            foreach (var node in instances)
            {
                if (node is not JsonObject item)
                {
                    continue;
                }

                var model = item["model"]?.GetValue<string>() ?? string.Empty;
                var urn = item["type"]?.GetValue<string>() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(urn))
                {
                    all[model] = urn;
                }
            }
        }

        try
        {
            var saveText = JsonSerializer.Serialize(all);
            await File.WriteAllTextAsync(cachePath, saveText, cancellationToken);
        }
        catch
        {
        }

        return all;
    }

    private static Dictionary<string, string> MatchMiotSpecModels(Dictionary<string, string> all, string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return all;
        }

        var matched = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in all)
        {
            if (pair.Key.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [pair.Key] = pair.Value };
            }

            if (pair.Key.Contains(type, StringComparison.OrdinalIgnoreCase))
            {
                matched[pair.Key] = pair.Value;
            }
        }

        return matched;
    }

    private static string RenderMiotSpecText(JsonNode result, string type, string? format)
    {
        var url = MiotSpecInstanceUrl + type;
        var sb = new StringBuilder();

        var isPython = string.Equals(format, "python", StringComparison.OrdinalIgnoreCase);
        if (isPython)
        {
            sb.AppendLine("from enum import Enum");
            sb.AppendLine();
        }

        sb.Append("# Generated by MiServiceSharp").AppendLine();
        sb.Append("# ").Append(url).AppendLine();
        sb.AppendLine();

        var services = result["services"] as JsonArray;
        if (services is null)
        {
            return sb.ToString();
        }

        var serviceNames = new List<string>();
        var valueEnums = new List<(string Name, Dictionary<string, JsonNode?> Values)>();

        foreach (var serviceNode in services)
        {
            if (serviceNode is not JsonObject service)
            {
                continue;
            }

            var siid = service["iid"]?.GetValue<int>() ?? 0;
            var svc = (service["description"]?.GetValue<string>() ?? "Service").Replace(' ', '_');
            serviceNames.Add(svc);

            if (isPython)
            {
                sb.AppendLine($"class {svc}(tuple, Enum):");
            }
            else
            {
                sb.AppendLine($"{svc} = {siid}");
            }

            if (service["properties"] is JsonArray properties)
            {
                foreach (var propertyNode in properties)
                {
                    if (propertyNode is not JsonObject property)
                    {
                        continue;
                    }

                    var (name, comment) = ParseDesc(property["description"]?.GetValue<string>() ?? string.Empty);
                    var access = (property["access"] as JsonArray)?.Select(static x => x?.GetValue<string>() ?? string.Empty).Where(static x => !string.IsNullOrWhiteSpace(x)).ToList() ?? [];

                    var formatHint = property["format"]?.GetValue<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(formatHint) && !string.Equals(formatHint, "string", StringComparison.OrdinalIgnoreCase))
                    {
                        comment += "  # " + formatHint;
                    }

                    var accessHint = string.Concat(access.Select(static a => a[0]));
                    if (!string.IsNullOrWhiteSpace(accessHint) && !string.Equals(accessHint, "r", StringComparison.OrdinalIgnoreCase))
                    {
                        comment += "  # " + accessHint;
                    }

                    var readable = access.Any(static a => a.Equals("read", StringComparison.OrdinalIgnoreCase));
                    var piid = property["iid"]?.GetValue<int>() ?? 0;
                    var valueText = isPython ? $"({siid}, {piid})" : piid.ToString();
                    sb.Append("    ").Append(readable ? string.Empty : "_").Append(name).Append(" = ").Append(valueText).Append(comment).AppendLine();

                    if (property["value-range"] is JsonArray valueRange)
                    {
                        var values = new Dictionary<string, JsonNode?>();
                        if (valueRange.Count > 0) values["MIN"] = valueRange[0];
                        if (valueRange.Count > 1) values["MAX"] = valueRange[1];
                        if (valueRange.Count > 2)
                        {
                            var step = valueRange[2]?.GetValue<int>() ?? 1;
                            if (step != 1)
                            {
                                values["STEP"] = valueRange[2];
                            }
                        }

                        if (values.Count > 0)
                        {
                            valueEnums.Add(($"{svc}_{name}", values));
                        }
                    }
                    else if (property["value-list"] is JsonArray valueList)
                    {
                        var values = new Dictionary<string, JsonNode?>();
                        foreach (var valueItemNode in valueList)
                        {
                            if (valueItemNode is not JsonObject valueItem)
                            {
                                continue;
                            }

                            var key = valueItem["description"]?.GetValue<string>() ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(key))
                            {
                                key = valueItem["value"]?.ToJsonString() ?? string.Empty;
                            }

                            values[key.Replace(' ', '_')] = valueItem["value"];
                        }

                        if (values.Count > 0)
                        {
                            valueEnums.Add(($"{svc}_{name}", values));
                        }
                    }
                }
            }

            if (service["actions"] is JsonArray actions && actions.Count > 0)
            {
                sb.AppendLine();
                foreach (var actionNode in actions)
                {
                    if (actionNode is not JsonObject action)
                    {
                        continue;
                    }

                    var (name, comment) = ParseDesc(action["description"]?.GetValue<string>() ?? string.Empty);
                    if (action["in"] is JsonArray inArray && inArray.Count > 0)
                    {
                        comment += "  # in=" + inArray.ToJsonString();
                    }

                    if (action["out"] is JsonArray outArray && outArray.Count > 0)
                    {
                        comment += "  # out=" + outArray.ToJsonString();
                    }

                    var aiid = action["iid"]?.GetValue<int>() ?? 0;
                    var valueText = isPython ? $"({siid}, {aiid})" : aiid.ToString();
                    sb.Append("    _").Append(name).Append(" = ").Append(valueText).Append(comment).AppendLine();
                }
            }

            sb.AppendLine();
        }

        foreach (var (name, values) in valueEnums)
        {
            if (isPython)
            {
                sb.AppendLine($"class {name}(int, Enum):");
            }
            else
            {
                sb.AppendLine(name);
            }

            foreach (var value in values)
            {
                var key = int.TryParse(value.Key, out _) ? "_" + value.Key : value.Key;
                sb.Append("    ").Append(key).Append(" = ").Append(value.Value?.ToJsonString() ?? "null").AppendLine();
            }

            sb.AppendLine();
        }

        if (isPython)
        {
            sb.Append("ALL_SVCS = (").Append(string.Join(", ", serviceNames)).AppendLine(")");
        }

        return sb.ToString();
    }

    private static (string Name, string Comment) ParseDesc(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
        {
            return ("Unnamed", string.Empty);
        }

        var splitChars = "-—{「[【(（<《";
        var nameBuilder = new StringBuilder();
        for (var i = 0; i < desc.Length; i++)
        {
            var ch = desc[i];
            if (splitChars.Contains(ch))
            {
                var name = nameBuilder.Length == 0 ? "Unnamed" : nameBuilder.ToString();
                return (name, "  # " + desc[i..]);
            }

            nameBuilder.Append(ch == ' ' ? '_' : ch);
        }

        return (nameBuilder.Length == 0 ? "Unnamed" : nameBuilder.ToString(), string.Empty);
    }
}
