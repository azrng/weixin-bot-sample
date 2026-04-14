using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using WeixinBotSample.Models;
using WeixinBotSample.Services;

namespace WeixinBotSample.Components.Pages;

public partial class Home : ComponentBase, IAsyncDisposable
{
    [Inject]
    private WeixinBotDemoService DemoService { get; set; } = default!;

    private readonly CancellationTokenSource _refreshCancellation = new();
    private WeixinDemoState? _state;
    private DemoConfiguration _configurationModel = new();
    private PushMessageRequest _pushRequest = new();
    private MediaUploadRequest _mediaRequest = new();
    private string _lastSuggestedExternalChatId = string.Empty;
    private string _lastSuggestedContextToken = string.Empty;
    private byte[]? _selectedMediaContent;
    private string _selectedMediaName = string.Empty;
    private long _selectedMediaSize;
    private string _selectedMediaContentType = string.Empty;
    private bool _isLoading = true;
    private bool _isSaving;
    private bool _isBinding;
    private bool _isRuntimeWorking;
    private bool _isPushing;
    private bool _isCheckingConnection;
    private bool _isSendingMedia;
    private bool _isRunningChecklist;
    private bool _isRunningAllChecklist;
    private string _activeChecklistCode = string.Empty;
    private bool _configurationDirty;
    private string _pageError = string.Empty;
    private string _saveButtonText => _isSaving ? "保存中..." : "保存配置";

    protected override async Task OnInitializedAsync()
    {
        await LoadStateAsync(true);
        _ = Task.Run(RefreshLoopAsync);
    }

    private async Task RefreshLoopAsync()
    {
        while (!_refreshCancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), _refreshCancellation.Token);
                await InvokeAsync(() => LoadStateAsync(!_configurationDirty));
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task LoadStateAsync(bool overwriteConfiguration)
    {
        try
        {
            var state = await DemoService.GetStateAsync(_refreshCancellation.Token);
            _state = state;

            if (overwriteConfiguration)
            {
                _configurationModel = state.Configuration.Clone();
                _configurationDirty = false;
            }

            ApplyPushRequestDefaults(state);

            _isLoading = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _pageError = exception.Message;
            _isLoading = false;
        }
    }

    private void MarkConfigurationDirty()
    {
        _configurationDirty = true;
    }

