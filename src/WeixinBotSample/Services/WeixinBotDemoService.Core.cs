using System.Text.Json;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private const string DefaultBaseUrl = "https://ilinkai.weixin.qq.com";
    private const string DefaultChannelVersion = "1.0.3";
    private const string DefaultBotType = "3";
    private const int ActiveLoginTtlMilliseconds = 5 * 60 * 1000;
    private const int QrLongPollingTimeoutMilliseconds = 35_000;
    private const int DefaultPollDelayMilliseconds = 35_000;
    private const int PollingRetryDelayMilliseconds = 2_000;
    private const int PollingBackoffDelayMilliseconds = 30_000;
    private const int PollingBackoffFailureThreshold = 3;
    private const int MaxLogEntries = 80;
    private const int MaxMessageEntries = 30;
    private const int MaxKnownContactEntries = 20;
    private const int MaxMediaEntries = 24;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WeixinDemoState _state = new();
    private bool _initialized;
    private CancellationTokenSource? _bindingCancellation;
    private Task? _bindingTask;
    private CancellationTokenSource? _pollingCancellation;
    private Task? _pollingTask;

    public async Task<WeixinDemoState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _state.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveConfigurationAsync(DemoConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await EnsureInitializedAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.BaseUrl = NormalizeBaseUrl(configuration.BaseUrl);
            _state.Configuration.ChannelVersion = NormalizeChannelVersion(configuration.ChannelVersion);
            _state.Configuration.RouteTag = configuration.RouteTag.Trim();
            _state.Configuration.Token = configuration.Token.Trim();
            _state.Configuration.AccountId = configuration.AccountId.Trim();
            _state.Configuration.UserId = configuration.UserId.Trim();
            _state.Configuration.UpdatedAt = DateTimeOffset.UtcNow;
            RecordLogNoLock("Info", "基础配置已保存。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        DemoConfiguration configuration;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            configuration = _state.Configuration.Clone();
            ValidateRuntimeConfiguration(configuration);

            if (_pollingTask is not null && !_pollingTask.IsCompleted)
            {
                RecordLogNoLock("Info", "监听已经在运行，无需重复启动。");
                await PersistStateNoLockAsync(cancellationToken);
                return;
            }

            _state.Configuration.RuntimeStatus = ChannelRuntimeStatus.Running;
            _state.Configuration.RuntimeError = string.Empty;
            _state.Configuration.RuntimeStartedAt = DateTimeOffset.UtcNow;
            _state.Configuration.RuntimeStoppedAt = null;
            RecordLogNoLock("Info", "已启动微信长轮询监听。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        _pollingCancellation?.Cancel();
        _pollingCancellation?.Dispose();
        _pollingCancellation = new CancellationTokenSource();
        _pollingTask = Task.Run(() => RunPollingLoopAsync(configuration, _pollingCancellation.Token), CancellationToken.None);
    }

    public async Task StopListeningAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        _pollingCancellation?.Cancel();
        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.RuntimeStatus = ChannelRuntimeStatus.Stopped;
            _state.Configuration.RuntimeStoppedAt = DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(_state.Configuration.RuntimeError))
            {
                _state.Configuration.RuntimeError = string.Empty;
            }

            RecordLogNoLock("Info", "已停止微信监听。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SendPushMessageAsync(PushMessageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await EnsureInitializedAsync(cancellationToken);

        DemoConfiguration configuration;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            configuration = _state.Configuration.Clone();
        }
        finally
        {
            _gate.Release();
        }

        if (string.IsNullOrWhiteSpace(configuration.Token))
        {
            throw new InvalidOperationException("请先完成微信绑定，确保 token 已保存。");
        }

        if (string.IsNullOrWhiteSpace(request.ExternalChatId) ||
            string.IsNullOrWhiteSpace(request.ContextToken) ||
            string.IsNullOrWhiteSpace(request.Content))
        {
            throw new InvalidOperationException("主动推送需要填写 ExternalChatId、ContextToken 和消息内容。");
        }

        SendMessageResult result;
        var client = new WeixinPollingClient(CreateClient(), configuration);
        try
        {
            configuration = await EnsureTypingTicketAsync(client, configuration, cancellationToken);
            await TrySendTypingAsync(client, configuration, cancellationToken);
            result = await client.SendTextMessageAsync(
                request.ExternalChatId.Trim(),
                request.ContextToken.Trim(),
                request.Content.Trim(),
                cancellationToken);
        }
        catch (WeixinApiException exception) when (exception.ErrorCode == -14)
        {
            await MarkSessionExpiredAsync("会话已过期，请重新扫码绑定。", CancellationToken.None);
            throw new InvalidOperationException("会话已过期，请重新扫码绑定。", exception);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.LastExternalChatId = request.ExternalChatId.Trim();
            _state.Configuration.LastContextToken = request.ContextToken.Trim();
            _state.LastPushResult = new PushMessageResult
            {
                Succeeded = true,
                Message = "主动推送消息发送成功。",
                ResponseSummary = TruncateSingleLine(result.RawText, 280),
                ExternalChatId = request.ExternalChatId.Trim(),
                ContextToken = request.ContextToken.Trim(),
                Content = request.Content.Trim(),
                SentAt = DateTimeOffset.UtcNow,
            };
            UpsertKnownContactNoLock(new WeixinMessageRecord
            {
                ExternalChatId = request.ExternalChatId.Trim(),
                ExternalUserId = request.ExternalChatId.Trim(),
                SenderName = request.ExternalChatId.Trim(),
                ChatName = request.ExternalChatId.Trim(),
                ContextToken = request.ContextToken.Trim(),
                Text = request.Content.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            RecordLogNoLock("Success", $"主动推送成功，目标会话 {request.ExternalChatId.Trim()}。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            try
            {
                _state = await stateStore.LoadAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to load persisted weixin demo state.");
                _state = new WeixinDemoState
                {
                    LoadError = $"状态文件读取失败：{exception.Message}",
                };
                RecordLogNoLock("Error", _state.LoadError);
                await PersistStateNoLockAsync(cancellationToken);
            }

            _state.PrimaryGreeting = fixedGreetingService.PrimaryGreeting;
            EnsureChecklistDefaultsNoLock();
            if (_state.Configuration.RuntimeStatus == ChannelRuntimeStatus.Running)
            {
                _state.Configuration.RuntimeStatus = ChannelRuntimeStatus.Stopped;
                _state.Configuration.RuntimeError = "应用已重启，如需继续演示请重新启动监听。";
                _state.Configuration.RuntimeStoppedAt = DateTimeOffset.UtcNow;
                RecordLogNoLock("Warning", "检测到上次运行状态未完成，已回落为停止状态。");
            }

            _initialized = true;
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistInboundRecordAsync(WeixinMessageRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.LastExternalChatId = record.ExternalChatId;
            _state.Configuration.LastContextToken = record.ContextToken;
            _state.Messages.Insert(0, record);
            if (_state.Messages.Count > MaxMessageEntries)
            {
                _state.Messages = _state.Messages.Take(MaxMessageEntries).ToList();
            }

            UpsertKnownContactNoLock(record);
            _state.LatestReplyText = record.ReplyText;
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistMediaRecordAsync(MediaTransferRecord record, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            record.UpdatedAt = DateTimeOffset.UtcNow;
            UpsertMediaRecordNoLock(record);
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RecordBackgroundLogAsync(string level, string message)
    {
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            RecordLogNoLock(level, message);
            await PersistStateNoLockAsync(CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PersistStateNoLockAsync(CancellationToken cancellationToken)
    {
        _state.UpdatedAt = DateTimeOffset.UtcNow;
        await stateStore.SaveAsync(_state, cancellationToken);
    }

    private void RecordLogNoLock(string level, string message)
    {
        _state.Logs.Insert(0, new OperationLogEntry
        {
            CreatedAt = DateTimeOffset.UtcNow,
            Level = level,
            Message = message.Trim(),
        });

        if (_state.Logs.Count > MaxLogEntries)
        {
            _state.Logs = _state.Logs.Take(MaxLogEntries).ToList();
        }
    }

    private static void ValidateRuntimeConfiguration(DemoConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Token) || string.IsNullOrWhiteSpace(configuration.AccountId))
        {
            throw new InvalidOperationException("启动监听前请先完成微信绑定，确保 token 和 accountId 已保存。");
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("WeixinBotDemo");
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    private void UpsertKnownContactNoLock(WeixinMessageRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ExternalChatId))
        {
            return;
        }

        var externalUserId = string.IsNullOrWhiteSpace(record.ExternalUserId)
            ? record.ExternalChatId
            : record.ExternalUserId.Trim();
        var existing = _state.KnownContacts.FirstOrDefault(item =>
            string.Equals(item.ExternalChatId, record.ExternalChatId, StringComparison.Ordinal) ||
            string.Equals(item.ExternalUserId, externalUserId, StringComparison.Ordinal));

        if (existing is null)
        {
            existing = new KnownContactSession();
            _state.KnownContacts.Insert(0, existing);
        }

        existing.ExternalUserId = externalUserId;
        existing.ExternalChatId = record.ExternalChatId.Trim();
        existing.SenderName = record.SenderName.Trim();
        existing.ChatName = record.ChatName.Trim();
        existing.LatestContextToken = record.ContextToken.Trim();
        existing.LastMessageText = record.Text.Trim();
        existing.LastMessageAt = record.CreatedAt;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        _state.KnownContacts = _state.KnownContacts
            .OrderByDescending(item => item.LastMessageAt)
            .ThenByDescending(item => item.UpdatedAt)
            .Take(MaxKnownContactEntries)
            .ToList();
    }

    private void UpsertMediaRecordNoLock(MediaTransferRecord record)
    {
        var existing = _state.MediaRecords.FirstOrDefault(item =>
            string.Equals(item.Id, record.Id, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(record.MessageId) && string.Equals(item.MessageId, record.MessageId, StringComparison.Ordinal)));

        if (existing is null)
        {
            _state.MediaRecords.Insert(0, record);
        }
        else
        {
            var index = _state.MediaRecords.IndexOf(existing);
            _state.MediaRecords[index] = record;
        }

        _state.MediaRecords = _state.MediaRecords
            .OrderByDescending(item => item.UpdatedAt)
            .ThenByDescending(item => item.CreatedAt)
            .Take(MaxMediaEntries)
            .ToList();
    }

    private void EnsureChecklistDefaultsNoLock()
    {
        var defaults = BuildChecklistDefaults();
        foreach (var item in defaults)
        {
            if (_state.ChecklistItems.All(existing => !string.Equals(existing.Code, item.Code, StringComparison.Ordinal)))
            {
                _state.ChecklistItems.Add(item);
            }
        }

        _state.ChecklistItems = _state.ChecklistItems
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static List<ChecklistItemRecord> BuildChecklistDefaults()
    {
        return
        [
            new ChecklistItemRecord { Code = "env", Name = "环境检查", Description = "检查必要参数、缓存目录和协议版本是否就绪。" },
            new ChecklistItemRecord { Code = "binding", Name = "绑定检查", Description = "确认二维码绑定、Token 与账号状态可用。" },
            new ChecklistItemRecord { Code = "typing", Name = "Typing 检查", Description = "确认 getconfig 与 sendtyping 链路已跑通或具备前置条件。" },
            new ChecklistItemRecord { Code = "text", Name = "文本链路检查", Description = "确认文本收消息、自动回复或主动推送已经发生。" },
            new ChecklistItemRecord { Code = "media", Name = "媒体链路检查", Description = "确认媒体上传发送或下载解密已至少完成一次。" },
            new ChecklistItemRecord { Code = "voice_asr", Name = "语音 ASR 检查", Description = "确认收到语音消息并拿到协议自带的转写文本。" },
            new ChecklistItemRecord { Code = "session_expiry", Name = "会话过期恢复检查", Description = "确认 errcode=-14 场景被识别，并能引导重新扫码。" },
        ];
    }

    private static string NormalizeChannelVersion(string? value)
        => string.IsNullOrWhiteSpace(value) ? DefaultChannelVersion : value.Trim();
}
