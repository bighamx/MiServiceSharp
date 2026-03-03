using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MiServiceSharp.Auth;
using MiServiceSharp.Models;
using MiServiceSharp.Protocol.LocalMiio;
using MiServiceSharp.Services;
using MiServiceSharp.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MiServiceSharp.Api",
        Version = "v1",
        Description = "MiServiceSharp HTTP API (MiNA/MiIO/LocalMiIO)"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "输入 JWT，例如: Bearer eyJ..."
    });

    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-API-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "输入 API Key"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddProblemDetails();

var security = builder.Configuration.GetSection("Security").Get<SecurityOptions>() ?? new SecurityOptions();

var tokenFile = builder.Configuration["MiService:TokenFile"]
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mi.token.json");

builder.Services.AddSingleton<IMiTokenStore>(_ => new FileMiTokenStore(tokenFile));
builder.Services.AddHttpClient();
builder.Services.AddSingleton(security);
builder.Services.AddSingleton<LoginChallengeManager>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = security.JwtIssuer,
            ValidAudience = security.JwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(security.JwtSigningKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = feature?.Error;

        var status = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        var detail = app.Environment.IsDevelopment()
            ? exception?.ToString()
            : exception?.Message;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = "请求处理失败",
            Detail = detail,
            Instance = context.Request.Path
        };

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MiServiceSharp.Api v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();

var anonymousApiPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "/api/v1/health",
    "/api/v1/auth/login",
    "/api/v1/auth/jwt",
    "/api/v1/auth/login/challenge/start",
    "/api/v1/auth/login/challenge/continue"
};

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var path = context.Request.Path.Value ?? string.Empty;
    if (anonymousApiPaths.Contains(path))
    {
        await next();
        return;
    }

    var jwtOk = context.User?.Identity?.IsAuthenticated == true;
    if (jwtOk)
    {
        await next();
        return;
    }

    var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(security.ApiKey) && string.Equals(apiKey, security.ApiKey, StringComparison.Ordinal))
    {
        await next();
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "未授权",
        Detail = "请提供有效的 Bearer Token 或 X-API-Key",
        Instance = context.Request.Path
    });
});

app.UseAuthorization();

app.MapGet("/api/health", () => Results.Redirect("/api/v1/health", permanent: false));

var v1 = app.MapGroup("/api/v1").WithTags("MiService v1");

v1.MapGet("/health", () => Results.Ok(new { ok = true, ts = DateTimeOffset.UtcNow }))
    .WithTags("System");

v1.MapPost("/auth/login", async (LoginRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "username/password 不能为空" });
    }

    var sid = string.IsNullOrWhiteSpace(req.Sid) ? "micoapi" : req.Sid;
    using var httpClient = httpClientFactory.CreateClient();
    var account = new MiAccountClient(
        httpClient,
        new MiAccountOptions { Username = req.Username, Password = req.Password },
        tokenStore);

    await account.InitializeAsync(ct);
    var ok = await account.LoginAsync(sid, ct);
    if (!ok || account.TokenBundle is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        ok,
        sid,
        account.TokenBundle.UserId,
        account.TokenBundle.DeviceId,
        services = account.TokenBundle.Services.Keys
    });
})
    .WithTags("Auth");

v1.MapPost("/auth/login/challenge/start", async (
    LoginRequest req,
    IHttpClientFactory httpClientFactory,
    IMiTokenStore tokenStore,
    LoginChallengeManager challengeManager,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "username/password 不能为空" });
    }

    var sid = string.IsNullOrWhiteSpace(req.Sid) ? "micoapi" : req.Sid;
    var session = challengeManager.Create(req.Username, sid);

    _ = Task.Run(async () =>
    {
        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var account = new MiAccountClient(
                httpClient,
                new MiAccountOptions
                {
                    Username = req.Username,
                    Password = req.Password,
                    EnableInteractiveVerification = false,
                    NotificationUrlHandler = async (url, token) =>
                    {
                        session.SetWaiting(url);
                        await session.WaitForContinueAsync(token);
                        session.SetProcessing();
                    }
                },
                tokenStore);

            await account.InitializeAsync(ct);
            var ok = await account.LoginAsync(sid, ct);
            if (ok && account.TokenBundle is not null)
            {
                session.SetSucceeded(account.TokenBundle.UserId, account.TokenBundle.DeviceId, account.TokenBundle.Services.Keys);
            }
            else
            {
                session.SetFailed("登录失败");
            }
        }
        catch (NotificationVerificationRequiredException ex)
        {
            session.SetWaiting(ex.NotificationUrl);
        }
        catch (Exception ex)
        {
            session.SetFailed(ex.Message);
        }
    }, ct);

    await Task.Delay(300, ct);
    return Results.Json(session.ToResponse(), statusCode: session.ToHttpStatusCode());
})
    .WithTags("Auth");

