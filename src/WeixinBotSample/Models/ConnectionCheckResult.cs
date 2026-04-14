namespace WeixinBotSample.Models;

public sealed class ConnectionCheckResult
{
    public bool Succeeded { get; set; }

    public bool SessionExpired { get; set; }

    public bool TypingCapabilityAvailable { get; set; } = true;

    public string Message { get; set; } = string.Empty;

    public string ResponseSummary { get; set; } = string.Empty;

    public string TypingTicket { get; set; } = string.Empty;

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    public ConnectionCheckResult Clone()
    {
        return new ConnectionCheckResult
        {
            Succeeded = Succeeded,
            SessionExpired = SessionExpired,
            TypingCapabilityAvailable = TypingCapabilityAvailable,
            Message = Message,
            ResponseSummary = ResponseSummary,
            TypingTicket = TypingTicket,
            CheckedAt = CheckedAt,
        };
    }
}
