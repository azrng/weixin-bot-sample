namespace WeixinBotSample.Models;

public sealed class PushMessageResult
{
    public bool Succeeded { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ResponseSummary { get; set; } = string.Empty;

    public string ExternalChatId { get; set; } = string.Empty;

    public string ContextToken { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    public PushMessageResult Clone()
    {
        return new PushMessageResult
        {
            Succeeded = Succeeded,
            Message = Message,
            ResponseSummary = ResponseSummary,
            ExternalChatId = ExternalChatId,
            ContextToken = ContextToken,
            Content = Content,
            SentAt = SentAt,
        };
    }
}
