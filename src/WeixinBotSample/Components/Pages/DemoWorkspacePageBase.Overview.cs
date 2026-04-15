using WeixinBotSample.Components.Common;
using WeixinBotSample.Models;

namespace WeixinBotSample.Components.Pages;

public abstract partial class DemoWorkspacePageBase
{
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

    protected IReadOnlyList<DemoWorkspaceShell.WorkspaceFactItem> GetWorkspaceFacts()
    {
        var passedChecklist = State?.ChecklistItems.Count(item => item.Status == ChecklistItemStatus.Passed) ?? 0;
        var totalChecklist = State?.ChecklistItems.Count ?? 0;
        var mediaCount = State?.MediaRecords.Count ?? 0;
        var contactCount = State?.KnownContacts.Count ?? 0;
        var messageCount = State?.Messages.Count ?? 0;

        return
        [
            new("绑定", GetBindingStatusText(), State?.Configuration.IsBound == true ? "success" : "warning"),
            new("监听", GetRuntimeStatusText(), State?.Configuration.RuntimeStatus == ChannelRuntimeStatus.Running ? "info" : State?.Configuration.RuntimeStatus == ChannelRuntimeStatus.Error ? "danger" : "neutral"),
            new("联系人", contactCount.ToString(), contactCount > 0 ? "info" : "neutral"),
            new("消息", messageCount.ToString(), messageCount > 0 ? "info" : "neutral"),
            new("媒体", mediaCount.ToString(), mediaCount > 0 ? "info" : "neutral"),
            new("Checklist", totalChecklist == 0 ? "0 / 0" : $"{passedChecklist} / {totalChecklist}", passedChecklist > 0 ? "success" : "warning"),
        ];
    }

    protected IReadOnlyList<OverviewInsightItem> GetOverviewInsights()
    {
        var latestContact = State?.KnownContacts.FirstOrDefault();
        var latestMedia = State?.MediaRecords.FirstOrDefault();
        var passedChecklist = State?.ChecklistItems.Count(item => item.Status == ChecklistItemStatus.Passed) ?? 0;
        var totalChecklist = State?.ChecklistItems.Count ?? 0;

        return
        [
            new(
                "bi bi-person-check",
                "最近会话",
                latestContact is null ? "待触发" : DisplayOrFallback(latestContact.SenderName, latestContact.ExternalChatId),
                latestContact is null ? "还没有拿到可续聊的联系人上下文。" : $"最近会话时间：{FormatDateTime(latestContact.LastMessageAt)}",
                latestContact is null ? "warning" : "info"),
            new(
                "bi bi-file-earmark-lock2",
                "媒体链路",
                latestMedia is null ? "尚无记录" : GetMediaTypeText(latestMedia.MediaType),
                latestMedia is null ? "建议至少完成一次上传发送或下载回读。" : $"最近状态：{GetMediaStatusText(latestMedia.TransferStatus)}",
                latestMedia is null ? "warning" : latestMedia.TransferStatus is MediaTransferStatus.Sent or MediaTransferStatus.Downloaded ? "success" : latestMedia.TransferStatus == MediaTransferStatus.Failed ? "danger" : "info"),
            new(
                "bi bi-clipboard-data",
                "联调进度",
                totalChecklist == 0 ? "0 / 0" : $"{passedChecklist} / {totalChecklist}",
                totalChecklist == 0 ? "Checklist 尚未初始化。" : passedChecklist == totalChecklist ? "公开场景检查已全部通过。" : "还有检查项等待现场证据。",
                totalChecklist > 0 && passedChecklist == totalChecklist ? "success" : "warning"),
            new(
                "bi bi-activity",
                "连接状态",
                State?.LastConnectionCheck is null ? "待检测" : (State.LastConnectionCheck.Succeeded ? "可用" : "异常"),
                State?.LastConnectionCheck is null ? "建议先执行一次“验证连接”。" : DisplayOrFallback(State.LastConnectionCheck.Message, "暂无连接说明。"),
                State?.LastConnectionCheck is null ? "warning" : State.LastConnectionCheck.Succeeded ? "success" : "danger"),
        ];
    }

