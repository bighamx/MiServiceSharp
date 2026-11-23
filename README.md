# MiServiceSharp

基于 `MiService` 思路重写的 C# 类库，目标是：

- 小米账号登录与 token 文件存储
- MiNA 云端接口（音箱控制）
- MiIO 云端接口（米家云协议）
- 本地 miIO UDP 协议（局域网控制）
- 可运行 Demo 与单元/集成测试模板

## 项目结构

- `MiServiceSharp/`
  - `Auth/` 认证与登录
  - `Storage/` token 文件存储
  - `Services/` 云端协议能力（MiNA / MiIO）
  - `Protocol/LocalMiio/` 本地 miIO 协议
  - `Models/` 模型定义
  - `Devices/` 绑定设备与 MIoT 能力的对象
  - `MiServiceFacade.cs` 聚合入口
- `MiServiceSharp.Demo/` 命令行演示
- `MiServiceSharp.Api/` WebAPI 封装层（OpenAPI）
- `MiServiceSharp.Tests/` 单元测试与集成测试模板

## 快速开始

所有项目均使用 `.NET 8`。在仓库根目录构建并运行 Demo。

```powershell
dotnet build MiServiceSharp.sln
dotnet run --project .\MiServiceSharp.Demo -- help
```

首次登录时设置账号和密码。已有有效令牌时可直接查询设备。

```powershell
$env:MI_USER = "你的小米账号"
$env:MI_PASS = "你的小米密码"
dotnet run --project .\MiServiceSharp.Demo -- login
```

令牌默认保存在仓库根目录的 `.mi.token.json`，也可通过 `MI_TOKEN_PATH` 指定位置。
Demo 从编译输出目录运行时也会定位到同一个仓库令牌。
可以导入已有的 Python MiService 令牌，导入结果写入 C# 项目的令牌文件。

```powershell
dotnet run --project .\MiServiceSharp.Demo -- import-token E:\GIT\MiService\.mi.token
```

### Demo 命令

每条设备命令显式接收设备名称或 ID。
服务、属性和动作支持名称、数字 IID 或完整 URN。

```powershell
dotnet run --project .\MiServiceSharp.Demo -- mina-devices
dotnet run --project .\MiServiceSharp.Demo -- miio-devices
dotnet run --project .\MiServiceSharp.Demo -- status "客厅红外音箱"
dotnet run --project .\MiServiceSharp.Demo -- pause "客厅音箱"
dotnet run --project .\MiServiceSharp.Demo -- stop "主卧音箱"
dotnet run --project .\MiServiceSharp.Demo -- volume "客厅红外音箱" 40
dotnet run --project .\MiServiceSharp.Demo -- capabilities "客厅红外音箱"
dotnet run --project .\MiServiceSharp.Demo -- get "客厅红外音箱" speaker volume
dotnet run --project .\MiServiceSharp.Demo -- get "客厅红外音箱" 2 1
dotnet run --project .\MiServiceSharp.Demo -- set "客厅红外音箱" speaker volume 40
dotnet run --project .\MiServiceSharp.Demo -- spec xiaomi.wifispeaker.lx5a
```

`set` 的值使用 JSON 类型，例如 `true`、`40` 或带 JSON 引号的字符串。
`action <设备> <服务> <动作> [JSON参数数组]` 根据该型号 Spec 检查参数数量和类型。
`local-miio [方法] [JSON参数数组]` 使用 `MI_LOCAL_HOST` 和 `MI_LOCAL_TOKEN` 连接局域网设备。
命令执行失败返回非零退出码，运行 `help` 可查看完整命令。

### 面向对象的设备调用

```csharp
using MiServiceSharp;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

using var httpClient = new HttpClient();
var account = new MiAccountClient(httpClient, new MiAccountOptions(),
    new FileMiTokenStore(FileMiTokenStore.GetDefaultPath()));
await account.InitializeAsync();
var client = new MiServiceFacade(account);

var speakers = await client.Mina.GetDevicesAsync();
var livingRoom = speakers.Single(device => device.Name == "客厅音箱");
var bedroom = speakers.Single(device => device.Name == "主卧音箱");
await livingRoom.PauseAsync();
await bedroom.StopAsync();
```

每个设备对象绑定自己的设备 ID。原有接收设备 ID 的底层服务方法继续提供。
MIoT 设备按自身型号加载 Spec，服务、属性与动作通过实例描述映射到对应 IID。

