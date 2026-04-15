using WeixinBotSample.Models;

namespace WeixinBotSample.Components.Pages;

public abstract partial class DemoWorkspacePageBase
{
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

    protected void ClearPushValidationMessage()
    {
        PushValidationMessage = string.Empty;
    }

    protected void ClearMediaValidationMessage()
    {
        MediaValidationMessage = string.Empty;
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

    protected bool HasActiveAutoFillPrompt()
    {
        return ActiveAutoFillPrompt?.HasTarget == true;
    }

    protected string GetPushFieldClass(string currentValue, string targetValue)
    {
        var baseClass = "form-control";
        var classNames = new List<string> { baseClass };
        if (ShouldHighlightPushFieldAsMissing(currentValue))
        {
            classNames.Add("demo-input--missing");
        }

        if (HasActiveAutoFillPrompt() &&
            !string.IsNullOrWhiteSpace(targetValue) &&
            string.Equals(currentValue.Trim(), targetValue.Trim(), StringComparison.Ordinal))
        {
            classNames.Add("demo-input--autofill");
        }

        return string.Join(" ", classNames);
    }

    protected string GetAutoFillPromptTitle()
    {
        if (!HasActiveAutoFillPrompt())
        {
            return string.Empty;
        }

        var senderName = DisplayOrFallback(ActiveAutoFillPrompt!.SenderName, ActiveAutoFillPrompt.ExternalChatId);
        return $"已自动带入最近会话目标：{senderName}";
    }

    protected string GetAutoFillPromptMessage()
    {
        if (!HasActiveAutoFillPrompt())
        {
            return string.Empty;
        }

        return $"{DisplayOrFallback(ActiveAutoFillPrompt!.Summary, "收到新的可回复会话。")} 现在可以直接发送主动消息。";
    }

    protected string GetPushTargetHint()
    {
        var currentContact = State?.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));

        if (currentContact is null)
        {
            if (State?.Configuration.IsBound == true)
            {
                var boundAccount = DisplayOrFallback(State.Configuration.UserId, State.Configuration.AccountId);
                return $"当前已绑定账号：{boundAccount}。主动推送仍需要最近会话的 ExternalChatId 和 ContextToken；只要先收到一条用户消息，系统就会自动带入。";
            }

            return "默认带入最近一次可回复的联系人上下文。";
        }

        var senderName = DisplayOrFallback(currentContact.SenderName, currentContact.ExternalUserId);
        return $"默认带入当前微信用户：{senderName} / {currentContact.ExternalChatId}";
    }

    protected string GetMediaTargetHint()
    {
        var currentContact = State?.KnownContacts.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken));

        if (currentContact is null)
        {
            if (State?.Configuration.IsBound == true)
            {
                var boundAccount = DisplayOrFallback(State.Configuration.UserId, State.Configuration.AccountId);
                return $"当前已绑定账号：{boundAccount}。媒体发送同样需要最近会话的 ExternalChatId 和 ContextToken；先收到一条用户消息后，系统才能自动带入。";
            }

            return "默认带入最近一次可发送媒体的联系人上下文。";
        }

        var senderName = DisplayOrFallback(currentContact.SenderName, currentContact.ExternalUserId);
        return $"默认带入当前会话目标：{senderName} / {currentContact.ExternalChatId}";
    }

    protected bool ShouldShowPushReadinessNotice()
    {
        return !HasKnownPushTarget() &&
               string.IsNullOrWhiteSpace(PushRequest.ExternalChatId) &&
               string.IsNullOrWhiteSpace(PushRequest.ContextToken);
    }

    protected string GetPushReadinessNotice()
    {
        if (State?.Configuration.IsBound == true)
        {
            return "当前账号已绑定，但还没有可续聊的微信会话。请先启动监听，并让微信用户至少发来一条消息；系统拿到 ExternalChatId 和 ContextToken 后，就会自动带入到这里。";
        }

        return "请先完成微信绑定并启动监听，再让微信用户发来一条消息，系统拿到 ExternalChatId 和 ContextToken 后才能主动推送。";
    }

    protected bool IsVoiceMediaType()
    {
        return MediaRequest.MediaType == MediaMessageType.Voice;
    }

    protected string GetMediaDescriptionLabel()
    {
        return IsVoiceMediaType() ? "附加说明 / 语音 ASR 备注" : "附加说明";
    }

    protected bool ShouldShowMediaSelfTargetHint()
    {
        return !string.IsNullOrWhiteSpace(MediaRequest.ExternalChatId) &&
               !string.IsNullOrWhiteSpace(State?.Configuration.UserId) &&
               string.Equals(MediaRequest.ExternalChatId.Trim(), State.Configuration.UserId.Trim(), StringComparison.Ordinal);
    }

    protected string GetMediaSelfTargetHint()
    {
        return "当前目标会话就是已绑定 Bot 自己的微信账号。文本消息可以继续演示，但媒体链路如果在 getuploadurl 阶段持续返回 errcode=-2，建议先换成另一位真实联系人发来的会话再继续联调。";
    }

    protected bool ShouldShowLatestMediaUploadFailureHint()
    {
        return GetLatestMediaUploadFailureRecord() is not null;
    }

    protected string GetLatestMediaUploadFailureHint()
    {
        var latestFailure = GetLatestMediaUploadFailureRecord();
        if (latestFailure is null)
        {
            return string.Empty;
        }

        var traceHint = string.IsNullOrWhiteSpace(latestFailure.TraceFilePath)
            ? "当前失败发生在 getuploadurl 阶段，尚未进入 CDN 上传和 sendmessage。"
            : $"当前失败发生在 getuploadurl 阶段，尚未进入 CDN 上传和 sendmessage。Trace 文件：{latestFailure.TraceFilePath}";
        return $"{traceHint} 文件：{DisplayOrFallback(latestFailure.FileName, "未命名文件")}，密文大小：{FormatFileSize(latestFailure.EncryptedFileSize)}。这通常更接近账号媒体能力、文件类型/大小限制或当前联调场景限制，而不是 ExternalChatId / ContextToken 填写错误。";
    }

    protected string GetMediaFieldClass(string currentValue)
    {
        return !string.IsNullOrWhiteSpace(MediaValidationMessage) && string.IsNullOrWhiteSpace(currentValue)
            ? "form-control demo-input--missing"
            : "form-control";
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
}
