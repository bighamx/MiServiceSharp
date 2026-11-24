# MiServiceSharp

面向 .NET 8 的小米设备 C# 类库，提供账号登录、米家设备查询、MIoT 属性与动作调用、音箱控制、局域网 miIO 和云端消息订阅。

设备对象绑定自己的设备 ID，调用方式为 `device → service → property/action`。每台设备按自身型号加载 MIoT Spec，并据此检查属性权限、值类型和动作参数。

## 目录

- [SDK 类库用法](#sdk-类库用法)
  - [阅读与运行示例](#阅读与运行示例)
  - [初始化客户端](#初始化客户端)
  - [获取设备、读写属性](#获取设备读写属性)
  - [枚举能力与调用动作](#枚举能力与调用动作)
  - [音箱控制](#音箱控制)
  - [MIoT 型号与能力](#miot-型号与能力)
  - [云端消息订阅](#云端消息订阅)
- [CLI 用法](#cli-用法)
  - [登录与导入凭证](#登录与导入凭证)
  - [查询设备与能力](#查询设备与能力)
  - [读取与控制](#读取与控制)
  - [OAuth 授权](#oauth-授权)
  - [订阅属性与事件](#订阅属性与事件)
  - [命令参考](#命令参考)
  - [JSON 参数与局域网命令](#json-参数与局域网命令)
- [凭证与配置](#凭证与配置)
- [WebAPI](#webapi)
- [构建与测试](#构建与测试)
- [常见问题](#常见问题)
- [项目结构](#项目结构)

## SDK 类库用法

在自己的 .NET 8 项目中引用 `MiServiceSharp/MiServiceSharp.csproj`。`MiServiceFacade` 提供米家云端与音箱服务入口，获取设备后即可通过绑定的设备对象操作。

### 阅读与运行示例

`MiServiceSharp.Examples` 按设备类别组织示例。在 [Program.cs](MiServiceSharp.Examples/Program.cs) 中可以看到每种设备的选择方式，以及相应示例方法的调用。设备名称和型号直接在代码中修改。

入口依次查询灯、插座、插排、空调、净化器、新风机、摄像机、温湿度、门磁、燃气传感器、网关、热水器、油烟机和音箱；扫地机器人在线时查询状态与电量。每类设备文件还提供对应的控制、动作或事件订阅方法。

```csharp
var devices = await DeviceExamples.ListAsync(client, stopping.Token);

var light = devices.Single(device => device.Name == "客厅灯");
await LightExamples.ReadStateAsync(light, stopping.Token);

var airConditioner = devices.Single(device => device.Model == "xiaomi.aircondition.mc6");
await AirConditionerExamples.ReadStateAsync(airConditioner, stopping.Token);

var sensor = devices.Single(device => device.Name == "小米温湿度传感器");
await TemperatureHumidityExamples.ReadStateAsync(sensor, stopping.Token);
```

在 IDE 中启动 `MiServiceSharp.Examples`，或直接运行项目。

```powershell
dotnet run --project .\MiServiceSharp.Examples
```

| 设备类别 | 示例源码 | 具体用法 |
| --- | --- | --- |
| 灯 | [LightExamples.cs](MiServiceSharp.Examples/LightExamples.cs) | 开关、亮度、色温读取与设置，`toggle` 动作 |
| 插座、插排 | [PlugExamples.cs](MiServiceSharp.Examples/PlugExamples.cs) | 开关、温度、功率、指示灯，插排 `toggle` 动作 |
| 空调 | [AirConditionerExamples.cs](MiServiceSharp.Examples/AirConditionerExamples.cs) | 工作模式、目标温度、室温、风速、扫风及制冷设置 |
| 空气净化器 | [AirPurifierExamples.cs](MiServiceSharp.Examples/AirPurifierExamples.cs) | 空气质量、滤芯寿命、自动模式、童锁 |
| 新风机 | [FreshAirExamples.cs](MiServiceSharp.Examples/FreshAirExamples.cs) | PM2.5、CO₂、风量、多个同名滤芯服务的 IID 定位 |
| 摄像机 | [CameraExamples.cs](MiServiceSharp.Examples/CameraExamples.cs) | 夜视、移动追踪、存储卡状态、检测设置、人形移动事件 |
| 温湿度传感器 | [TemperatureHumidityExamples.cs](MiServiceSharp.Examples/TemperatureHumidityExamples.cs) | 读取温度与相对湿度 |
| 门磁 | [DoorSensorExamples.cs](MiServiceSharp.Examples/DoorSensorExamples.cs) | 读取触点状态、订阅开门事件 |
| 燃气传感器 | [GasSensorExamples.cs](MiServiceSharp.Examples/GasSensorExamples.cs) | 读取运行状态和浓度 |
| 网关 | [GatewayExamples.cs](MiServiceSharp.Examples/GatewayExamples.cs) | 布防状态、布防设置、按 ParentId 关联子设备 |
| 扫地机器人 | [RobotVacuumExamples.cs](MiServiceSharp.Examples/RobotVacuumExamples.cs) | 状态、电量、清扫模式，开始、暂停和回充动作 |
| 热水器 | [WaterHeaterExamples.cs](MiServiceSharp.Examples/WaterHeaterExamples.cs) | 当前温度、目标温度、工作状态及目标温度设置 |
| 油烟机 | [RangeHoodExamples.cs](MiServiceSharp.Examples/RangeHoodExamples.cs) | 开关状态、开启后的风速读取与高档设置 |
| 音箱 | [SpeakerExamples.cs](MiServiceSharp.Examples/SpeakerExamples.cs) | MiNA 播放状态、音量和语音播报 |
| 音箱 MIoT 动作 | [ActionExamples.cs](MiServiceSharp.Examples/ActionExamples.cs) | `play-text` 文本参数动作、无参数 `pause` 动作 |

通用场景提供独立示例。

| 场景 | 示例源码 | 具体用法 |
| --- | --- | --- |
| 账号登录 | [AccountExamples.cs](MiServiceSharp.Examples/AccountExamples.cs) | 登录米家与音箱服务并保存凭证 |
| 能力枚举 | [DeviceExamples.cs](MiServiceSharp.Examples/DeviceExamples.cs) | 枚举 service、property、action、event 及动作输入、输出参数 |
| OAuth 授权 | [OAuthExamples.cs](MiServiceSharp.Examples/OAuthExamples.cs) | 启动 HTTP 监听，接收浏览器回调、校验 state、交换并保存凭证 |
| 多设备订阅 | [SubscriptionExamples.cs](MiServiceSharp.Examples/SubscriptionExamples.cs) | 灯属性、温度、燃气状态、门磁及摄像机事件共用 MQTT 连接 |
| 局域网访问 | [LocalExamples.cs](MiServiceSharp.Examples/LocalExamples.cs) | 从清单取得 IP 和 Token，直接读取本地 MIoT 或传统 miIO 属性 |

控制方法可在入口中直接调用，例如 `await LightExamples.SetLightingAsync(light, stopping.Token)` 或 `await RobotVacuumExamples.ReturnToDockAsync(robot, stopping.Token)`。写入和动作会操作所选设备，属性名称、枚举值及 IID 以具体型号 Spec 为准。订阅示例需要先完成 [OAuth 授权](#oauth-授权)，按 Ctrl+C 取消并释放订阅。

### 初始化客户端

以下初始化代码供本节设备调用片段共用。账号和密码从环境变量读取，`InitializeAsync` 加载已有凭证。凭证位置可通过 [凭证与配置](#凭证与配置) 设置。

```csharp
using MiServiceSharp;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

using var http = new HttpClient();
var account = new MiAccountClient(http, new MiAccountOptions
{
    Username = Environment.GetEnvironmentVariable("MI_USER") ?? string.Empty,
    Password = Environment.GetEnvironmentVariable("MI_PASS") ?? string.Empty
}, new FileMiTokenStore(FileMiTokenStore.GetDefaultPath()));

await account.InitializeAsync();
var client = new MiServiceFacade(account);
```

首次登录时，在创建客户端后登录所需服务，成功后凭证由 Store 保存。米家设备使用 `xiaomiio`，音箱使用 `micoapi`。

```csharp
foreach (var sid in new[] { "xiaomiio", "micoapi" })
{
    if (!await account.LoginAsync(sid))
    {
        throw new InvalidOperationException(account.LastLoginError ?? $"服务 {sid} 登录失败。");
    }
}
```

### 获取设备、读写属性

示例根据设备实际型号查找 `light` 服务和 `on` 属性。名称、IID 和完整 URN 都通过该设备的 Spec 解析。

```csharp
var devices = await client.MiioCloud.GetDevicesAsync();
var light = devices.Single(device => device.Name == "客厅灯");
var service = await light.GetServiceAsync("light");
var power = service.GetProperty("on");

var current = await power.GetAsync();
Console.WriteLine($"开关状态 {current}");
await power.SetAsync(true);
```

### 枚举能力与调用动作

```csharp
foreach (var capability in await light.GetServicesAsync())
{
    Console.WriteLine($"服务 {capability.Iid} {capability.Name}");
    foreach (var property in capability.Properties)
    {
        Console.WriteLine(
            $"  属性 {property.Iid} {property.Name} " +
            $"R={property.CanRead} W={property.CanWrite} N={property.CanNotify}");
    }
    foreach (var action in capability.Actions)
    {
        Console.WriteLine($"  动作 {action.Iid} {action.Name}");
    }
}
```

每个设备的服务对象分别提供 `Properties` 和 `Actions`。`service.GetProperty(...)` 获取属性对象，`service.GetAction(...)` 获取动作对象。

```csharp
var lightService = await light.GetServiceAsync("light");
var toggle = lightService.GetAction("toggle");
var toggleResult = await toggle.InvokeAsync();

var robot = devices.Single(device => device.Name == "扫地机器人");
var battery = await robot.GetServiceAsync("battery");
var charge = battery.GetAction("start-charge");
var chargeResult = await charge.InvokeAsync();
```

有参数动作的参数类型为 `IEnumerable<object?>`，按照 `InputProperties` 顺序传入。无参数动作直接调用 `InvokeAsync()`。动作定义中的 `Definition.Out` 列出输出属性 IID，返回的 `JsonNode` 保留云端响应及输出参数。带文本参数的动作见 [ActionExamples.cs](MiServiceSharp.Examples/ActionExamples.cs)。

### 音箱控制

```csharp
var speakers = await client.Mina.GetDevicesAsync();
var speaker = speakers.Single(device => device.Name == "客厅音箱");
await speaker.PauseAsync();
await speaker.SetVolumeAsync(40);
var status = await speaker.GetStatusAsync();
```

### MIoT 型号与能力

同类设备通过标准服务、属性和动作名称描述能力，具体功能由每个型号的 Spec 决定。服务 IID、属性 IID、动作 IID、值范围、枚举和权限均按设备自己的定义解析。

| 标记 | 代码属性 | 含义 |
| --- | --- | --- |
| R | `CanRead` | 可读取当前值 |
| W | `CanWrite` | 可写入值 |
| N | `CanNotify` | 支持属性变化上报 |

属性的完整定义通过 `Definition` 获取。写入时检查类型、数值范围、步长和枚举，动作调用时检查参数数量与类型。同名能力出现多次时，使用 IID 或完整 URN 定位。

当前代码通过以下地址在线获取 Spec，型号索引缓存到系统临时目录，设备对象缓存已加载的型号定义。

```text
http://miot-spec.org/miot-spec-v2/instances?status=all
http://miot-spec.org/miot-spec-v2/instance?type=<完整类型URN>
```

### 云端消息订阅

订阅使用 OAuth 凭证和 MQTT 5/TLS。属性变化回调提供 DID、SIID、PIID、JSON 值和接收时间，设备事件回调提供 DID、SIID、EIID、参数数组和接收时间。

#### 订阅属性

以下代码可作为控制台项目的 `Program.cs`。先准备账号凭证并完成 [OAuth 授权](#oauth-授权)，再设置两个 Token 文件的实际路径，并替换设备名称。

```csharp
using MiServiceSharp;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    stopping.Cancel();
};

using var http = new HttpClient();
var account = new MiAccountClient(http, new MiAccountOptions(),
    new FileMiTokenStore(FileMiTokenStore.GetDefaultPath()));
await account.InitializeAsync(stopping.Token);
var client = new MiServiceFacade(account);

var tokens = new MiOAuthTokenProvider(new MiOAuthClient(http),
    new FileMiOAuthTokenStore(FileMiOAuthTokenStore.GetDefaultPath()));
await using var notifications = client.CreateSubscriptionClient(tokens);
notifications.Error += (_, error) =>
{
    Console.Error.WriteLine(error.Exception.Message);
};

var devices = await client.MiioCloud.GetDevicesAsync(cancellationToken: stopping.Token);
var light = devices.Single(device => device.Name == "客厅灯");
var power = (await light.GetServiceAsync("light", stopping.Token)).GetProperty("on");

try
{
    await using var subscription = await power.SubscribeAsync(
        notifications,
        (change, callbackToken) =>
        {
            Console.WriteLine($"{change.DeviceId} {change.Siid}/{change.Piid} = {change.Value}");
            return Task.CompletedTask;
        },
        stopping.Token);

    Console.WriteLine("正在监听，按 Ctrl+C 结束。");
    await Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
}
```

订阅建立时自动连接。需要初始状态时，另外调用 `await power.GetAsync()`。

#### 订阅全部属性与事件

订阅整个设备时，可将上例中的 `power.SubscribeAsync(...)` 替换为以下任一调用，沿用相同的连接及等待逻辑。

```csharp
// 全部属性变化
await using var properties = await light.SubscribePropertiesAsync(
    notifications,
    (change, token) =>
    {
        Console.WriteLine($"属性 {change.Siid}/{change.Piid} = {change.Value}");
        return Task.CompletedTask;
    });

// 全部设备事件
await using var events = await light.SubscribeEventsAsync(
    notifications,
    (message, token) =>
    {
        Console.WriteLine($"事件 {message.Siid}/{message.Eiid} {message.Arguments}");
        return Task.CompletedTask;
    });
```

单个事件使用 `notifications.SubscribeEventAsync(did, siid, eiid, handler)`。

#### 生命周期与连接配置

- 订阅句柄和订阅客户端均实现 `IAsyncDisposable`，使用 `await using` 释放。
- 同一 Topic 的监听器共享 Broker 订阅，最后一个监听器释放时取消该 Topic。
- 断线后自动重连并恢复有效订阅，OAuth Token 接近过期时自动刷新。
- 回调按顺序在独立分发任务中执行，支持回调内取消订阅。耗时工作应异步处理并响应回调的取消令牌。
- MQTT QoS 1 可能重复投递。默认消息缓冲容量为 1024，缓冲满或回调失败会触发 `Error`。
- `CreateSubscriptionClient(tokens, options)` 接受 `MiotSubscriptionOptions`，可配置主机、端口、TLS、超时、重连间隔和缓冲容量。默认端口 `8883`，启用 TLS 与系统证书验证。

## CLI 用法

CLI 项目为 `MiServiceSharp.Cli`，默认发布 Windows x64 单文件 `micli.exe`，应用代码、MiServiceSharp 类库与依赖程序集打包到该文件，运行环境需要安装 .NET 8 运行时。安装 .NET 8 SDK，在仓库根目录发布并将发布目录加入当前 PowerShell 会话的 PATH。

```powershell
dotnet publish .\MiServiceSharp.Cli --configuration Release
$cliDirectory = (Resolve-Path .\MiServiceSharp.Cli\bin\Release\net8.0\win-x64\publish).Path
$env:PATH = $cliDirectory + ';' + $env:PATH
micli.exe help
```

统一运行形式为 `micli.exe <命令> <参数>`。以下终端示例使用 PowerShell。

`<device>` 接收设备名称或 ID。音箱命令接受 `deviceId`、`miotDid` 或名称，米家设备命令接受 DID 或名称。名称重复时使用 ID。

`<service>`、`<property>` 和 `<action>` 接收名称、数字 IID 或完整 URN。下表中的尖括号表示需要替换的参数。

### 登录与导入凭证

```powershell
$env:MI_USER = "你的小米账号"
$env:MI_PASS = "你的小米密码"
micli.exe login
```

默认登录 `micoapi` 和 `xiaomiio`，分别用于音箱与米家云端接口。需要安全验证时，按终端提示在浏览器完成验证，再继续登录。

成功后凭证保存到仓库根目录 `.mi.token.json`，后续调用会加载已有凭证。

也可以导入 Python MiService 已有的 Token。请将路径替换为实际位置。

```powershell
micli.exe import-token E:\GIT\MiService\.mi.token
```

### 查询设备与能力

```powershell
# 米家设备，输出名称、型号、DID 和在线状态
micli.exe miio-devices

# 音箱设备，输出名称、miotDid 和 deviceId
micli.exe mina-devices

# 查看指定设备的型号能力
micli.exe capabilities "客厅灯"
```

### 读取与控制

以下示例要求设备提供对应能力，请先根据 `capabilities` 的结果确认服务和属性。控制命令会操作真实设备。

```powershell
# 读取灯的开关状态
micli.exe get "客厅灯" light on

# 开灯、关灯
micli.exe set "客厅灯" light on true
micli.exe set "客厅灯" light on false

# 音箱播放状态与音量
micli.exe status "客厅音箱"
micli.exe volume "客厅音箱" 40
```

### OAuth 授权

使用官方 Xiaomi Home（Home Assistant）应用的现成授权入口。授权账号应为设备所属账号，页面会显示该应用的身份与权限范围。以下命令使用中国区 `cn`，其他支持的区域为 `de`、`i2`、`ru`、`sg`、`us`。

```powershell
micli.exe oauth-login 2882303761520251711 http://homeassistant.local:8123 cn
```

CLI 自动补齐该应用的 `/api/webhook/<随机ID>` 路径，生成设备绑定的 `state`，启动 HTTP 回调监听后打开浏览器。确认授权后，程序自动接收回调、校验路径与 `state`、交换凭证并保存到 `.mi.oauth.json`。监听最长等待 10 分钟，Ctrl+C 可取消。

HA 应用使用已登记的 `homeassistant.local` 回调域名。CLI 会提示 hosts 文件的位置和映射内容。若该域名尚未解析到 CLI 主机，请在浏览器所在机器以管理员权限编辑 `C:\Windows\System32\drivers\etc\hosts`。

浏览器与 CLI 在同一台机器时，设置或更新以下映射。

```text
127.0.0.1 homeassistant.local
```

浏览器与 CLI 在不同机器时，将 `127.0.0.1` 替换为浏览器可访问的 CLI 主机 IP，并确保 TCP `8123` 可达。使用自己登记的 OAuth 应用时，可将参数替换为该应用已登记的 HTTP 回调地址；登记地址使用 IP 时，监听服务可直接绑定该 IP。

授权发起时的上下文保存在 `.mi.oauth.pending.json`。成功保存凭证后会清理该文件。进程中断后，可通过 `oauth-complete` 粘贴同一次授权的完整回调地址以继续交换凭证。

```powershell
micli.exe oauth-complete
```

需要手动打开授权页和粘贴回调地址时，在 `oauth-login` 后追加 `--manual`。该模式适用于已有回调服务或 HTTPS 回调地址。

### 订阅属性与事件

```powershell
# 单个属性，服务与属性也可以使用数字 IID
micli.exe watch "客厅灯" light on

# 全部属性
micli.exe watch "客厅灯"

# 全部事件或一个指定事件
micli.exe watch-events "设备名称"
micli.exe watch-events "设备名称" <siid> <eiid>
```

按 Ctrl+C 取消订阅并关闭连接。实际推送取决于设备能力、在线状态及变化上报。

### 命令参考

| 命令 | 用途 |
| --- | --- |
| `help` | 查看命令帮助 |
| `login [micoapi|xiaomiio]` | 登录指定服务，省略时登录两个服务 |
| `import-token <token-file>` | 导入已有凭证 |
| `mina-devices` | 列出音箱 |
| `miio-devices [keyword]` | 列出米家设备，可按关键字过滤 |
| `pause <device>` / `stop <device>` / `play <device>` | 暂停、停止、播放 |
| `status <device>` | 查询音箱播放状态 |
| `volume <device> <0-100>` | 设置音箱音量 |
| `tts <device> <text>` | 音箱语音播报 |
| `play-url <device> <url>` | 音箱播放 URL |
| `spec <model-or-URN>` | 获取型号或完整 URN 对应的 Spec |
| `capabilities <device>` | 获取设备的完整 Spec |
| `get <device> <service> <property>` | 读取属性 |
| `set <device> <service> <property> <json-value>` | 写入属性 |
| `action <device> <service> <action> [json-array]` | 调用动作 |
| `oauth-login <app-id> <redirect-uri> [region] [--manual]` | 浏览器 OAuth 授权 |
| `oauth-complete` | 恢复待完成的 OAuth 授权 |
| `watch <device> [service property]` | 订阅全部属性或一个指定属性 |
| `watch-events <device> [siid eiid]` | 订阅全部事件或一个指定事件 |
| `local-miio [method] [json-array]` | 执行局域网 miIO 命令 |

### JSON 参数与局域网命令

`set` 按 JSON 解析值，布尔值使用 `true` / `false`，数值使用 `40`，字符串需要包含 JSON 双引号。PowerShell 7 示例中的外层单引号用于保留 JSON 文本。

```powershell
# 数字 IID 的用法，具体含义以该型号 Spec 为准
micli.exe get "客厅灯" 2 1

# 字符串和动作参数的传参形式，将设备与能力名称替换为实际值
micli.exe set "设备名称" "服务名称" "字符串属性名称" '"hello"'
micli.exe action "设备名称" "服务名称" "动作名称" '[1,"hello"]'

# 局域网 miIO，Token 为设备的十六进制 Token
$env:MI_LOCAL_HOST = "192.168.1.100"
$env:MI_LOCAL_TOKEN = "设备Token"
micli.exe local-miio miIO.info
```

命令失败返回非零退出码，Ctrl+C 取消操作时返回 `130`。

## 凭证与配置

| 配置 | 用途与默认行为 |
| --- | --- |
| `MI_USER` / `MI_PASS` | CLI 与示例项目登录使用的小米账号和密码 |
| `MI_TOKEN_PATH` | `FileMiTokenStore.GetDefaultPath()` 使用的账号凭证路径 |
| `MI_OAUTH_TOKEN_PATH` | CLI 与示例项目使用的 OAuth 凭证路径 |
| `MI_LOCAL_HOST` / `MI_LOCAL_TOKEN` | CLI 局域网 miIO 的地址与设备 Token |

账号凭证默认从程序输出目录向上查找 `MiServiceSharp.sln` 所在目录，并使用其中的 `.mi.token.json`。发布到独立目录后，默认使用程序所在目录，也可显式指定路径。

OAuth 默认文件 `.mi.oauth.json` 位于默认账号凭证路径的同级目录。类库中需要自定义 OAuth 路径时，显式传给 `new FileMiOAuthTokenStore(path)`。`MI_OAUTH_TOKEN_PATH` 由 CLI 与示例项目读取。

账号凭证用于查询与控制，OAuth 凭证用于订阅。凭证文件及其临时文件已加入 Git 忽略规则。

## WebAPI

```powershell
dotnet run --project .\MiServiceSharp.Api
```

开发环境通过 `/swagger` 查看完整请求模型及接口。请求示例见 [MiServiceSharp.Api.http](MiServiceSharp.Api/MiServiceSharp.Api.http)。

API 默认配置 `MiService:TokenFile` 为 `./data/.mi.token.json`，相对路径按进程工作目录解析。可通过环境变量 `MiService__TokenFile` 指向已有账号凭证。API Key 和 JWT 配置位于 `Security` 配置节，可通过 `Security__ApiKey`、`Security__JwtSigningKey` 等环境变量设置。

### 接口

下表路径统一以 `/api/v1` 为前缀。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| GET | `/health` | 健康检查 |
| POST | `/auth/login` | 登录并保存账号凭证 |
| POST | `/auth/login/challenge/start` | 创建安全验证会话 |
| POST | `/auth/login/challenge/continue` | 完成浏览器验证后继续登录 |
| GET | `/auth/login/challenge/{sessionId}` | 查询验证会话状态 |
| POST | `/auth/jwt` | 登录并签发 JWT |
| GET | `/auth/token` | 读取账号凭证 |
| POST | `/auth/token/clear` | 清除账号凭证 |
| GET | `/mina/devices` | 查询音箱 |
| POST | `/mina/tts`、`/mina/play-url`、`/mina/player-op` | 播报、播放 URL 与播放器操作 |
| GET | `/miio/devices` | 查询米家设备及设备 Token |
| POST | `/miio/home-rpc` | 通用米家云端 RPC |
| POST | `/miio/home/get-props`、`/miio/home/get-prop` | 读取 miIO 属性 |
| POST | `/miio/home/set-props`、`/miio/home/set-prop` | 写入 miIO 属性 |
| POST | `/miio/miot/get-props`、`/miio/miot/get-prop` | 读取 MIoT 属性 |
| POST | `/miio/miot/set-props`、`/miio/miot/set-prop` | 写入 MIoT 属性 |
| POST | `/miio/miot/action` | 调用 MIoT 动作 |
| POST | `/miio/miot-spec` | 获取 Spec |
| POST | `/miio/local/command` | 局域网 miIO 命令 |

### 鉴权与安全验证流程

以下路径允许匿名访问。

- `GET /api/v1/health`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/jwt`
- `POST /api/v1/auth/login/challenge/start`
- `POST /api/v1/auth/login/challenge/continue`

其余 `/api/**` 请求携带 `X-API-Key: <Security.ApiKey>` 或 `Authorization: Bearer <jwt>`。服务异常统一返回 ProblemDetails。

小米账号要求安全验证时，按以下流程操作。

1. 调用 `/api/v1/auth/login/challenge/start`，取得 `202`、`sessionId` 和 `verificationUrl`。
2. 用户在浏览器完成验证。
3. 调用 `/api/v1/auth/login/challenge/continue`。
4. 携带 API Key 或 JWT 查询 `/api/v1/auth/login/challenge/{sessionId}`，状态为 `succeeded` 后调用设备接口。

## 构建与测试

```powershell
dotnet build MiServiceSharp.sln --configuration Release
dotnet test MiServiceSharp.sln --configuration Release
```

默认测试覆盖账号恢复、凭证存储、设备能力解析、值校验、OAuth 流程及本地 MQTT Broker 订阅。真实账号和云端测试由环境变量启用。

```powershell
# 使用已有凭证，或先设置 MI_USER / MI_PASS
$env:RUN_MI_INTEGRATION = "1"
dotnet test MiServiceSharp.sln --filter FullyQualifiedName~IntegrationSmokeTests
Remove-Item Env:RUN_MI_INTEGRATION

# 先完成 OAuth 授权，再填入已授权设备的 DID
$env:RUN_MI_OAUTH_INTEGRATION = "1"
$env:MI_OAUTH_TEST_DEVICE = "已授权设备DID"
dotnet test MiServiceSharp.sln --filter FullyQualifiedName~OAuthCloudIntegrationTests
Remove-Item Env:RUN_MI_OAUTH_INTEGRATION
Remove-Item Env:MI_OAUTH_TEST_DEVICE
```

OAuth 集成测试会刷新并保存凭证，验证 MQTT 连接与订阅建立。实际属性推送可通过 `watch` 观察，并在米家 App 或设备端改变对应属性。

## 常见问题

### 提示设备匹配数量为 0 或大于 1

先用 `miio-devices` 或 `mina-devices` 确认名称与 ID。同名设备使用对应 ID 精确定位。

### 提示服务、属性或动作匹配失败

运行 `capabilities <device>` 查看当前型号的定义，按输出中的名称、IID 或完整 URN 调用。同类设备的能力和 IID 应以各自型号为准。

### 属性带 N，如何获取变化

完成 OAuth 授权后调用属性对象的 `SubscribeAsync`，或者运行 `watch <device> <service> <property>`。可通过 `GetAsync` 获取初始值，再处理后续变化。

### OAuth 授权中断或授权码失效

保留完整回调地址，使用 `oauth-complete` 恢复待完成请求。授权码失效或已被消费时，重新运行 `oauth-login` 完成新一轮授权。

### 从输出目录启动时找不到凭证

确认程序位于仓库目录内，或设置 `MI_TOKEN_PATH` 和 `MI_OAUTH_TOKEN_PATH`。自己的应用可直接为两个 Store 传入绝对路径。

## 项目结构

| 目录或文件 | 用途 |
| --- | --- |
| `MiServiceSharp/Auth/` | 小米账号与 OAuth 认证 |
| `MiServiceSharp/Storage/` | 凭证存储 |
| `MiServiceSharp/Devices/` | 设备、服务、属性与动作对象 |
| `MiServiceSharp/Services/` | MiNA、米家云端协议 |
| `MiServiceSharp/Subscriptions/` | MQTT 属性与事件订阅 |
| `MiServiceSharp/Protocol/LocalMiio/` | 局域网 miIO UDP 协议 |
| `MiServiceSharp/Models/` | 响应模型与 Spec 定义 |
| `MiServiceSharp/MiServiceFacade.cs` | 类库聚合入口 |
| `MiServiceSharp.Cli/` | `micli.exe` 命令行工具 |
| `MiServiceSharp.Examples/` | 可运行的 SDK 使用示例 |
| `MiServiceSharp.Api/` | HTTP API |
| `MiServiceSharp.Tests/` | 单元与集成测试 |

## 许可证

本项目采用 [Selective Freedom License (SFL) v1.0](https://github.com/bighamx/MIT-NoHuawei) 授权，详见 [LICENSE](LICENSE)。
