using System.Text.Json;
using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    private const string DefaultBaseUrl = "https://ilinkai.weixin.qq.com";
    private const string DefaultBotType = "3";
    private const int ActiveLoginTtlMilliseconds = 5 * 60 * 1000;
    private const int QrLongPollingTimeoutMilliseconds = 35_000;
    private const int DefaultPollDelayMilliseconds = 35_000;
    private const int MaxLogEntries = 80;
    private const int MaxMessageEntries = 30;

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

        var client = new WeixinPollingClient(CreateClient(), configuration);
        var result = await client.SendTextMessageAsync(
            request.ExternalChatId.Trim(),
            request.ContextToken.Trim(),
            request.Content.Trim(),
            cancellationToken);

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

            _state.LatestReplyText = record.ReplyText;
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
}
