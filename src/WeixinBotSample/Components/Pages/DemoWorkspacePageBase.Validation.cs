namespace WeixinBotSample.Components.Pages;

public abstract partial class DemoWorkspacePageBase
{
    private bool TryValidatePushRequest(out string message)
    {
        if (string.IsNullOrWhiteSpace(PushRequest.Content))
        {
            message = "请先填写要发送的消息内容。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(PushRequest.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(PushRequest.ContextToken))
        {
            message = string.Empty;
            return true;
        }

        if (!HasKnownPushTarget())
        {
            message = GetPushReadinessNotice();
            return false;
        }

        message = "请先从上方“已知联系人”点击“带入推送”，或手动补全 ExternalChatId 与 ContextToken。";
        return false;
    }

    private bool HasKnownPushTarget()
    {
        return State?.KnownContacts.Any(item =>
            !string.IsNullOrWhiteSpace(item.ExternalChatId) &&
            !string.IsNullOrWhiteSpace(item.LatestContextToken)) == true;
    }

    private bool ShouldHighlightPushFieldAsMissing(string currentValue)
    {
        return !string.IsNullOrWhiteSpace(PushValidationMessage) &&
               string.IsNullOrWhiteSpace(currentValue);
    }

    private bool TryValidateMediaRequest(out string message)
    {
        if (string.IsNullOrWhiteSpace(MediaRequest.ExternalChatId) || string.IsNullOrWhiteSpace(MediaRequest.ContextToken))
        {
            message = "媒体消息也需要真实会话的 ExternalChatId 和 ContextToken。请先让目标联系人发来一条消息，再点击“带入推送”或手动填写。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(SelectedMediaTempPath) || !File.Exists(SelectedMediaTempPath) || string.IsNullOrWhiteSpace(MediaRequest.FileName))
        {
            message = "请先选择一个要发送的媒体文件。";
            return false;
        }

        if (MediaRequest.MediaType == Models.MediaMessageType.Image &&
            !string.IsNullOrWhiteSpace(MediaRequest.ContentType) &&
            !MediaRequest.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            message = "当前媒体类型选择的是“图片”，但所选文件不是图片内容，请确认文件类型或切换媒体类型。";
            return false;
        }

        if (MediaRequest.MediaType == Models.MediaMessageType.Voice)
        {
            if (MediaRequest.EncodeType <= 0)
            {
                message = "语音消息需要填写有效的编码类型。";
                return false;
            }

            if (MediaRequest.PlayTimeMilliseconds <= 0)
            {
                message = "语音消息需要填写大于 0 的语音时长（毫秒）。";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    private void NormalizeMediaRequestForSend()
    {
        if (MediaRequest.MediaType != Models.MediaMessageType.Voice)
        {
            MediaRequest.EncodeType = 0;
            MediaRequest.PlayTimeMilliseconds = 0;
        }
    }

    private Models.MediaTransferRecord? GetLatestMediaUploadFailureRecord()
    {
        return State?.MediaRecords.FirstOrDefault(item =>
            item.Direction == "Outbound" &&
            item.TransferStatus == Models.MediaTransferStatus.Failed &&
            (item.ResponseSummary.Contains("getuploadurl", StringComparison.OrdinalIgnoreCase) ||
             item.StatusMessage.Contains("上传参数校验失败", StringComparison.OrdinalIgnoreCase)));
    }
}
