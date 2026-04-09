namespace WeixinBotSample.Models;

public sealed class OperationLogEntry
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Level { get; set; } = "Info";

    public string Message { get; set; } = string.Empty;

    public OperationLogEntry Clone()
    {
        return new OperationLogEntry
        {
            CreatedAt = CreatedAt,
            Level = Level,
            Message = Message,
        };
    }
}
