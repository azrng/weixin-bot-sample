namespace WeixinBotSample.Models;

public sealed class WeixinDemoState
{
    public DemoConfiguration Configuration { get; set; } = new();

    public BindingSessionState? ActiveBindingSession { get; set; }

    public List<WeixinMessageRecord> Messages { get; set; } = [];

    public List<KnownContactSession> KnownContacts { get; set; } = [];

    public List<MediaTransferRecord> MediaRecords { get; set; } = [];

    public List<ChecklistItemRecord> ChecklistItems { get; set; } = [];

    public List<OperationLogEntry> Logs { get; set; } = [];

    public PushMessageResult? LastPushResult { get; set; }

    public ConnectionCheckResult? LastConnectionCheck { get; set; }

    public AutoFillPromptState? PendingAutoFill { get; set; }

    public string PrimaryGreeting { get; set; } = "祝您今天顺顺利利，万事如意。";

    public string LatestReplyText { get; set; } = string.Empty;

    public string LoadError { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public WeixinDemoState Clone()
    {
        return new WeixinDemoState
        {
            Configuration = Configuration.Clone(),
            ActiveBindingSession = ActiveBindingSession?.Clone(),
            Messages = Messages.Select(item => item.Clone()).ToList(),
            KnownContacts = KnownContacts.Select(item => item.Clone()).ToList(),
            MediaRecords = MediaRecords.Select(item => item.Clone()).ToList(),
            ChecklistItems = ChecklistItems.Select(item => item.Clone()).ToList(),
            Logs = Logs.Select(item => item.Clone()).ToList(),
            LastPushResult = LastPushResult?.Clone(),
            LastConnectionCheck = LastConnectionCheck?.Clone(),
            PendingAutoFill = PendingAutoFill?.Clone(),
            PrimaryGreeting = PrimaryGreeting,
            LatestReplyText = LatestReplyText,
            LoadError = LoadError,
            UpdatedAt = UpdatedAt,
        };
    }
}