```csharp
var devices = await client.MiioCloud.GetDevicesAsync();
var speaker = devices.Single(device => device.Name == "客厅红外音箱");
var service = await speaker.GetServiceAsync("speaker");
var volume = service.GetProperty("volume");
var currentVolume = await volume.GetAsync();
await volume.SetAsync(40);

foreach (var capability in await speaker.GetServicesAsync())
{
    Console.WriteLine($"{capability.Iid} {capability.Name}");
}
```

属性对象提供 `CanRead`、`CanWrite`、`CanNotify` 和完整 `Definition`，写入时检查格式、范围、步长与枚举值。
Spec 中的 R、W、N 分别对应读取、写入和属性变化上报。通知接收通过下面的 OAuth/MQTT 订阅接口实现。
动作对象提供 `InputProperties`，调用 `InvokeAsync` 后返回含输出参数的响应。
同名能力可以通过 IID 或完整 URN 进一步定位。
Spec 型号列表及单型号定义通过 `miot-spec.org/miot-spec-v2/` 在线获取，型号索引缓存到临时目录。

### 实时消息订阅

`MiotSubscriptionClient` 使用 MQTT 5 和 TLS 连接小米云端，接收属性变化和设备事件。
设备查询及控制使用 `.mi.token.json`，消息订阅使用 OAuth Access Token，授权结果保存到项目根目录的 `.mi.oauth.json`。
可通过 `MI_OAUTH_TOKEN_PATH` 指定 OAuth 文件位置。两个令牌文件及其临时文件均已加入 Git 忽略规则。

先使用已获得小米 Home 云接口权限的 OAuth 应用及其登记回调地址完成授权。
OAuth 应授权设备所属的小米账号。

```powershell
dotnet run --project .\MiServiceSharp.Demo -- oauth-login <应用ID> <登记回调地址> cn
```

Demo 会打开浏览器。授权跳转后，将包含 `code` 和 `state` 的完整回调地址粘贴回终端。
程序校验回调地址和 `state` 后交换并保存凭证，重连时会重新读取凭证，接近过期时自动刷新。
使用小米官方 Xiaomi Home（Home Assistant）应用授权时，页面会显示该应用身份，其公开参数见官方仓库的 `miot/const.py`。
为该官方应用传入 `http://homeassistant.local:8123` 基址时，程序会自动补齐 `/api/webhook/<随机ID>` 回调路径，并按官方方式生成设备绑定的 `state`。
授权请求保存到 `.mi.oauth.pending.json`。进程中断后可运行以下命令，再粘贴同一次授权得到的完整回调地址。

```powershell
dotnet run --project .\MiServiceSharp.Demo -- oauth-complete
```

`oauth-login` 支持 `--manual`，适用于手动选择浏览器的环境。成功交换并保存凭证后会清理待完成请求。

```powershell
# 指定设备的单个属性，支持名称、数字 IID 和完整 URN
dotnet run --project .\MiServiceSharp.Demo -- watch 132634187 2 1
dotnet run --project .\MiServiceSharp.Demo -- watch 132634187 light on

# 指定设备的全部属性或全部事件
dotnet run --project .\MiServiceSharp.Demo -- watch 132634187
dotnet run --project .\MiServiceSharp.Demo -- watch-events 132634187

# 单个设备事件
dotnet run --project .\MiServiceSharp.Demo -- watch-events <设备ID> <siid> <eiid>
```

按 Ctrl+C 取消订阅并关闭连接。事件使用小米协议中的 `event_occured` Topic。
库中可直接对绑定设备的属性对象订阅。

```csharp
using var stopping = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) => { args.Cancel = true; stopping.Cancel(); };
using var oauthHttp = new HttpClient();
var tokens = new MiOAuthTokenProvider(new MiOAuthClient(oauthHttp),
    new FileMiOAuthTokenStore(FileMiOAuthTokenStore.GetDefaultPath()));
await using var notifications = client.CreateSubscriptionClient(tokens);
notifications.Error += (_, error) => Console.Error.WriteLine(error.Exception.Message);

var light = (await client.MiioCloud.GetDevicesAsync()).Single(device => device.Did == "132634187");
var power = (await light.GetServiceAsync("light")).GetProperty("on");
await using var subscription = await power.SubscribeAsync(notifications, (change, callbackToken) =>
{
    Console.WriteLine($"{change.DeviceId} {change.Siid}/{change.Piid} = {change.Value}");
    return Task.CompletedTask;
});
try
{
    await Task.Delay(Timeout.InfiniteTimeSpan, stopping.Token);
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
```

