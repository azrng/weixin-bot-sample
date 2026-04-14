using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    public async Task ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
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
            throw new InvalidOperationException("请先完成微信绑定，再执行连接自检。");
        }

        var client = new WeixinPollingClient(CreateClient(), configuration);
        GetConfigResult configResult;
        try
        {
            configResult = await client.GetConfigAsync(cancellationToken);
        }
        catch (WeixinApiException exception) when (exception.ErrorCode == -14)
        {
            await MarkSessionExpiredAsync("会话已过期，请重新扫码绑定。", CancellationToken.None);
            throw new InvalidOperationException("会话已过期，请重新扫码绑定。", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && IsTypingTicketUnsupported(exception.Message))
        {
            await MarkTypingCapabilityUnavailableAsync(
                "连接已建立，但当前账号暂不支持 Typing Ticket，后续发送消息时会自动跳过“正在输入”状态。",
                exception.Message,
                cancellationToken);
            return;
        }

        var message = string.IsNullOrWhiteSpace(configResult.TypingTicket)
            ? "连接自检通过，但当前账号未返回 typing_ticket。"
            : "连接自检通过，已获取 typing_ticket。";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.TypingTicket = configResult.TypingTicket;
            _state.Configuration.TypingTicketUpdatedAt = DateTimeOffset.UtcNow;
            _state.LastConnectionCheck = new ConnectionCheckResult
            {
                Succeeded = true,
                SessionExpired = false,
                TypingCapabilityAvailable = true,
                Message = message,
                ResponseSummary = TruncateSingleLine(configResult.RawText, 280),
                TypingTicket = configResult.TypingTicket,
                CheckedAt = DateTimeOffset.UtcNow,
            };
            RecordLogNoLock("Success", "连接自检成功。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DemoConfiguration> EnsureTypingTicketAsync(
        WeixinPollingClient client,
        DemoConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.UserId))
        {
            return configuration;
        }

        if (!string.IsNullOrWhiteSpace(configuration.TypingTicket) &&
            configuration.TypingTicketUpdatedAt is not null &&
            DateTimeOffset.UtcNow - configuration.TypingTicketUpdatedAt <= TimeSpan.FromMinutes(30))
        {
            return configuration;
        }

        GetConfigResult configResult;
        try
        {
            configResult = await client.GetConfigAsync(cancellationToken);
        }
        catch (WeixinApiException exception) when (exception.ErrorCode == -14)
        {
            await MarkSessionExpiredAsync("会话已过期，请重新扫码绑定。", CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException && IsTypingTicketUnsupported(exception.Message))
        {
            return await MarkTypingCapabilityUnavailableAsync(
                "当前账号暂不支持 Typing Ticket，后续发送消息时会自动跳过“正在输入”状态。",
                exception.Message,
                cancellationToken);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.TypingTicket = configResult.TypingTicket;
            _state.Configuration.TypingTicketUpdatedAt = DateTimeOffset.UtcNow;
            _state.LastConnectionCheck = new ConnectionCheckResult
            {
                Succeeded = true,
                SessionExpired = false,
                TypingCapabilityAvailable = true,
                Message = string.IsNullOrWhiteSpace(configResult.TypingTicket)
                    ? "已刷新配置，但接口未返回 typing_ticket。"
                    : "已刷新 typing_ticket。",
                ResponseSummary = TruncateSingleLine(configResult.RawText, 280),
                TypingTicket = configResult.TypingTicket,
                CheckedAt = DateTimeOffset.UtcNow,
            };
            await PersistStateNoLockAsync(cancellationToken);
            return _state.Configuration.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TrySendTypingAsync(
        WeixinPollingClient client,
        DemoConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.UserId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.TypingTicket))
        {
            return;
        }

        try
        {
            await client.SendTypingAsync(configuration.UserId, configuration.TypingTicket, cancellationToken);
            await RecordBackgroundLogAsync("Info", "已发送“正在输入”状态。");
        }
        catch (WeixinApiException exception) when (exception.ErrorCode == -14)
        {
            await MarkSessionExpiredAsync(exception.Message, CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RecordBackgroundLogAsync("Warning", $"发送“正在输入”状态失败：{exception.Message}");
        }
    }

    private async Task UpdateSyncCursorAsync(string syncCursor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(syncCursor))
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(_state.Configuration.SyncCursor, syncCursor, StringComparison.Ordinal))
            {
                return;
            }

            _state.Configuration.SyncCursor = syncCursor;
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MarkSessionExpiredAsync(string message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.RuntimeStatus = ChannelRuntimeStatus.Error;
            _state.Configuration.RuntimeError = message;
            _state.Configuration.RuntimeStoppedAt = DateTimeOffset.UtcNow;
            _state.LastConnectionCheck = new ConnectionCheckResult
            {
                Succeeded = false,
                SessionExpired = true,
                TypingCapabilityAvailable = !string.IsNullOrWhiteSpace(_state.Configuration.TypingTicket),
                Message = message,
                ResponseSummary = string.Empty,
                TypingTicket = _state.Configuration.TypingTicket,
                CheckedAt = DateTimeOffset.UtcNow,
            };
            RecordLogNoLock("Error", message);
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DemoConfiguration> MarkTypingCapabilityUnavailableAsync(
        string userMessage,
        string responseSummary,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.Configuration.TypingTicket = string.Empty;
            _state.Configuration.TypingTicketUpdatedAt = DateTimeOffset.UtcNow;
            _state.LastConnectionCheck = new ConnectionCheckResult
            {
                Succeeded = true,
                SessionExpired = false,
                TypingCapabilityAvailable = false,
                Message = userMessage,
                ResponseSummary = TruncateSingleLine(responseSummary, 280),
                TypingTicket = string.Empty,
                CheckedAt = DateTimeOffset.UtcNow,
            };
            RecordLogNoLock("Warning", userMessage);
            await PersistStateNoLockAsync(cancellationToken);
            return _state.Configuration.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsTypingTicketUnsupported(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("GetTypingTicket rpc failed", StringComparison.OrdinalIgnoreCase) ||
               (message.Contains("typing_ticket", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("rpc failed", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class WeixinApiException(
        string message,
        int? errorCode = null,
        string requestPayload = "",
        string responsePayload = "") : InvalidOperationException(message)
    {
        public int? ErrorCode { get; } = errorCode;

        public string RequestPayload { get; } = requestPayload;

        public string ResponsePayload { get; } = responsePayload;
    }
}