v1.MapPost("/auth/login/challenge/continue", async (
    ChallengeContinueRequest req,
    LoginChallengeManager challengeManager,
    CancellationToken ct) =>
{
    var session = challengeManager.Get(req.SessionId);
    if (session is null)
    {
        return Results.NotFound(new { error = "session 不存在" });
    }

    session.SignalContinue();
    await Task.Delay(300, ct);
    return Results.Json(session.ToResponse(), statusCode: session.ToHttpStatusCode());
})
    .WithTags("Auth");

v1.MapGet("/auth/login/challenge/{sessionId}", (string sessionId, LoginChallengeManager challengeManager) =>
{
    var session = challengeManager.Get(sessionId);
    if (session is null)
    {
        return Results.NotFound(new { error = "session 不存在" });
    }

    return Results.Json(session.ToResponse(), statusCode: session.ToHttpStatusCode());
})
    .WithTags("Auth");

v1.MapPost("/auth/jwt", async (LoginRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, SecurityOptions options, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "username/password 不能为空" });
    }

    using var httpClient = httpClientFactory.CreateClient();
    var account = new MiAccountClient(
        httpClient,
        new MiAccountOptions { Username = req.Username, Password = req.Password },
        tokenStore);

    await account.InitializeAsync(ct);
    var loginMina = await account.LoginAsync("micoapi", ct);
    var loginMiio = await account.LoginAsync("xiaomiio", ct);

    if (!loginMina || !loginMiio || account.TokenBundle is null)
    {
        return Results.Unauthorized();
    }

    var token = CreateJwt(req.Username, options);
    return Results.Ok(new
    {
        access_token = token,
        token_type = "Bearer",
        expires_in = options.JwtExpireMinutes * 60
    });
})
    .WithTags("Auth");

v1.MapGet("/auth/token", async (IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var token = await tokenStore.LoadAsync(ct);
    if (token is null)
    {
        return Results.NotFound(new { error = "token 文件不存在" });
    }

    return Results.Ok(token);
})
    .WithTags("Auth");

v1.MapPost("/auth/token/clear", async (IMiTokenStore tokenStore, CancellationToken ct) =>
{
    await tokenStore.ClearAsync(ct);
    return Results.Ok(new { ok = true });
})
    .WithTags("Auth");

v1.MapGet("/mina/devices", async (string? username, string? password, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(new AccountContext { Username = username, Password = password }, httpClientFactory, tokenStore, ct);
    var mina = new MinaService(account);
    var devices = await mina.DeviceListAsync(cancellationToken: ct);
    return Results.Ok(devices);
})
    .WithTags("MiNA");

v1.MapPost("/mina/tts", async (MinaTtsRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var mina = new MinaService(account);
    var result = await mina.TextToSpeechAsync(req.DeviceId, req.Text, ct);
    return Results.Ok(result);
})
    .WithTags("MiNA");

v1.MapPost("/mina/play-url", async (MinaPlayUrlRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var mina = new MinaService(account);
    var result = await mina.PlayByUrlAsync(req.DeviceId, req.Url, req.Type, ct);
    return Results.Ok(result);
})
    .WithTags("MiNA");

v1.MapPost("/mina/player-op", async (MinaPlayerOpRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var mina = new MinaService(account);
    JsonNode result = req.Action.ToLowerInvariant() switch
    {
        "pause" => await mina.PlayerPauseAsync(req.DeviceId, ct),
        "stop" => await mina.PlayerStopAsync(req.DeviceId, ct),
        "play" => await mina.PlayerPlayAsync(req.DeviceId, ct),
        "status" => await mina.PlayerGetStatusAsync(req.DeviceId, ct),
        _ => throw new InvalidOperationException("action 仅支持 pause/stop/play/status")
    };

    return Results.Ok(result);
})
    .WithTags("MiNA");

