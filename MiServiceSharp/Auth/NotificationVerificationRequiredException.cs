namespace MiServiceSharp.Auth;

public sealed class NotificationVerificationRequiredException : InvalidOperationException
{
    public NotificationVerificationRequiredException(string notificationUrl)
        : base("登录需要二次验证，请先完成 notificationUrl 验证。")
    {
        NotificationUrl = notificationUrl;
    }

    public string NotificationUrl { get; }
}
