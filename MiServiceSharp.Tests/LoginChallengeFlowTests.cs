using System.Reflection;
using System.Text.Json.Nodes;
using MiServiceSharp.Auth;
using MiServiceSharp.Storage;

namespace MiServiceSharp.Tests;

public sealed class LoginChallengeFlowTests
{
    [Fact]
    public void NeedNotificationVerification_WithNotificationAndMissingTokens_ShouldBeTrue()
    {
        var node = JsonNode.Parse(
            """
            {
              "notificationUrl": "https://account.xiaomi.com/fe/service/identity/authStart?sid=micoapi&context=abc",
              "code": 0,
              "location": "",
              "description": "成功"
            }
            """)!;

        var method = typeof(MiAccountClient).GetMethod(
            "NeedNotificationVerification",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var ret = method!.Invoke(null, [node]);
        Assert.True(ret is true);
    }

    [Fact]
    public void NeedNotificationVerification_WithoutNotification_ShouldBeFalse()
    {
        var node = JsonNode.Parse(
            """
            {
              "code": 0,
              "location": "https://api2.mina.mi.com/sts?...",
              "ssecurity": "abc",
              "nonce": "xyz",
              "userId": "1",
              "passToken": "2"
            }
            """)!;

        var method = typeof(MiAccountClient).GetMethod(
            "NeedNotificationVerification",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var ret = method!.Invoke(null, [node]);
        Assert.False(ret is true);
    }
}
