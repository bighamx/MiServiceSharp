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
  - `MiServiceFacade.cs` 聚合入口
- `MiServiceSharp.Demo/` 命令行演示
- `MiServiceSharp.Api/` WebAPI 封装层（OpenAPI）
- `MiServiceSharp.Tests/` 单元测试与集成测试模板

## 快速开始

1. 设置环境变量

```powershell
$env:MI_USER="你的小米账号"
$env:MI_PASS="你的小米密码"
$env:MI_DID="你的设备DID或设备名"
```

2. 运行 Demo

```powershell
cd E:\GIT\MiServiceSharp

dotnet run --project .\MiServiceSharp.Demo -- login micoapi
dotnet run --project .\MiServiceSharp.Demo -- login xiaomiio
dotnet run --project .\MiServiceSharp.Demo -- mina-devices
dotnet run --project .\MiServiceSharp.Demo -- miio-devices
```

3. 本地 miIO（需要局域网设备 IP + token）

```powershell
$env:MI_LOCAL_HOST="192.168.1.120"
$env:MI_LOCAL_TOKEN="00112233445566778899AABBCCDDEEFF"

dotnet run --project .\MiServiceSharp.Demo -- local-miio "miIO.info" "[]"
```

## 说明

- 集成测试默认 `Skip`，避免在 CI 或无账号环境下失败。
- token 默认保存在用户目录下：`.mi.token.json`。
- 本地 miIO 依赖设备可达、token 正确、以及设备支持局域网协议。

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