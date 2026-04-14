using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Components.Pages;

public abstract class DemoWorkspacePageBase : ComponentBase, IAsyncDisposable
{
    [Inject]
    protected WeixinBotDemoService DemoService { get; set; } = default!;

    protected readonly CancellationTokenSource RefreshCancellation = new();
    protected WeixinDemoState? State;
    protected DemoConfiguration ConfigurationModel = new();
    protected PushMessageRequest PushRequest = new();
    protected MediaUploadRequest MediaRequest = new();
    protected string LastSuggestedExternalChatId = string.Empty;
    protected string LastSuggestedContextToken = string.Empty;
    protected byte[]? SelectedMediaContent;
    protected string SelectedMediaName = string.Empty;
    protected long SelectedMediaSize;
    protected string SelectedMediaContentType = string.Empty;
    protected bool IsLoading = true;
    protected bool IsSaving;
    protected bool IsBinding;
    protected bool IsRuntimeWorking;
    protected bool IsPushing;
    protected bool IsCheckingConnection;
    protected bool IsSendingMedia;
    protected bool IsRunningChecklist;
    protected bool IsRunningAllChecklist;
    protected string ActiveChecklistCode = string.Empty;
    protected bool ConfigurationDirty;
    protected string PageError = string.Empty;
    protected string DismissedLoadError = string.Empty;
    protected string SaveButtonText => IsSaving ? "保存中..." : "保存配置";

    protected override async Task OnInitializedAsync()
    {
        await LoadStateAsync(true);
        _ = Task.Run(RefreshLoopAsync);
    }

    protected async Task RefreshLoopAsync()
    {
        while (!RefreshCancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), RefreshCancellation.Token);
                await InvokeAsync(() => LoadStateAsync(!ConfigurationDirty));
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    protected async Task LoadStateAsync(bool overwriteConfiguration)
    {
        try
        {
            var state = await DemoService.GetStateAsync(RefreshCancellation.Token);
            State = state;

            if (overwriteConfiguration)
            {
                ConfigurationModel = state.Configuration.Clone();
                ConfigurationDirty = false;
            }

            ApplyPushRequestDefaults(state);
            if (!string.Equals(DismissedLoadError, state.LoadError, StringComparison.Ordinal))
            {
                DismissedLoadError = string.Empty;
            }

            IsLoading = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PageError = exception.Message;
            IsLoading = false;
        }
    }

    protected void MarkConfigurationDirty()
    {
        ConfigurationDirty = true;
    }

