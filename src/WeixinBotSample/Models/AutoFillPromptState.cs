namespace WeixinBotSample.Models;

public sealed class AutoFillPromptState
{
    public string ExternalChatId { get; set; } = string.Empty;

    public string ContextToken { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTimeOffset TriggeredAt { get; set; } = DateTimeOffset.UtcNow;

    public bool HasTarget =>
        !string.IsNullOrWhiteSpace(ExternalChatId) &&
        !string.IsNullOrWhiteSpace(ContextToken);

    public AutoFillPromptState Clone()
    {
        return new AutoFillPromptState
        {
            ExternalChatId = ExternalChatId,
            ContextToken = ContextToken,
            SenderName = SenderName,
            Summary = Summary,
            TriggeredAt = TriggeredAt,
        };
    }
}