v1.MapGet("/miio/devices", async (string? keyword, string? username, string? password, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(new AccountContext { Username = username, Password = password }, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var devices = await miio.DeviceListAsync(name: keyword, cancellationToken: ct);
    return Results.Ok(devices);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/home-rpc", async (MiioHomeRpcRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.HomeRequestAsync(req.Did, req.Method, req.Params ?? new JsonArray(), ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/home/get-props", async (MiioHomeGetPropsRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.HomeGetPropsAsync(req.Did, req.Props ?? [], ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/home/get-prop", async (MiioHomeGetPropRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.HomeGetPropAsync(req.Did, req.Prop, ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/home/set-props", async (MiioHomeSetPropsRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.HomeSetPropsAsync(
        req.Did,
        (req.Props ?? []).Select(static p => (p.Prop, p.Value)),
        ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/home/set-prop", async (MiioHomeSetPropRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.HomeSetPropAsync(req.Did, req.Prop, req.Value, ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/miot/get-props", async (MiioMiotGetPropsRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.MiotGetPropsAsync(
        req.Did,
        (req.Iids ?? []).Select(static item => (item.Siid, item.Piid)),
        ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/miot/get-prop", async (MiioMiotGetPropRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.MiotGetPropAsync(req.Did, (req.Siid, req.Piid), ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/miot/set-props", async (MiioMiotSetPropsRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.MiotSetPropsAsync(
        req.Did,
        (req.Props ?? []).Select(static item => (item.Siid, item.Piid, item.Value)),
        ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/miot/set-prop", async (MiioMiotSetPropRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.MiotSetPropAsync(req.Did, (req.Siid, req.Piid), req.Value, ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/miot/action", async (MiioMiotActionRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    var result = await miio.MiotActionAsync(req.Did, (req.Siid, req.Aiid), req.Args, ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/miot-spec", async (MiioMiotSpecRequest req, IHttpClientFactory httpClientFactory, IMiTokenStore tokenStore, CancellationToken ct) =>
{
    var account = await BuildAccountAsync(req.Account, httpClientFactory, tokenStore, ct);
    var miio = new MiioCloudService(account);
    object result = string.Equals(req.Format, "json", StringComparison.OrdinalIgnoreCase)
        ? await miio.MiotSpecDataAsync(req.Type, ct)
        : await miio.MiotSpecTextAsync(req.Type, req.Format, ct);
    return Results.Ok(result);
})
    .WithTags("MiIO Cloud");

v1.MapPost("/miio/local/command", async (LocalMiioRequest req, CancellationToken ct) =>
{
    await using var local = new LocalMiioClient(req.Host, req.TokenHex, req.Port, req.TimeoutMs);
    var hello = await local.HandshakeAsync(ct);
    var result = await local.SendCommandAsync(req.Method, req.Params ?? new JsonArray(), ct);
    return Results.Ok(new { hello.DeviceId, hello.Stamp, result });
})
    .WithTags("MiIO Local");

app.Run();

static string CreateJwt(string username, SecurityOptions options)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSigningKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var now = DateTime.UtcNow;
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, username),
        new(JwtRegisteredClaimNames.UniqueName, username),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
    };

    var jwt = new JwtSecurityToken(
        issuer: options.JwtIssuer,
        audience: options.JwtAudience,
        claims: claims,
        notBefore: now,
        expires: now.AddMinutes(options.JwtExpireMinutes),
        signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(jwt);
}

static async Task<MiAccountClient> BuildAccountAsync(
    AccountContext context,
    IHttpClientFactory httpClientFactory,
    IMiTokenStore tokenStore,
    CancellationToken ct)
{
    var httpClient = httpClientFactory.CreateClient();

    var account = new MiAccountClient(
        httpClient,
        new MiAccountOptions
        {
            Username = context.Username ?? string.Empty,
            Password = context.Password ?? string.Empty
        },
        tokenStore);

    await account.InitializeAsync(ct);

    if (!string.IsNullOrWhiteSpace(context.Username) && !string.IsNullOrWhiteSpace(context.Password))
    {
        await account.LoginAsync("micoapi", ct);
        await account.LoginAsync("xiaomiio", ct);
        return account;
    }

    if (account.TokenBundle is null || !account.TokenBundle.IsLoggedIn)
    {
        throw new InvalidOperationException("未登录：请先调用 /api/auth/login 或在请求中提供账号密码");
    }

    return account;
}

public sealed class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Sid { get; set; }
}

public sealed class AccountContext
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public sealed class MinaTtsRequest
{
    public AccountContext Account { get; set; } = new();
    public string DeviceId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public sealed class MinaPlayUrlRequest
{
    public AccountContext Account { get; set; } = new();
    public string DeviceId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int Type { get; set; } = 2;
}

public sealed class MinaPlayerOpRequest
{
    public AccountContext Account { get; set; } = new();
    public string DeviceId { get; set; } = string.Empty;
    public string Action { get; set; } = "status";
}

public sealed class MiioHomeRpcRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public JsonArray? Params { get; set; }
}

public sealed class MiioHomeGetPropsRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public List<string>? Props { get; set; }
}

public sealed class MiioHomeGetPropRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public string Prop { get; set; } = string.Empty;
}

public sealed class MiioHomeSetPropItem
{
    public string Prop { get; set; } = string.Empty;
    public JsonNode? Value { get; set; }
}

public sealed class MiioHomeSetPropsRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public List<MiioHomeSetPropItem>? Props { get; set; }
}

public sealed class MiioHomeSetPropRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public string Prop { get; set; } = string.Empty;
    public JsonNode? Value { get; set; }
}

public sealed class MiioIidItem
{
    public int Siid { get; set; }
    public int Piid { get; set; }
}

public sealed class MiioMiotGetPropsRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public List<MiioIidItem>? Iids { get; set; }
}

public sealed class MiioMiotGetPropRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public int Siid { get; set; }
    public int Piid { get; set; }
}

public sealed class MiioMiotSetPropItem
{
    public int Siid { get; set; }
    public int Piid { get; set; }
    public JsonNode? Value { get; set; }
}

public sealed class MiioMiotSetPropsRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public List<MiioMiotSetPropItem>? Props { get; set; }
}

public sealed class MiioMiotSetPropRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public int Siid { get; set; }
    public int Piid { get; set; }
    public string? Value { get; set; }
}

public sealed class MiioMiotActionRequest
{
    public AccountContext Account { get; set; } = new();
    public string Did { get; set; } = string.Empty;
    public int Siid { get; set; }
    public int Aiid { get; set; }
    public JsonArray? Args { get; set; }
}

public sealed class MiioMiotSpecRequest
{
    public AccountContext Account { get; set; } = new();
    public string? Type { get; set; }
    public string? Format { get; set; }
}

public sealed class LocalMiioRequest
{
    public string Host { get; set; } = string.Empty;
    public string TokenHex { get; set; } = string.Empty;
    public int Port { get; set; } = 54321;
    public int TimeoutMs { get; set; } = 3000;
    public string Method { get; set; } = "miIO.info";
    public JsonArray? Params { get; set; }
}

public sealed class SecurityOptions
{
    public string ApiKey { get; set; } = "change-this-api-key";
    public string JwtIssuer { get; set; } = "MiServiceSharp.Api";
    public string JwtAudience { get; set; } = "MiServiceSharp.Client";
    public string JwtSigningKey { get; set; } = "change-this-super-long-jwt-signing-key-1234567890";
    public int JwtExpireMinutes { get; set; } = 120;
}

public sealed class ChallengeContinueRequest
{
    public string SessionId { get; set; } = string.Empty;
}

public sealed class LoginChallengeManager
{
    private readonly ConcurrentDictionary<string, LoginChallengeSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public LoginChallengeSession Create(string username, string sid)
    {
        Cleanup();
        var session = new LoginChallengeSession(username, sid);
        _sessions[session.SessionId] = session;
        return session;
    }

    public LoginChallengeSession? Get(string sessionId)
    {
        Cleanup();
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    private void Cleanup()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _sessions)
        {
            if (now - item.Value.UpdatedAt > TimeSpan.FromMinutes(20))
            {
                _sessions.TryRemove(item.Key, out _);
            }
        }
    }
}