    protected OverviewRecommendation GetOverviewRecommendation()
    {
        if (State?.Configuration.IsBound != true)
        {
            return new OverviewRecommendation("warning", "先完成二维码绑定", "当前还没有拿到可用的 Bot 会话，建议先在首页完成扫码绑定，再开始后续联调。", "留在总览页操作", "/");
        }

        if (State.Configuration.RuntimeStatus != ChannelRuntimeStatus.Running)
        {
            return new OverviewRecommendation("info", "建议启动监听", "监听运行后，系统才能自动沉淀联系人上下文、入站消息和最近会话目标。", "查看消息中心", "/messages");
        }

        if (State.KnownContacts.Count == 0)
        {
            return new OverviewRecommendation("warning", "让真实联系人先发一条消息", "当前已经绑定并运行，但还没有可续聊的会话。拿到 ExternalChatId 和 ContextToken 后，主动推送和媒体联调都会更顺。", "前往消息中心", "/messages");
        }

        if (State.MediaRecords.All(item => item.TransferStatus is not MediaTransferStatus.Sent and not MediaTransferStatus.Downloaded))
        {
            return new OverviewRecommendation("info", "建议执行一次媒体联调", "文本链路已经具备基础条件，下一步可以在媒体页完成上传、发送和下载回读，验证协议的文件能力。", "前往媒体页", "/media");
        }

        var checklistTotal = State.ChecklistItems.Count;
        var checklistPassed = State.ChecklistItems.Count(item => item.Status == ChecklistItemStatus.Passed);
        if (checklistTotal == 0 || checklistPassed < checklistTotal)
        {
            return new OverviewRecommendation("warning", "Checklist 还没有全部闭环", "建议再执行一次联调整体检查，把已经验证的能力和仍需现场触发的场景重新梳理清楚。", "前往联调页", "/checklist");
        }

        return new OverviewRecommendation("success", "当前 Demo 已具备完整演示骨架", "首页、消息、媒体、联调四条主线都已经具备可操作页面，可以继续针对真实账号环境做更细的协议验证。", "复核联调页", "/checklist");
    }

    protected string GetCapabilityStatusClass(string status)
    {
        return status switch
        {
            "已验证" or "已支持" or "已积累" or "已运行" or "运行中" or "可用" => "demo-badge demo-badge--success",
            "待执行" or "待触发" or "待消息触发" or "待真实语音" or "待真实联调" or "待检测" or "说明中" => "demo-badge demo-badge--warning",
            "异常" => "demo-badge demo-badge--danger",
            _ => "demo-badge demo-badge--neutral",
        };
    }

    protected string GetMediaStatusClass(MediaTransferStatus status)
    {
        return status switch
        {
            MediaTransferStatus.Sent or MediaTransferStatus.Downloaded or MediaTransferStatus.Received => "demo-badge demo-badge--success",
            MediaTransferStatus.Failed => "demo-badge demo-badge--danger",
            MediaTransferStatus.Preparing or MediaTransferStatus.Encrypting or MediaTransferStatus.Uploading or MediaTransferStatus.Sending or MediaTransferStatus.Downloading => "demo-badge demo-badge--info",
            _ => "demo-badge demo-badge--neutral",
        };
    }

    protected string GetChecklistStatusClass(ChecklistItemStatus status)
    {
        return status switch
        {
            ChecklistItemStatus.Passed => "demo-badge demo-badge--success",
            ChecklistItemStatus.Failed => "demo-badge demo-badge--danger",
            ChecklistItemStatus.Blocked => "demo-badge demo-badge--warning",
            _ => "demo-badge demo-badge--neutral",
        };
    }

    protected string GetReplyStatusClass(WeixinMessageRecord item)
    {
        var replyStatus = item.ReplyStatus ?? string.Empty;
        if (item.ReplySucceeded)
        {
            return "demo-badge demo-badge--success";
        }

        if (replyStatus.Contains("缺少", StringComparison.Ordinal) ||
            replyStatus.Contains("未执行", StringComparison.Ordinal))
        {
            return "demo-badge demo-badge--warning";
        }

        return "demo-badge demo-badge--danger";
    }

    protected string GetReplyStatusSummary(WeixinMessageRecord item)
    {
        var replyStatus = item.ReplyStatus ?? string.Empty;
        if (item.ReplySucceeded)
        {
            return "回复成功";
        }

        if (replyStatus.Contains("缺少", StringComparison.Ordinal) ||
            replyStatus.Contains("未执行", StringComparison.Ordinal))
        {
            return "待补上下文";
        }

        if (replyStatus.Contains("过期", StringComparison.Ordinal))
        {
            return "会话过期";
        }

        return "回复失败";
    }

    public sealed record ProtocolCapabilityView(string Code, string Scene, string Status, string Entry, string Summary);

    public sealed record OverviewInsightItem(string Icon, string Title, string Value, string Detail, string Tone);

    public sealed record OverviewRecommendation(string Tone, string Title, string Message, string LinkText, string Href);
}