    protected async Task SaveConfigurationAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SaveConfigurationAsync(ConfigurationModel, RefreshCancellation.Token),
            () => IsSaving = true,
            () => IsSaving = false,
            overwriteConfiguration: true);
    }

    protected async Task BindWeChatAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(false, RefreshCancellation.Token),
            () => IsBinding = true,
            () => IsBinding = false,
            overwriteConfiguration: false);
    }

    protected async Task RefreshQrCodeAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(true, RefreshCancellation.Token),
            () => IsBinding = true,
            () => IsBinding = false,
            overwriteConfiguration: false);
    }

    protected async Task StartListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartListeningAsync(RefreshCancellation.Token),
            () => IsRuntimeWorking = true,
            () => IsRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    protected async Task StopListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StopListeningAsync(RefreshCancellation.Token),
            () => IsRuntimeWorking = true,
            () => IsRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    protected async Task SendPushAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SendPushMessageAsync(PushRequest, RefreshCancellation.Token),
            () => IsPushing = true,
            () => IsPushing = false,
            overwriteConfiguration: true);
    }

    protected async Task ValidateConnectionAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.ValidateConnectionAsync(RefreshCancellation.Token),
            () => IsCheckingConnection = true,
            () => IsCheckingConnection = false,
            overwriteConfiguration: true);
    }

    protected async Task OnMediaFileChanged(InputFileChangeEventArgs args)
    {
        ClearFloatingError();
        var file = args.File;
        if (file is null)
        {
            SelectedMediaContent = null;
            SelectedMediaName = string.Empty;
            SelectedMediaSize = 0;
            SelectedMediaContentType = string.Empty;
            return;
        }

        const long maxAllowedSize = 20 * 1024 * 1024;
        await using var stream = file.OpenReadStream(maxAllowedSize, RefreshCancellation.Token);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, RefreshCancellation.Token);
        SelectedMediaContent = memory.ToArray();
        SelectedMediaName = file.Name;
        SelectedMediaSize = file.Size;
        SelectedMediaContentType = file.ContentType;
        MediaRequest.FileName = file.Name;
        MediaRequest.ContentType = file.ContentType;
    }

    protected async Task SendMediaAsync()
    {
        if (SelectedMediaContent is null || SelectedMediaContent.Length == 0)
        {
            PageError = "请先选择一个媒体文件。";
            return;
        }

        MediaRequest.FileName = string.IsNullOrWhiteSpace(MediaRequest.FileName) ? SelectedMediaName : MediaRequest.FileName;
        MediaRequest.ContentType = string.IsNullOrWhiteSpace(MediaRequest.ContentType) ? SelectedMediaContentType : MediaRequest.ContentType;

        await ExecuteBusyAsync(
            () => DemoService.SendMediaMessageAsync(MediaRequest.Clone(), SelectedMediaContent, RefreshCancellation.Token),
            () => IsSendingMedia = true,
            () => IsSendingMedia = false,
            overwriteConfiguration: true);
    }

    protected async Task DownloadMediaAsync(string recordId)
    {
        await ExecuteBusyAsync(
            () => DemoService.DownloadMediaAsync(recordId, RefreshCancellation.Token),
            () => IsSendingMedia = true,
            () => IsSendingMedia = false,
            overwriteConfiguration: true);
    }

    protected async Task RunChecklistAsync(string code)
    {
        await ExecuteBusyAsync(
            () => DemoService.RunChecklistAsync(code, RefreshCancellation.Token),
            () =>
            {
                IsRunningChecklist = true;
                ActiveChecklistCode = code;
            },
            () =>
            {
                IsRunningChecklist = false;
                ActiveChecklistCode = string.Empty;
            },
            overwriteConfiguration: true);
    }

    protected async Task RunAllChecklistAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.RunAllChecklistAsync(RefreshCancellation.Token),
            () => IsRunningAllChecklist = true,
            () => IsRunningAllChecklist = false,
            overwriteConfiguration: true);
    }

    protected async Task ExecuteBusyAsync(Func<Task> action, Action begin, Action end, bool overwriteConfiguration)
    {
        ClearFloatingError();
        begin();
        try
        {
            await action();
            await LoadStateAsync(overwriteConfiguration);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PageError = exception.Message;
            await LoadStateAsync(overwriteConfiguration);
        }
        finally
        {
            end();
        }
    }

    protected bool ShouldShowFloatingNotice()
    {
        return !string.IsNullOrWhiteSpace(PageError) ||
               (!string.IsNullOrWhiteSpace(State?.LoadError) && !string.Equals(DismissedLoadError, State.LoadError, StringComparison.Ordinal));
    }

    protected string GetFloatingNoticeTitle()
    {
        return string.IsNullOrWhiteSpace(PageError) ? "系统提示" : "操作失败";
    }

    protected string GetFloatingNoticeMessage()
    {
        if (!string.IsNullOrWhiteSpace(PageError))
        {
            return PageError;
        }

        return State?.LoadError ?? string.Empty;
    }

    protected void DismissFloatingNotice()
    {
        if (!string.IsNullOrWhiteSpace(PageError))
        {
            PageError = string.Empty;
            return;
        }

        DismissedLoadError = State?.LoadError ?? string.Empty;
    }

    protected void ClearFloatingError()
    {
        PageError = string.Empty;
    }

    protected string GetRuntimeStatusText()
    {
        return State?.Configuration.RuntimeStatus switch
        {
            ChannelRuntimeStatus.Running => "监听运行中",
            ChannelRuntimeStatus.Error => "监听异常",
            _ => "监听已停止",
        };
    }

    protected string GetRuntimeStatusClass()
    {
        return State?.Configuration.RuntimeStatus switch
        {
            ChannelRuntimeStatus.Running => "status-chip--running",
            ChannelRuntimeStatus.Error => "status-chip--error",
            _ => "status-chip--stopped",
        };
    }

    protected string GetBindingStatusText()
    {
        return State?.Configuration.IsBound == true ? "已绑定微信 Bot" : "尚未绑定";
    }

    protected string GetBindingStatusClass()
    {
        return State?.Configuration.IsBound == true
            ? "status-chip--bound"
            : "status-chip--pending";
    }

    protected static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    protected static string DisplayOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    protected void ApplyPushRequestDefaults(WeixinDemoState state)
    {
        var currentContact = state.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));
        var suggestedExternalChatId = currentContact?.ExternalChatId ?? state.Configuration.LastExternalChatId;
        var suggestedContextToken = currentContact?.LatestContextToken ?? state.Configuration.LastContextToken;

        if (ShouldApplySuggestedValue(PushRequest.ExternalChatId, LastSuggestedExternalChatId))
        {
            PushRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(PushRequest.ContextToken, LastSuggestedContextToken))
        {
            PushRequest.ContextToken = suggestedContextToken;
        }

        if (ShouldApplySuggestedValue(MediaRequest.ExternalChatId, LastSuggestedExternalChatId))
        {
            MediaRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(MediaRequest.ContextToken, LastSuggestedContextToken))
        {
            MediaRequest.ContextToken = suggestedContextToken;
        }

        LastSuggestedExternalChatId = suggestedExternalChatId;
        LastSuggestedContextToken = suggestedContextToken;
    }

    protected string GetPushTargetHint()
    {
        var currentContact = State?.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));

        if (currentContact is null)
        {
            return "默认带入最近一次可回复的联系人上下文。";
        }

        var senderName = DisplayOrFallback(currentContact.SenderName, currentContact.ExternalUserId);
        return $"默认带入当前微信用户：{senderName} / {currentContact.ExternalChatId}";
    }

    protected void UseKnownContact(KnownContactSession contact)
    {
        PushRequest.ExternalChatId = contact.ExternalChatId;
        PushRequest.ContextToken = contact.LatestContextToken;
        MediaRequest.ExternalChatId = contact.ExternalChatId;
        MediaRequest.ContextToken = contact.LatestContextToken;
    }

    protected string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "-";
        }

        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    protected static bool ShouldShowMediaTechnicalDetails(MediaTransferRecord item)
    {
        return !string.IsNullOrWhiteSpace(item.AsrText) ||
               !string.IsNullOrWhiteSpace(item.ContextToken) ||
               !string.IsNullOrWhiteSpace(item.FileKey) ||
               !string.IsNullOrWhiteSpace(item.Md5) ||
               !string.IsNullOrWhiteSpace(item.AesKey) ||
               !string.IsNullOrWhiteSpace(item.DownloadParam) ||
               !string.IsNullOrWhiteSpace(item.TraceFilePath) ||
               !string.IsNullOrWhiteSpace(item.LocalCachePath) ||
               item.FileSize > 0 ||
               item.EncryptedFileSize > 0;
    }

    protected static string GetMediaTypeText(MediaMessageType mediaType)
    {
        return mediaType switch
        {
            MediaMessageType.Image => "图片",
            MediaMessageType.Voice => "语音",
            MediaMessageType.Video => "视频",
            _ => "文件",
        };
    }

    protected static string GetMediaStatusText(MediaTransferStatus status)
    {
        return status switch
        {
            MediaTransferStatus.Preparing => "准备中",
            MediaTransferStatus.Encrypting => "加密中",
            MediaTransferStatus.Uploading => "上传中",
            MediaTransferStatus.Sending => "发送中",
            MediaTransferStatus.Sent => "已发送",
            MediaTransferStatus.Received => "已接收",
            MediaTransferStatus.Downloading => "下载中",
            MediaTransferStatus.Downloaded => "已下载",
            MediaTransferStatus.Failed => "失败",
            _ => "待处理",
        };
    }

    protected static string GetChecklistStatusText(ChecklistItemStatus status)
    {
        return status switch
        {
            ChecklistItemStatus.Passed => "通过",
            ChecklistItemStatus.Failed => "失败",
            ChecklistItemStatus.Blocked => "待条件满足",
            _ => "未执行",
        };
    }

    protected IEnumerable<ProtocolCapabilityView> GetProtocolCapabilities()
    {
        var mediaReady = State?.MediaRecords.Any(item => item.TransferStatus is MediaTransferStatus.Sent or MediaTransferStatus.Downloaded) == true;
        var voiceAsrReady = State?.MediaRecords.Any(item => item.MediaType == MediaMessageType.Voice && !string.IsNullOrWhiteSpace(item.AsrText)) == true;
        var cursorReady = !string.IsNullOrWhiteSpace(State?.Configuration.SyncCursor);
        var knownContactReady = State?.KnownContacts.Count > 0;

        return
        [
            new("get_bot_qrcode", "二维码登录", "已支持", "微信绑定区", "获取二维码并轮询扫码状态，演示 Bot 如何接入。"),
            new("getupdates", "长轮询收消息", State?.Configuration.RuntimeStatus == ChannelRuntimeStatus.Running ? "运行中" : "已支持", "消息接收区", "通过 getupdates 长轮询接收入站消息，并记录原始上下文。"),
            new("cursor-persistence", "游标持久化", cursorReady ? "已运行" : "待执行", "连接参数 / 消息接收区", "把 get_updates_buf 落盘保存，应用重启后仍可续接。"),
            new("getconfig", "连接自检", State?.LastConnectionCheck is not null ? "已支持" : "待执行", "协议自检区", "校验当前 token 是否可用，并获取 typing_ticket。"),
            new("sendtyping", "正在输入", !string.IsNullOrWhiteSpace(State?.Configuration.TypingTicket) ? "已支持" : "待执行", "协议自检/发送链路", "在真正回复前先展示“正在输入”，提升交互体验。"),
            new("sendmessage:text", "文本消息", State?.LastPushResult is not null || State?.Messages.Count > 0 ? "已支持" : "待执行", "主动推送区 / 最近入站消息", "支持主动文本发送和文本自动回复。"),
            new("context-cache", "续聊上下文缓存", knownContactReady ? "已积累" : "待消息触发", "已知联系人区", "缓存 ExternalChatId 和 context_token，方便主动消息续聊。"),
            new("getuploadurl+upload", "媒体上传", mediaReady ? "已验证" : "已支持", "媒体消息演示区", "先拿 upload_param，再加密上传到 CDN。"),
            new("c2c/download", "媒体下载回读", State?.MediaRecords.Any(item => item.TransferStatus == MediaTransferStatus.Downloaded) == true ? "已验证" : "已支持", "媒体记录区", "下载密文并使用 aes_key 解密到本地缓存。"),
            new("voice_asr", "语音 ASR", voiceAsrReady ? "已验证" : "待真实语音", "媒体记录区 / Checklist", "协议若返回 text 字段，就直接展示转写结果。"),
            new("session-expiry", "会话过期恢复", State?.LastConnectionCheck?.SessionExpired == true ? "已验证" : "待真实联调", "Checklist", "真实联调时触发 errcode=-14，验证重新绑定提示。"),
            new("group-boundary", "群聊边界", "说明中", "协议说明区", "协议中可能出现群聊字段，但当前 Demo 不承诺完整群聊支持。"),
        ];
    }

    protected sealed record ProtocolCapabilityView(string Code, string Scene, string Status, string Entry, string Summary);

    protected static bool ShouldApplySuggestedValue(string currentValue, string lastSuggestedValue)
    {
        return string.IsNullOrWhiteSpace(currentValue) ||
               string.Equals(currentValue.Trim(), lastSuggestedValue, StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        if (!RefreshCancellation.IsCancellationRequested)
        {
            await RefreshCancellation.CancelAsync();
        }

        RefreshCancellation.Dispose();
    }
}
