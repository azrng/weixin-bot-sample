namespace WeixinBotSample.Models;

public sealed class DemoConfiguration
{
    public string BaseUrl { get; set; } = "https://ilinkai.weixin.qq.com";

    public string RouteTag { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string AccountId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public bool IsBound { get; set; }

    public string BoundAccountName { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChannelRuntimeStatus RuntimeStatus { get; set; } = ChannelRuntimeStatus.Stopped;

    public string RuntimeError { get; set; } = string.Empty;

    public DateTimeOffset? RuntimeStartedAt { get; set; }

    public DateTimeOffset? RuntimeStoppedAt { get; set; }

    public string LastExternalChatId { get; set; } = string.Empty;

    public string LastContextToken { get; set; } = string.Empty;

    public DemoConfiguration Clone()
    {
        return new DemoConfiguration
        {
            BaseUrl = BaseUrl,
            RouteTag = RouteTag,
            Token = Token,
            AccountId = AccountId,
            UserId = UserId,
            IsBound = IsBound,
            BoundAccountName = BoundAccountName,
            UpdatedAt = UpdatedAt,
            RuntimeStatus = RuntimeStatus,
            RuntimeError = RuntimeError,
            RuntimeStartedAt = RuntimeStartedAt,
            RuntimeStoppedAt = RuntimeStoppedAt,
            LastExternalChatId = LastExternalChatId,
            LastContextToken = LastContextToken,
        };
    }
}
