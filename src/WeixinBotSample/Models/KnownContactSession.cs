namespace WeixinBotSample.Models;

public sealed class KnownContactSession
{
    public string ExternalUserId { get; set; } = string.Empty;

    public string ExternalChatId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string ChatName { get; set; } = string.Empty;

    public string LatestContextToken { get; set; } = string.Empty;

    public string LastMessageText { get; set; } = string.Empty;

    public DateTimeOffset LastMessageAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public KnownContactSession Clone()
    {
        return new KnownContactSession
        {
            ExternalUserId = ExternalUserId,
            ExternalChatId = ExternalChatId,
            SenderName = SenderName,
            ChatName = ChatName,
            LatestContextToken = LatestContextToken,
            LastMessageText = LastMessageText,
            LastMessageAt = LastMessageAt,
            UpdatedAt = UpdatedAt,
        };
    }
}
