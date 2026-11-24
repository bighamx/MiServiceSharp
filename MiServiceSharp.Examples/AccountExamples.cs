using MiServiceSharp.Auth;

namespace MiServiceSharp.Examples;

internal static class AccountExamples
{
    // 账号和凭证存储在 Program.cs 中初始化，登录成功后由 Store 保存。
    public static async Task LoginAsync(MiAccountClient account, CancellationToken cancellationToken = default)
    {
        foreach (var serviceId in new[] { "xiaomiio", "micoapi" })
        {
            var success = await account.LoginAsync(serviceId, cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException(account.LastLoginError ?? $"Login for {serviceId} failed.");
            }

            Console.WriteLine($"{serviceId} login successful");
        }
    }
}
