using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using MiServiceSharp;
using MiServiceSharp.Auth;
using MiServiceSharp.Devices;
using MiServiceSharp.Protocol.LocalMiio;
using MiServiceSharp.Storage;
using MiServiceSharp.Subscriptions;

internal static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        try
        {
            return await RunAsync(args, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (NotificationVerificationRequiredException)
        {
            Console.Error.WriteLine("Xiaomi security verification is required. Run the login command in an interactive terminal.");
            return 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args[0] == "oauth-login")
        {
            return await LoginOAuthAsync(args, cancellationToken);
        }
        if (args[0] == "oauth-complete")
        {
            using var oauthHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
            var oauthStore = new FileMiOAuthTokenStore(GetOAuthTokenPath());
            var pending = await oauthStore.LoadAuthorizationAsync(cancellationToken)
                ?? throw new InvalidOperationException("No pending OAuth authorization. Run oauth-login first.");
            return await CompleteOAuthAsync(new MiOAuthClient(oauthHttp), oauthStore, pending, cancellationToken);
        }

        var store = new FileMiTokenStore(FileMiTokenStore.GetDefaultPath());
        if (args[0] == "import-token")
        {
            RequireArguments(args, 2, "import-token <token-file>");
            var imported = await new FileMiTokenStore(args[1]).LoadAsync(cancellationToken);
            if (imported is null || !imported.IsLoggedIn)
            {
                throw new InvalidOperationException("The source token contains no logged-in identity.");
            }
            await store.SaveAsync(imported, cancellationToken);
            Console.WriteLine($"Token imported into {store.FilePath}");
            return 0;
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var account = new MiAccountClient(httpClient, new MiAccountOptions
        {
            Username = GetEnv("MI_USER") ?? string.Empty,
            Password = GetEnv("MI_PASS") ?? string.Empty
        }, store);
        await account.InitializeAsync(cancellationToken);
        var client = new MiServiceFacade(account);

        switch (args[0])
        {
            case "login":
                foreach (var sid in args.Length > 1 ? args[1..] : new[] { "micoapi", "xiaomiio" })
                {
                    if (!await account.LoginAsync(sid, cancellationToken))
                    {
                        throw new InvalidOperationException(account.LastLoginError ?? $"Login for {sid} failed.");
                    }
                    Console.WriteLine($"{sid} login successful");
                }
                Console.WriteLine($"Token file is {store.FilePath}");
                return 0;
            case "mina-devices":
                foreach (var speaker in await client.Mina.GetDevicesAsync(cancellationToken))
                {
                    Console.WriteLine($"{speaker.Name} | miotDid={speaker.MiotDid} | deviceId={speaker.DeviceId} | {speaker.Info.Hardware}");
                }
                return 0;
            case "miio-devices":
                foreach (var device in await client.MiioCloud.GetDevicesAsync(args.Length > 1 ? args[1] : null, cancellationToken))
                {
                    Console.WriteLine($"{device.Name} | {device.Model} | did={device.Did} | online={device.Info.IsOnline}");
                }
                return 0;
            case "pause":
            case "stop":
            case "play":
            case "status":
            case "volume":
            case "tts":
            case "play-url":
                RequireArguments(args, 2, $"{args[0]} <device-name-or-id> [value]");
                var target = await FindSpeakerAsync(client, args[1], cancellationToken);
                var result = await RunSpeakerCommandAsync(target, args, cancellationToken);
                Console.WriteLine(result.ToJsonString(OutputOptions));
                return 0;
            case "spec":
                RequireArguments(args, 2, "spec <model-or-URN>");
                var spec = await client.MiioCloud.MiotSpecDataAsync(args[1], cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(spec, OutputOptions));
                return 0;
            case "capabilities":
                RequireArguments(args, 2, "capabilities <device-name-or-id>");
                var described = await FindDeviceAsync(client, args[1], cancellationToken);
                Console.WriteLine(JsonSerializer.Serialize(await described.GetSpecAsync(cancellationToken), OutputOptions));
                return 0;
            case "watch":
            case "watch-events":
                RequireArguments(args, 2, $"{args[0]} <device-name-or-id> [service] [property-or-event]");
                var oauthStore = new FileMiOAuthTokenStore(GetOAuthTokenPath());
                var provider = new MiOAuthTokenProvider(new MiOAuthClient(httpClient), oauthStore);
                await provider.GetTokenAsync(cancellationToken);
                var observed = await FindDeviceAsync(client, args[1], cancellationToken);
                await using (var notifications = client.CreateSubscriptionClient(provider))
                {
                    notifications.Error += (_, error) => Console.Error.WriteLine($"Subscription error ({error.Exception.GetType().Name}): {error.Exception.Message}");
                    IAsyncDisposable subscription;
                    if (args[0] == "watch-events")
                    {
                        if (args.Length == 2)
                        {
                            subscription = await observed.SubscribeEventsAsync(notifications, PrintEventAsync, cancellationToken);
                        }
                        else
                        {
                            RequireArguments(args, 4, "watch-events <device> <siid> <eiid>");
                            subscription = await notifications.SubscribeEventAsync(observed.Did,
                                int.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture), PrintEventAsync, cancellationToken);
                        }
                    }
                    else if (args.Length == 2)
                    {
                        subscription = await observed.SubscribePropertiesAsync(notifications, PrintPropertyAsync, cancellationToken);
                    }
                    else
                    {
                        RequireArguments(args, 4, "watch <device> <service-name-or-IID> <property-name-or-IID>");
                        var observedService = await observed.GetServiceAsync(args[2], cancellationToken);
                        subscription = await observedService.GetProperty(args[3]).SubscribeAsync(notifications, PrintPropertyAsync, cancellationToken);
                    }
                    await using (subscription)
                    {
                        Console.WriteLine("Subscription connected. Press Ctrl+C to stop.");
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                }
                return 0;
            case "get":
            case "set":
            case "action":
                RequireArguments(args, args[0] == "set" ? 5 : 4, $"{args[0]} <device> <service-name> <property-or-action-name> [json-value]");
                var selected = await FindDeviceAsync(client, args[1], cancellationToken);
                var service = await selected.GetServiceAsync(args[2], cancellationToken);
                if (args[0] == "action")
                {
                    var action = service.GetAction(args[3]);
                    var parameters = args.Length > 4 ? JsonNode.Parse(args[4]) as JsonArray : new JsonArray();
                    if (parameters is null) throw new ArgumentException("Action arguments must be a JSON array.");
                    var response = await action.InvokeAsync(parameters.Select(value => (object?)value), cancellationToken);
                    Console.WriteLine(response.ToJsonString(OutputOptions));
                }
                else
                {
                    var property = service.GetProperty(args[3]);
                    if (args[0] == "get")
                    {
                        var value = await property.GetAsync(cancellationToken);
                        Console.WriteLine(value?.ToJsonString() ?? "null");
                    }
                    else
                    {
                        await property.SetAsync(JsonNode.Parse(args[4]), cancellationToken);
                        Console.WriteLine("Property updated.");
                    }
                }
                return 0;
            case "local-miio":
                var host = GetEnv("MI_LOCAL_HOST") ?? throw new ArgumentException("Set MI_LOCAL_HOST.");
                var token = GetEnv("MI_LOCAL_TOKEN") ?? throw new ArgumentException("Set MI_LOCAL_TOKEN.");
                await using (var local = client.CreateLocalMiioClient(host, token))
                {
                    var parameters = args.Length > 2 ? JsonNode.Parse(args[2]) as JsonArray : new JsonArray();
                    if (parameters is null) throw new ArgumentException("Local command parameters must be a JSON array.");
                    var response = await local.SendCommandAsync(args.Length > 1 ? args[1] : "miIO.info", parameters, cancellationToken);
                    Console.WriteLine(response.ToJsonString(OutputOptions));
                }
                return 0;
            default:
                throw new ArgumentException($"Unknown command {args[0]}. Run help to list commands.");
        }
    }

    private static string GetOAuthTokenPath()
        => Environment.GetEnvironmentVariable("MI_OAUTH_TOKEN_PATH") ?? FileMiOAuthTokenStore.GetDefaultPath();

    private static Task PrintPropertyAsync(MiotPropertyChanged change, CancellationToken cancellationToken)
    {
        Console.WriteLine(JsonSerializer.Serialize(change));
        return Task.CompletedTask;
    }

    private static Task PrintEventAsync(MiotDeviceEvent occurrence, CancellationToken cancellationToken)
    {
        Console.WriteLine(JsonSerializer.Serialize(occurrence));
        return Task.CompletedTask;
    }

    private static async Task<int> LoginOAuthAsync(string[] args, CancellationToken cancellationToken)
    {
        RequireArguments(args, 3, "oauth-login <app-id> <registered-redirect-uri> [cn|de|i2|ru|sg|us]");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(35) };
        var oauth = new MiOAuthClient(http);
        var region = args.Skip(3).FirstOrDefault(value => value != "--manual") ?? "cn";
        var authorization = oauth.CreateAuthorization(args[1], args[2], region);
        var store = new FileMiOAuthTokenStore(GetOAuthTokenPath());
        await store.SaveAuthorizationAsync(authorization, cancellationToken);
        Console.WriteLine("Complete Xiaomi OAuth authorization in the browser.");
        if (args.Contains("--manual", StringComparer.Ordinal))
        {
            Console.WriteLine(authorization.AuthorizationUri.AbsoluteUri);
        }
        else
        {
            try
            {
                Process.Start(new ProcessStartInfo(authorization.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                Console.WriteLine(authorization.AuthorizationUri.AbsoluteUri);
            }
        }
        return await CompleteOAuthAsync(oauth, store, authorization, cancellationToken);
    }

    private static async Task<int> CompleteOAuthAsync(MiOAuthClient oauth, FileMiOAuthTokenStore store,
        MiOAuthAuthorization authorization, CancellationToken cancellationToken)
    {
        Console.WriteLine("Paste the final redirect URL containing code and state, then press Enter.");
        var input = await Console.In.ReadLineAsync(cancellationToken);
        if (!Uri.TryCreate(input, UriKind.Absolute, out var callback))
        {
            throw new ArgumentException("A complete OAuth redirect URL is required.");
        }
        var token = await oauth.CompleteAuthorizationAsync(authorization, callback, cancellationToken);
        await store.SaveAsync(token, cancellationToken);
        await store.ClearAuthorizationAsync(cancellationToken);
        Console.WriteLine($"OAuth login successful. Credentials saved to {store.FilePath}");
        return 0;
    }

    private static async Task<JsonNode> RunSpeakerCommandAsync(MinaDeviceClient speaker, string[] args, CancellationToken cancellationToken)
    {
        switch (args[0])
        {
            case "pause": return await speaker.PauseAsync(cancellationToken);
            case "stop": return await speaker.StopAsync(cancellationToken);
            case "play": return await speaker.PlayAsync(cancellationToken);
            case "status": return await speaker.GetStatusAsync(cancellationToken);
            case "volume":
                RequireArguments(args, 3, "volume <device> <0-100>");
                return await speaker.SetVolumeAsync(int.Parse(args[2], CultureInfo.InvariantCulture), cancellationToken);
            case "tts":
                RequireArguments(args, 3, "tts <device> <text>");
                return await speaker.SpeakAsync(string.Join(' ', args.Skip(2)), cancellationToken);
            case "play-url":
                RequireArguments(args, 3, "play-url <device> <url>");
                return await speaker.PlayUrlAsync(args[2], cancellationToken);
            default: throw new ArgumentException("Unknown speaker command.");
        }
    }

    private static async Task<MinaDeviceClient> FindSpeakerAsync(MiServiceFacade client, string target, CancellationToken cancellationToken)
    {
        var matches = (await client.Mina.GetDevicesAsync(cancellationToken))
            .Where(device => device.DeviceId == target || device.MiotDid == target || device.Name == target).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException("Select exactly one speaker by its name or ID.");
    }

    private static async Task<MiioDeviceClient> FindDeviceAsync(MiServiceFacade client, string target, CancellationToken cancellationToken)
    {
        var matches = (await client.MiioCloud.GetDevicesAsync(cancellationToken: cancellationToken))
            .Where(device => device.Did == target || device.Name == target).ToArray();
        return matches.Length == 1 ? matches[0] : throw new ArgumentException("Select exactly one device by its name or ID.");
    }

    private static void RequireArguments(string[] args, int minimum, string usage)
    {
        if (args.Length < minimum) throw new ArgumentException($"Usage is {usage}");
    }

    private static string? GetEnv(string name)
        => Environment.GetEnvironmentVariable(name) ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    private static void PrintHelp()
    {
        Console.WriteLine("MiServiceSharp Demo (.NET 8)");
        Console.WriteLine("  login [micoapi|xiaomiio]");
        Console.WriteLine("  import-token <token-file>");
        Console.WriteLine("  oauth-login <app-id> <registered-redirect-uri> [region] [--manual]");
        Console.WriteLine("  oauth-complete (resume a pending OAuth authorization)");
        Console.WriteLine("  mina-devices");
        Console.WriteLine("  miio-devices [keyword]");
        Console.WriteLine("  pause|stop|play|status <device-name-or-id>");
        Console.WriteLine("  volume <device-name-or-id> <0-100>");
        Console.WriteLine("  tts <device-name-or-id> <text>");
        Console.WriteLine("  play-url <device-name-or-id> <url>");
        Console.WriteLine("  spec <model-or-URN>");
        Console.WriteLine("  capabilities <device-name-or-id>");
        Console.WriteLine("  watch <device> [service-name-or-IID] [property-name-or-IID]");
        Console.WriteLine("  watch-events <device> [siid] [eiid]");
        Console.WriteLine("  get <device> <service-name-or-IID> <property-name-or-IID>");
        Console.WriteLine("  set <device> <service-name-or-IID> <property-name-or-IID> <json-value>");
        Console.WriteLine("  action <device> <service-name-or-IID> <action-name-or-IID> [json-array]");
        Console.WriteLine("  Full MIoT URNs are also accepted for services, properties and actions.");
        Console.WriteLine("  local-miio [method] [json-array] (MI_LOCAL_HOST and MI_LOCAL_TOKEN)");
    }
}
