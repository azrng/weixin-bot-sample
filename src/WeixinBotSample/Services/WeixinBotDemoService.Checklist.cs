using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    public async Task RunChecklistAsync(string code, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("缺少 checklist 项目标识。");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureChecklistDefaultsNoLock();
            var item = _state.ChecklistItems.FirstOrDefault(entry => string.Equals(entry.Code, code, StringComparison.Ordinal))
                       ?? throw new InvalidOperationException("未找到指定的 checklist 项。");

            var result = EvaluateChecklistItem(code, _state);
            item.Status = result.Status;
            item.Message = result.Message;
            item.Evidence = result.Evidence;
            item.CheckedAt = DateTimeOffset.UtcNow;
            RecordLogNoLock("Info", $"已执行联调检查：{item.Name}。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RunAllChecklistAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        foreach (var code in BuildChecklistDefaults().Select(item => item.Code))
        {
            await RunChecklistAsync(code, cancellationToken);
        }
    }

    private ChecklistEvaluationResult EvaluateChecklistItem(string code, WeixinDemoState state)
    {
        return code switch
        {
            "env" => EvaluateEnvironmentChecklist(state),
            "binding" => EvaluateBindingChecklist(state),
            "typing" => EvaluateTypingChecklist(state),
            "text" => EvaluateTextChecklist(state),
            "media" => EvaluateMediaChecklist(state),
            "voice_asr" => EvaluateVoiceAsrChecklist(state),
            "session_expiry" => EvaluateSessionExpiryChecklist(state),
            _ => new ChecklistEvaluationResult(ChecklistItemStatus.Blocked, "未定义的检查项。", "需要补充检查逻辑。"),
        };
    }

    private ChecklistEvaluationResult EvaluateEnvironmentChecklist(WeixinDemoState state)
    {
        var configuration = state.Configuration;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.BaseUrl)) missing.Add("BaseUrl");
        if (string.IsNullOrWhiteSpace(configuration.ChannelVersion)) missing.Add("ChannelVersion");
        if (string.IsNullOrWhiteSpace(configuration.AccountId)) missing.Add("AccountId");
        if (string.IsNullOrWhiteSpace(configuration.UserId)) missing.Add("UserId");
        if (string.IsNullOrWhiteSpace(configuration.Token)) missing.Add("Token");
        var cacheDirectory = Path.Combine(environment.ContentRootPath, "App_Data", "media-cache");
        var cacheDirectoryReady = TryEnsureChecklistDirectory(cacheDirectory, out var cacheEvidence);
        if (!cacheDirectoryReady) missing.Add("MediaCache");

        return missing.Count == 0
            ? new ChecklistEvaluationResult(
                ChecklistItemStatus.Passed,
                "环境参数完整，可继续执行联调。",
                $"BaseUrl={configuration.BaseUrl}；协议版本={configuration.ChannelVersion}；缓存目录={cacheEvidence}")
            : new ChecklistEvaluationResult(
                ChecklistItemStatus.Failed,
                "环境参数未准备完整。",
                $"缺少：{string.Join("、", missing)}；缓存检查={cacheEvidence}");
    }

    private static ChecklistEvaluationResult EvaluateBindingChecklist(WeixinDemoState state)
    {
        return state.Configuration.IsBound
            ? new ChecklistEvaluationResult(ChecklistItemStatus.Passed, "绑定状态正常。", $"账号：{state.Configuration.BoundAccountName}")
            : new ChecklistEvaluationResult(ChecklistItemStatus.Failed, "尚未完成二维码绑定。", "请先执行“绑定微信”并确认账号信息已回写。");
    }

    private static ChecklistEvaluationResult EvaluateTypingChecklist(WeixinDemoState state)
    {
        if (state.LastConnectionCheck is not null &&
            state.LastConnectionCheck.Succeeded &&
            !state.LastConnectionCheck.TypingCapabilityAvailable)
        {
            return new ChecklistEvaluationResult(
                ChecklistItemStatus.Blocked,
                "当前账号暂不支持 Typing Ticket，系统已降级跳过 sendtyping。",
                state.LastConnectionCheck.Message);
        }

        if (!string.IsNullOrWhiteSpace(state.Configuration.TypingTicket) && state.LastConnectionCheck?.Succeeded == true)
        {
            return new ChecklistEvaluationResult(
                ChecklistItemStatus.Passed,
                "Typing 链路已具备前置条件。",
                $"TypingTicket 更新时间：{state.Configuration.TypingTicketUpdatedAt:yyyy-MM-dd HH:mm:ss}");
        }

        return new ChecklistEvaluationResult(ChecklistItemStatus.Blocked, "尚未获取 typing_ticket。", "请先执行“验证连接”或等待发送链路自动刷新配置。");
    }

    private static ChecklistEvaluationResult EvaluateTextChecklist(WeixinDemoState state)
    {
        var replied = state.Messages.FirstOrDefault(item => item.ReplySucceeded);
        if (replied is not null)
        {
            return new ChecklistEvaluationResult(ChecklistItemStatus.Passed, "文本收发链路已验证。", $"最近成功回复：{replied.Text}");
        }

        if (state.LastPushResult?.Succeeded == true)
        {
            return new ChecklistEvaluationResult(ChecklistItemStatus.Passed, "主动文本推送链路已验证。", $"目标：{state.LastPushResult.ExternalChatId}");
        }

        return new ChecklistEvaluationResult(ChecklistItemStatus.Blocked, "尚未检测到成功的文本收发记录。", "请先完成一次文本自动回复或主动推送。");
    }

    private static ChecklistEvaluationResult EvaluateMediaChecklist(WeixinDemoState state)
    {
        var completed = state.MediaRecords.FirstOrDefault(item =>
            item.TransferStatus is MediaTransferStatus.Sent or MediaTransferStatus.Downloaded);
        if (completed is not null)
        {
            return new ChecklistEvaluationResult(
                ChecklistItemStatus.Passed,
                "媒体链路已至少成功执行一次。",
                $"{completed.Direction} / {GetMediaTypeLabel(completed.MediaType)} / {completed.FileName}");
        }

        return new ChecklistEvaluationResult(ChecklistItemStatus.Blocked, "尚未检测到成功的媒体发送或下载。", "请先完成一次媒体上传发送或下载解密。");
    }

    private static ChecklistEvaluationResult EvaluateVoiceAsrChecklist(WeixinDemoState state)
    {
        var voice = state.MediaRecords.FirstOrDefault(item =>
            item.MediaType == MediaMessageType.Voice &&
            !string.IsNullOrWhiteSpace(item.AsrText));
        if (voice is not null)
        {
            return new ChecklistEvaluationResult(ChecklistItemStatus.Passed, "语音 ASR 结果已获取。", voice.AsrText);
        }

        return new ChecklistEvaluationResult(ChecklistItemStatus.Blocked, "尚未获取带 ASR 文本的语音消息。", "需要真实收到一条语音消息并由协议返回转写文本。");
    }

    private static ChecklistEvaluationResult EvaluateSessionExpiryChecklist(WeixinDemoState state)
    {
        if (state.LastConnectionCheck?.SessionExpired == true ||
            state.Configuration.RuntimeError.Contains("会话已过期", StringComparison.Ordinal))
        {
            return new ChecklistEvaluationResult(ChecklistItemStatus.Passed, "已识别会话过期场景。", state.Configuration.RuntimeError);
        }

        return new ChecklistEvaluationResult(ChecklistItemStatus.Blocked, "尚未触发会话过期。", "该项需要真实联调时出现 errcode=-14 才能完成验证。");
    }

    private static bool TryEnsureChecklistDirectory(string directoryPath, out string evidence)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            evidence = directoryPath;
            return true;
        }
        catch (Exception exception)
        {
            evidence = $"目录不可用：{exception.Message}";
            return false;
        }
    }

    private sealed record ChecklistEvaluationResult(ChecklistItemStatus Status, string Message, string Evidence);
}
