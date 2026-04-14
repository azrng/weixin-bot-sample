namespace WeixinBotSample.Models;

public sealed class ChecklistItemRecord
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ChecklistItemStatus Status { get; set; } = ChecklistItemStatus.NotRun;

    public string Message { get; set; } = string.Empty;

    public string Evidence { get; set; } = string.Empty;

    public DateTimeOffset? CheckedAt { get; set; }

    public ChecklistItemRecord Clone()
    {
        return new ChecklistItemRecord
        {
            Code = Code,
            Name = Name,
            Description = Description,
            Status = Status,
            Message = Message,
            Evidence = Evidence,
            CheckedAt = CheckedAt,
        };
    }
}