public sealed class LoginChallengeSession
{
    private TaskCompletionSource<bool> _continueSignal = NewSignal();
    private readonly object _lock = new();

    public LoginChallengeSession(string username, string sid)
    {
        SessionId = Guid.NewGuid().ToString("N");
        Username = username;
        Sid = sid;
        Status = "processing";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string SessionId { get; }
    public string Username { get; }
    public string Sid { get; }
    public string Status { get; private set; }
    public string? VerificationUrl { get; private set; }
    public string? Error { get; private set; }
    public long? UserId { get; private set; }
    public string? DeviceId { get; private set; }
    public IReadOnlyCollection<string>? Services { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetWaiting(string verificationUrl)
    {
        lock (_lock)
        {
            VerificationUrl = verificationUrl;
            Status = "waiting_verification";
            Error = null;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void SetProcessing()
    {
        lock (_lock)
        {
            Status = "processing";
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void SetSucceeded(long userId, string deviceId, IEnumerable<string> services)
    {
        lock (_lock)
        {
            UserId = userId;
            DeviceId = deviceId;
            Services = services.ToArray();
            Status = "succeeded";
            Error = null;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void SetFailed(string error)
    {
        lock (_lock)
        {
            Status = "failed";
            Error = error;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public Task WaitForContinueAsync(CancellationToken cancellationToken)
    {
        return _continueSignal.Task.WaitAsync(cancellationToken);
    }

    public void SignalContinue()
    {
        lock (_lock)
        {
            _continueSignal.TrySetResult(true);
            _continueSignal = NewSignal();
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    public int ToHttpStatusCode()
    {
        return Status switch
        {
            "succeeded" => StatusCodes.Status200OK,
            "failed" => StatusCodes.Status400BadRequest,
            "waiting_verification" => StatusCodes.Status202Accepted,
            _ => StatusCodes.Status202Accepted
        };
    }

    public object ToResponse()
    {
        return new
        {
            sessionId = SessionId,
            status = Status,
            verificationUrl = VerificationUrl,
            error = Error,
            userId = UserId,
            deviceId = DeviceId,
            services = Services,
            updatedAt = UpdatedAt
        };
    }

    private static TaskCompletionSource<bool> NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
