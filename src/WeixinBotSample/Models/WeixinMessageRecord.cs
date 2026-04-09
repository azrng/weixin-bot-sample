namespace WeixinBotSample.Models;

public sealed class WeixinMessageRecord
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    public string ExternalChatId { get; set; } = string.Empty;

    public string ExternalUserId { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string ChatName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string ContextToken { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string ReplyText { get; set; } = string.Empty;

    public string ReplyStatus { get; set; } = string.Empty;

    public bool ReplySucceeded { get; set; }

    public WeixinMessageRecord Clone()
    {
        return new WeixinMessageRecord
        {
            MessageId = MessageId,
            ExternalChatId = ExternalChatId,
            ExternalUserId = ExternalUserId,
            SenderName = SenderName,
            ChatName = ChatName,
            Text = Text,
            ContextToken = ContextToken,
            CreatedAt = CreatedAt,
            ReplyText = ReplyText,
            ReplyStatus = ReplyStatus,
            ReplySucceeded = ReplySucceeded,
        };
    }
}