    private async Task SaveConfigurationAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SaveConfigurationAsync(_configurationModel, _refreshCancellation.Token),
            () => _isSaving = true,
            () => _isSaving = false,
            overwriteConfiguration: true);
    }

    private async Task BindWeChatAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(false, _refreshCancellation.Token),
            () => _isBinding = true,
            () => _isBinding = false,
            overwriteConfiguration: false);
    }

    private async Task RefreshQrCodeAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartBindingAsync(true, _refreshCancellation.Token),
            () => _isBinding = true,
            () => _isBinding = false,
            overwriteConfiguration: false);
    }

    private async Task StartListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StartListeningAsync(_refreshCancellation.Token),
            () => _isRuntimeWorking = true,
            () => _isRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    private async Task StopListeningAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.StopListeningAsync(_refreshCancellation.Token),
            () => _isRuntimeWorking = true,
            () => _isRuntimeWorking = false,
            overwriteConfiguration: true);
    }

    private async Task SendPushAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.SendPushMessageAsync(_pushRequest, _refreshCancellation.Token),
            () => _isPushing = true,
            () => _isPushing = false,
            overwriteConfiguration: true);
    }

    private async Task ValidateConnectionAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.ValidateConnectionAsync(_refreshCancellation.Token),
            () => _isCheckingConnection = true,
            () => _isCheckingConnection = false,
            overwriteConfiguration: true);
    }

    private async Task OnMediaFileChanged(InputFileChangeEventArgs args)
    {
        _pageError = string.Empty;
        var file = args.File;
        if (file is null)
        {
            _selectedMediaContent = null;
            _selectedMediaName = string.Empty;
            _selectedMediaSize = 0;
            _selectedMediaContentType = string.Empty;
            return;
        }

        const long maxAllowedSize = 20 * 1024 * 1024;
        await using var stream = file.OpenReadStream(maxAllowedSize, _refreshCancellation.Token);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, _refreshCancellation.Token);
        _selectedMediaContent = memory.ToArray();
        _selectedMediaName = file.Name;
        _selectedMediaSize = file.Size;
        _selectedMediaContentType = file.ContentType;
        _mediaRequest.FileName = file.Name;
        _mediaRequest.ContentType = file.ContentType;
    }

    private async Task SendMediaAsync()
    {
        if (_selectedMediaContent is null || _selectedMediaContent.Length == 0)
        {
            _pageError = "请先选择一个媒体文件。";
            return;
        }

        _mediaRequest.FileName = string.IsNullOrWhiteSpace(_mediaRequest.FileName) ? _selectedMediaName : _mediaRequest.FileName;
        _mediaRequest.ContentType = string.IsNullOrWhiteSpace(_mediaRequest.ContentType) ? _selectedMediaContentType : _mediaRequest.ContentType;

        await ExecuteBusyAsync(
            () => DemoService.SendMediaMessageAsync(_mediaRequest.Clone(), _selectedMediaContent, _refreshCancellation.Token),
            () => _isSendingMedia = true,
            () => _isSendingMedia = false,
            overwriteConfiguration: true);
    }

    private async Task DownloadMediaAsync(string recordId)
    {
        await ExecuteBusyAsync(
            () => DemoService.DownloadMediaAsync(recordId, _refreshCancellation.Token),
            () => _isSendingMedia = true,
            () => _isSendingMedia = false,
            overwriteConfiguration: true);
    }

    private async Task RunChecklistAsync(string code)
    {
        await ExecuteBusyAsync(
            () => DemoService.RunChecklistAsync(code, _refreshCancellation.Token),
            () =>
            {
                _isRunningChecklist = true;
                _activeChecklistCode = code;
            },
            () =>
            {
                _isRunningChecklist = false;
                _activeChecklistCode = string.Empty;
            },
            overwriteConfiguration: true);
    }

    private async Task RunAllChecklistAsync()
    {
        await ExecuteBusyAsync(
            () => DemoService.RunAllChecklistAsync(_refreshCancellation.Token),
            () => _isRunningAllChecklist = true,
            () => _isRunningAllChecklist = false,
            overwriteConfiguration: true);
    }

    private async Task ExecuteBusyAsync(Func<Task> action, Action begin, Action end, bool overwriteConfiguration)
    {
        _pageError = string.Empty;
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
            _pageError = exception.Message;
            await LoadStateAsync(overwriteConfiguration);
        }
        finally
        {
            end();
        }
    }

    private string GetRuntimeStatusText()
    {
        return _state?.Configuration.RuntimeStatus switch
        {
            ChannelRuntimeStatus.Running => "监听运行中",
            ChannelRuntimeStatus.Error => "监听异常",
            _ => "监听已停止",
        };
    }

    private string GetRuntimeStatusClass()
    {
        return _state?.Configuration.RuntimeStatus switch
        {
            ChannelRuntimeStatus.Running => "status-chip--running",
            ChannelRuntimeStatus.Error => "status-chip--error",
            _ => "status-chip--stopped",
        };
    }

    private string GetBindingStatusText()
    {
        return _state?.Configuration.IsBound == true ? "已绑定微信 Bot" : "尚未绑定";
    }

    private string GetBindingStatusClass()
    {
        return _state?.Configuration.IsBound == true
            ? "status-chip--bound"
            : "status-chip--pending";
    }

    private static string FormatDateTime(DateTimeOffset? value)
    {
        return value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";
    }

    private static string DisplayOrFallback(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private void ApplyPushRequestDefaults(WeixinDemoState state)
    {
        var currentContact = state.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));
        var suggestedExternalChatId = currentContact?.ExternalChatId ?? state.Configuration.LastExternalChatId;
        var suggestedContextToken = currentContact?.LatestContextToken ?? state.Configuration.LastContextToken;

        if (ShouldApplySuggestedValue(_pushRequest.ExternalChatId, _lastSuggestedExternalChatId))
        {
            _pushRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(_pushRequest.ContextToken, _lastSuggestedContextToken))
        {
            _pushRequest.ContextToken = suggestedContextToken;
        }

        if (ShouldApplySuggestedValue(_mediaRequest.ExternalChatId, _lastSuggestedExternalChatId))
        {
            _mediaRequest.ExternalChatId = suggestedExternalChatId;
        }

        if (ShouldApplySuggestedValue(_mediaRequest.ContextToken, _lastSuggestedContextToken))
        {
            _mediaRequest.ContextToken = suggestedContextToken;
        }

        _lastSuggestedExternalChatId = suggestedExternalChatId;
        _lastSuggestedContextToken = suggestedContextToken;
    }

    private string GetPushTargetHint()
    {
        var currentContact = _state?.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));

        if (currentContact is null)
        {
            return "默认带入最近一次可回复的联系人上下文。";
        }

        var senderName = DisplayOrFallback(currentContact.SenderName, currentContact.ExternalUserId);
        return $"默认带入当前微信用户：{senderName} / {currentContact.ExternalChatId}";
    }

    private void UseKnownContact(KnownContactSession contact)
    {
        _pushRequest.ExternalChatId = contact.ExternalChatId;
        _pushRequest.ContextToken = contact.LatestContextToken;
        _mediaRequest.ExternalChatId = contact.ExternalChatId;
        _mediaRequest.ContextToken = contact.LatestContextToken;
    }

    private string FormatFileSize(long bytes)
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

    private static bool ShouldShowMediaTechnicalDetails(MediaTransferRecord item)
    {
        return !string.IsNullOrWhiteSpace(item.AsrText) ||
               !string.IsNullOrWhiteSpace(item.ContextToken) ||
               !string.IsNullOrWhiteSpace(item.FileKey) ||
               !string.IsNullOrWhiteSpace(item.Md5) ||
               !string.IsNullOrWhiteSpace(item.AesKey) ||
               !string.IsNullOrWhiteSpace(item.DownloadParam) ||
               !string.IsNullOrWhiteSpace(item.LocalCachePath) ||
               item.FileSize > 0;
    }

    private static string GetMediaTypeText(MediaMessageType mediaType)
    {
        return mediaType switch
        {
            MediaMessageType.Image => "图片",
            MediaMessageType.Voice => "语音",
            MediaMessageType.Video => "视频",
            _ => "文件",
        };
    }

    private static string GetMediaStatusText(MediaTransferStatus status)
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

    private static string GetChecklistStatusText(ChecklistItemStatus status)
    {
        return status switch
        {
            ChecklistItemStatus.Passed => "通过",
            ChecklistItemStatus.Failed => "失败",
            ChecklistItemStatus.Blocked => "待条件满足",
            _ => "未执行",
        };
    }

    private IEnumerable<ProtocolCapabilityView> GetProtocolCapabilities()
    {
        var mediaReady = _state?.MediaRecords.Any(item => item.TransferStatus is MediaTransferStatus.Sent or MediaTransferStatus.Downloaded) == true;
        var voiceAsrReady = _state?.MediaRecords.Any(item => item.MediaType == MediaMessageType.Voice && !string.IsNullOrWhiteSpace(item.AsrText)) == true;
        var cursorReady = !string.IsNullOrWhiteSpace(_state?.Configuration.SyncCursor);
        var knownContactReady = _state?.KnownContacts.Count > 0;

        return
        [
            new("get_bot_qrcode", "二维码登录", "已支持", "微信绑定区", "获取二维码并轮询扫码状态，演示 Bot 如何接入。"),
            new("getupdates", "长轮询收消息", _state?.Configuration.RuntimeStatus == ChannelRuntimeStatus.Running ? "运行中" : "已支持", "消息接收区", "通过 getupdates 长轮询接收入站消息，并记录原始上下文。"),
            new("cursor-persistence", "游标持久化", cursorReady ? "已运行" : "待执行", "连接参数 / 消息接收区", "把 get_updates_buf 落盘保存，应用重启后仍可续接。"),
            new("getconfig", "连接自检", _state?.LastConnectionCheck is not null ? "已支持" : "待执行", "协议自检区", "校验当前 token 是否可用，并获取 typing_ticket。"),
            new("sendtyping", "正在输入", !string.IsNullOrWhiteSpace(_state?.Configuration.TypingTicket) ? "已支持" : "待执行", "协议自检/发送链路", "在真正回复前先展示“正在输入”，提升交互体验。"),
            new("sendmessage:text", "文本消息", _state?.LastPushResult is not null || _state?.Messages.Count > 0 ? "已支持" : "待执行", "主动推送区 / 最近入站消息", "支持主动文本发送和文本自动回复。"),
            new("context-cache", "续聊上下文缓存", knownContactReady ? "已积累" : "待消息触发", "已知联系人区", "缓存 ExternalChatId 和 context_token，方便主动消息续聊。"),
            new("getuploadurl+upload", "媒体上传", mediaReady ? "已验证" : "已支持", "媒体消息演示区", "先拿 upload_param，再加密上传到 CDN。"),
            new("c2c/download", "媒体下载回读", _state?.MediaRecords.Any(item => item.TransferStatus == MediaTransferStatus.Downloaded) == true ? "已验证" : "已支持", "媒体记录区", "下载密文并使用 aes_key 解密到本地缓存。"),
            new("voice_asr", "语音 ASR", voiceAsrReady ? "已验证" : "待真实语音", "媒体记录区 / Checklist", "协议若返回 text 字段，就直接展示转写结果。"),
            new("session-expiry", "会话过期恢复", _state?.LastConnectionCheck?.SessionExpired == true ? "已验证" : "待真实联调", "Checklist", "真实联调时触发 errcode=-14，验证重新绑定提示。"),
            new("group-boundary", "群聊边界", "说明中", "协议说明区", "协议中可能出现群聊字段，但当前 Demo 不承诺完整群聊支持。"),
        ];
    }

    private sealed record ProtocolCapabilityView(string Code, string Scene, string Status, string Entry, string Summary);

    private static bool ShouldApplySuggestedValue(string currentValue, string lastSuggestedValue)
    {
        return string.IsNullOrWhiteSpace(currentValue) ||
               string.Equals(currentValue.Trim(), lastSuggestedValue, StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_refreshCancellation.IsCancellationRequested)
        {
            await _refreshCancellation.CancelAsync();
        }

        _refreshCancellation.Dispose();
    }
}
