using MiServiceSharp.Auth;
using MiServiceSharp.Protocol.LocalMiio;
using MiServiceSharp.Services;
using MiServiceSharp.Storage;
using MiServiceSharp.Tests;
using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Xunit;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var user = GetEnv("MI_USER") ?? string.Empty;
        var pass = GetEnv("MI_PASS") ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(user));
        Assert.False(string.IsNullOrWhiteSpace(pass));

        var tokenFile = Path.Combine(Path.GetTempPath(), $"miservice-integration.json");
        var tokenStore = new FileMiTokenStore(tokenFile);
        using var httpClient = new HttpClient();
        var account = new MiAccountClient(httpClient, new MiAccountOptions
        {
            Username = user,
            Password = pass,
            EnableInteractiveVerification = true
        }, tokenStore);

        await account.InitializeAsync();
        var loginMina = await account.LoginAsync("micoapi");
        var loginMiio = await account.LoginAsync("xiaomiio");

        Assert.True(loginMina, "micoapi 登录失败，可能需要先完成 notificationUrl 的手机验证。");
        Assert.True(loginMiio, "xiaomiio 登录失败，可能需要先完成 notificationUrl 的手机验证。");

        var mina = new MinaService(account);
        var miio = new MiioCloudService(account);
        var minaDevices = await mina.DeviceListAsync();
        var miioDevices = await miio.DeviceListAsync();


        //获取miio设备
        var d = miioDevices.First(x => x.Name.Contains("客厅"));

        //获取设备spec
        var spec = await miio.MiotSpecDataAsync(d.Model);

        var service = spec.Services[1];//Light
        var prop = service.Properties[0];//Switch Status

        //获取和设置设备属性（控制设备、获取设备状态）
        var propv = await miio.MiotGetPropAsync(d.Did, (service.Iid, prop.Iid));
        Console.WriteLine($"{propv}");

        var r = await miio.MiotSetPropAsync(d.Did, (service.Iid, prop.Iid), false);

        //调用设备action（控制设备）
        d = miioDevices.First(x => x.Name.Contains("客厅音"));
        spec = await miio.MiotSpecDataAsync(d.Model);
        service = spec.Services[4];//Intelligent Speaker 服务
        var action = service.Actions[3];//Execute Text Directive action
        var rr = await miio.MiotActionAsync(d.Did, (service.Iid, action.Iid), ["打开客厅灯",true]);

        //获取mina设备
        var dd = minaDevices.First(x => x.Name.Contains("客厅"));
        //await mina.TextToSpeechAsync(dd.DeviceId, "你好，小芮芮");

        //await mina.PlayByMusicUrlAsync(dd.DeviceId, "https://file.xbyham.com/d/cloudflare/%E4%B8%80%E9%97%AA%E4%B8%80%E9%97%AA%E4%BA%AE%E6%99%B6%E6%99%B6.mp3?sign=8TUb1lBj3Q7sSn4IJQvIoqvsRpVwm5cjHb3jSqSI0K4=:0");
        await mina.PlayByUrlAsync(dd.DeviceId, "https://file.xbyham.com/d/cloudflare/02.%20%E6%98%9F%E7%A9%BA%E7%89%A9%E8%AA%9E.flac?sign=JNuv7TXvIwZnwBQkWpXXIizTc_0Zc41DNZfKQ0jnLwg=:0");

        await mina.PlayerStopAsync(dd.DeviceId);
        //测试本地控制（需要正确的 localHost 和 localToken，且设备需要在局域网内）
        var d2 = miioDevices.First();

        var did = d2.Did;//Environment.GetEnvironmentVariable("MI_DID") ?? string.Empty;
        var localHost = d2.InternetIp;//Environment.GetEnvironmentVariable("MI_LOCAL_HOST") ?? string.Empty;
        var localToken = d2.Token;// Environment.GetEnvironmentVariable("MI_LOCAL_TOKEN") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
        {
            Console.WriteLine("请先设置环境变量: MI_USER, MI_PASS");
            return;
        }




        await account.InitializeAsync();

        var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        switch (cmd)
        {
            case "login":
                await HandleLoginAsync(account, args, tokenFile);
                break;

            case "mina-devices":
                await HandleMinaDevicesAsync(account);
                break;

            case "miio-devices":
                await HandleMiioDevicesAsync(account, args);
                break;

            case "tts":
                await HandleTtsAsync(account, args, did);
                break;

            case "play-url":
                await HandlePlayUrlAsync(account, args, did);
                break;

            case "local-miio":
                await HandleLocalMiioAsync(args, localHost, localToken);
                break;

            default:
                PrintHelp();
                break;
        }
    }

    private static string? GetEnv(string name)
    {
        return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process)
               ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
               ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
               ?? Environment.GetEnvironmentVariable(name);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("可用命令:");
        Console.WriteLine("  login [micoapi|xiaomiio]");
        Console.WriteLine("  mina-devices");
        Console.WriteLine("  miio-devices [keyword]");
        Console.WriteLine("  tts <text>");
        Console.WriteLine("  play-url <url>");
        Console.WriteLine("  local-miio [method] [jsonArrayParams]");
    }

    private static async Task HandleLoginAsync(MiAccountClient account, string[] args, string tokenFile)
    {
        var sid = args.Length > 1 ? args[1] : "micoapi";
        var ok = await account.LoginAsync(sid);
        Console.WriteLine($"login({sid}) => {ok}");
        Console.WriteLine($"token file => {tokenFile}");
    }

    private static async Task HandleMinaDevicesAsync(MiAccountClient account)
    {
        var mina = new MinaService(account);
        var devices = await mina.DeviceListAsync();
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Name} | did={device.MiotDid} | deviceId={device.DeviceId} | hw={device.Hardware}");
        }
    }

    private static async Task HandleMiioDevicesAsync(MiAccountClient account, string[] args)
    {
        var miio = new MiioCloudService(account);
        var devices = await miio.DeviceListAsync(name: args.Length > 1 ? args[1] : null);
        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Name} | model={device.Model} | did={device.Did} | token={device.Token}");
        }
    }

    private static async Task HandleTtsAsync(MiAccountClient account, string[] args, string did)
    {
        if (string.IsNullOrWhiteSpace(did))
        {
            Console.WriteLine("请设置 MI_DID（miotDid）");
            return;
        }

        var text = args.Length > 1 ? string.Join(' ', args.Skip(1)) : "你好，这是 MiServiceSharp";
        var mina = new MinaService(account);
        var devices = await mina.DeviceListAsync();
        var target = devices.FirstOrDefault(x => x.MiotDid == did || x.Name == did);
        if (target is null)
        {
            Console.WriteLine($"找不到设备: {did}");
            return;
        }

        var result = await mina.TextToSpeechAsync(target.DeviceId, text);
        Console.WriteLine(result.ToJsonString());
    }

    private static async Task HandlePlayUrlAsync(MiAccountClient account, string[] args, string did)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: play-url <url>");
            return;
        }

        if (string.IsNullOrWhiteSpace(did))
        {
            Console.WriteLine("请设置 MI_DID（miotDid）");
            return;
        }

        var url = args[1];
        var mina = new MinaService(account);
        var devices = await mina.DeviceListAsync();
        var target = devices.FirstOrDefault(x => x.MiotDid == did || x.Name == did);
        if (target is null)
        {
            Console.WriteLine($"找不到设备: {did}");
            return;
        }

        var result = await mina.PlayByUrlAsync(target.DeviceId, url);
        Console.WriteLine(result.ToJsonString());
    }

    private static async Task HandleLocalMiioAsync(string[] args, string localHost, string localToken)
    {
        if (string.IsNullOrWhiteSpace(localHost) || string.IsNullOrWhiteSpace(localToken))
        {
            Console.WriteLine("请设置 MI_LOCAL_HOST 和 MI_LOCAL_TOKEN（32位hex）");
            return;
        }

        var method = args.Length > 1 ? args[1] : "miIO.info";
        var paramNode = args.Length > 2 ? JsonNode.Parse(args[2]) : new JsonArray();
        var parameters = paramNode as JsonArray ?? new JsonArray();

        await using var local = new LocalMiioClient(localHost, localToken);
        var hello = await local.HandshakeAsync();
        Console.WriteLine($"local hello => deviceId={hello.DeviceId}, stamp={hello.Stamp}");

        var result = await local.SendCommandAsync(method, parameters);
        Console.WriteLine(result.ToJsonString());
    }
}