设备对象还提供 `SubscribePropertiesAsync` 和 `SubscribeEventsAsync`，单个事件可调用订阅客户端的 `SubscribeEventAsync`。
每个订阅句柄均实现 `IAsyncDisposable`。同 Topic 的多个监听器共享一次 Broker 订阅，最后一个监听器释放时取消该 Topic。
断线后会重新连接并恢复全部有效订阅。回调在独立分发任务中按顺序执行，支持在回调内取消订阅。
消息按 MQTT QoS 1 接收，可能重复投递。默认缓冲容量为 1024，缓冲满或回调失败会触发 `Error`，回调应及时返回并响应取消。
所有连接参数可通过 `MiotSubscriptionOptions` 配置，TLS 默认启用并使用系统证书验证。

### 测试

```powershell
dotnet test MiServiceSharp.sln
```

真实账号集成测试默认跳过。设置 `RUN_MI_INTEGRATION=1` 后会测试登录与设备列表查询。
设备的读取、写入和动作调用均由 Demo 中对应的命令执行。
设置 `RUN_MI_OAUTH_INTEGRATION=1` 和 `MI_OAUTH_TEST_DEVICE=<已授权设备ID>` 后，可以运行 `OAuthCloudIntegrationTests`，验证真实 OAuth 刷新、MQTT 连接和设备订阅。

## WebAPI 用法

启动 API：

```powershell
cd E:\GIT\MiServiceSharp
dotnet run --project .\MiServiceSharp.Api
```

关键接口：

- Auth
  - `POST /api/v1/auth/login`：登录并落盘 token
  - `POST /api/v1/auth/login/challenge/start`：开启“二次验证会话式登录”
  - `POST /api/v1/auth/login/challenge/continue`：用户完成手机验证后继续
  - `GET /api/v1/auth/login/challenge/{sessionId}`：查询会话状态
  - `POST /api/v1/auth/jwt`：登录并签发 JWT
  - `GET /api/v1/auth/token`：读取当前 token
- MiNA
  - `GET /api/v1/mina/devices`：获取音箱设备
  - `POST /api/v1/mina/tts`：TTS 播报
  - `POST /api/v1/mina/play-url`：URL 播放
- MiIO Cloud
  - `GET /api/v1/miio/devices`：获取 MiIO 设备（含 token）
  - `POST /api/v1/miio/home-rpc`：通用 MiIO 云端 RPC
  - `POST /api/v1/miio/home/get-props`：对应 `home_get_props`
  - `POST /api/v1/miio/home/get-prop`：对应 `home_get_prop`
  - `POST /api/v1/miio/home/set-props`：对应 `home_set_props`
  - `POST /api/v1/miio/home/set-prop`：对应 `home_set_prop`
  - `POST /api/v1/miio/miot/get-props`：对应 `miot_get_props`
  - `POST /api/v1/miio/miot/get-prop`：对应 `miot_get_prop`
  - `POST /api/v1/miio/miot/set-props`：对应 `miot_set_props`
  - `POST /api/v1/miio/miot/set-prop`：对应 `miot_set_prop`
  - `POST /api/v1/miio/miot/action`：对应 `miot_action`
  - `POST /api/v1/miio/miot-spec`：对应 `miot_spec`
- MiIO Local
  - `POST /api/v1/miio/local/command`：本地 miIO UDP 命令

鉴权说明：

- ` /api/health`、`/api/auth/login`、`/api/auth/jwt` 允许匿名。
- 其余 `/api/**` 需要二选一：
  - 请求头 `X-API-Key: <appsettings中的Security.ApiKey>`
  - 或 `Authorization: Bearer <jwt>`
- 服务异常统一返回 `application/problem+json`（ProblemDetails）。
- 若小米登录返回 `notificationUrl`，建议走 challenge 会话流程：
  1) 调用 `/api/v1/auth/login/challenge/start` 获取 `202 + sessionId + verificationUrl`
  2) 用户在浏览器完成短信验证
  3) 调用 `/api/v1/auth/login/challenge/continue` 并轮询 `/api/v1/auth/login/challenge/{sessionId}`
  4) 状态变为 `succeeded` 后继续调用其他接口

Swagger UI：开发环境访问 `/swagger`，支持 Bearer 与 API Key 的 Authorize 按钮。

请求示例见 [MiServiceSharp.Api/MiServiceSharp.Api.http](MiServiceSharp.Api/MiServiceSharp.Api.http)。

## 许可证

本项目采用 [Selective Freedom License (SFL) v1.0](https://github.com/bighamx/MIT-NoHuawei) 授权，详见 [LICENSE](LICENSE)。
