namespace WeixinBotSample.Models;

public sealed class BindingSessionState
{
    public string SessionKey { get; set; } = string.Empty;

    public string QrCode { get; set; } = string.Empty;

    public string QrCodeUrl { get; set; } = string.Empty;

    public string QrCodeDataUrl { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPolledAt { get; set; }

    public bool IsExpired { get; set; }

    public BindingSessionState Clone()
    {
        return new BindingSessionState
        {
            SessionKey = SessionKey,
            QrCode = QrCode,
            QrCodeUrl = QrCodeUrl,
            QrCodeDataUrl = QrCodeDataUrl,
            Message = Message,
            StartedAt = StartedAt,
            LastPolledAt = LastPolledAt,
            IsExpired = IsExpired,
        };
    }
}
