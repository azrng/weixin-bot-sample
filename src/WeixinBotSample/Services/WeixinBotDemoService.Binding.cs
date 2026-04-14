using WeixinBotSample.Models;

namespace WeixinBotSample.Services;

public sealed partial class WeixinBotDemoService
{
    public async Task StartBindingAsync(bool forceRefresh, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        DemoConfiguration configuration;
        BindingSessionState? existingSession;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            configuration = _state.Configuration.Clone();
            existingSession = _state.ActiveBindingSession?.Clone();
        }
        finally
        {
            _gate.Release();
        }

        if (!forceRefresh &&
            existingSession is not null &&
            !existingSession.IsExpired &&
            DateTimeOffset.UtcNow - existingSession.StartedAt < TimeSpan.FromMilliseconds(ActiveLoginTtlMilliseconds))
        {
            StartBindingWatcher(existingSession.SessionKey, configuration);
            return;
        }

        var client = CreateClient();
        var qrResponse = await FetchQrCodeAsync(client, configuration.BaseUrl, configuration.RouteTag, cancellationToken);
        var qrDisplayUrl = await NormalizeQrDisplayUrlAsync(client, qrResponse.QrCodeImageContent, cancellationToken);
        var sessionKey = string.IsNullOrWhiteSpace(configuration.AccountId)
            ? Guid.NewGuid().ToString("N")
            : configuration.AccountId.Trim();

        var session = new BindingSessionState
        {
            SessionKey = sessionKey,
            QrCode = qrResponse.QrCode,
            QrCodeUrl = qrResponse.QrCodeImageContent,
            QrCodeDataUrl = qrDisplayUrl,
            Message = "请使用微信扫描二维码完成绑定。",
            StartedAt = DateTimeOffset.UtcNow,
            IsExpired = false,
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _state.ActiveBindingSession = session;
            _state.PrimaryGreeting = fixedGreetingService.PrimaryGreeting;
            RecordLogNoLock("Info", "已获取新的绑定二维码。");
            await PersistStateNoLockAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        StartBindingWatcher(session.SessionKey, configuration);
    }

    private void StartBindingWatcher(string sessionKey, DemoConfiguration configuration)
    {
        _bindingCancellation?.Cancel();
        _bindingCancellation?.Dispose();
        _bindingCancellation = new CancellationTokenSource();
        _bindingTask = Task.Run(() => WatchBindingAsync(sessionKey, configuration, _bindingCancellation.Token), CancellationToken.None);
    }

    private async Task WatchBindingAsync(string sessionKey, DemoConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                BindingSessionState? currentSession;
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    currentSession = _state.ActiveBindingSession?.Clone();
                }
                finally
                {
                    _gate.Release();
                }

                if (currentSession is null || !string.Equals(currentSession.SessionKey, sessionKey, StringComparison.Ordinal))
                {
                    return;
                }

                var status = await PollQrStatusAsync(
                    CreateClient(),
                    configuration.BaseUrl,
                    currentSession.QrCode,
                    configuration.RouteTag,
                    cancellationToken);

                await _gate.WaitAsync(cancellationToken);
                try
                {
                    if (_state.ActiveBindingSession is null ||
                        !string.Equals(_state.ActiveBindingSession.SessionKey, sessionKey, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _state.ActiveBindingSession.LastPolledAt = DateTimeOffset.UtcNow;

                    switch (status.Status)
                    {
                        case "wait":
                            _state.ActiveBindingSession.Message = "二维码已就绪，等待微信扫码。";
                            break;
                        case "scaned":
                            _state.ActiveBindingSession.Message = "二维码已扫描，请在微信中确认。";
                            break;
                        case "expired":
                            _state.ActiveBindingSession.Message = "二维码已过期，请刷新后重新绑定。";
                            _state.ActiveBindingSession.IsExpired = true;
                            RecordLogNoLock("Warning", "绑定二维码已过期。");
                            await PersistStateNoLockAsync(cancellationToken);
                            return;
                        case "confirmed":
                            var previousConfiguration = _state.Configuration.Clone();
                            _state.Configuration.Token = status.Token?.Trim() ?? string.Empty;
                            _state.Configuration.AccountId = status.AccountId?.Trim() ?? _state.Configuration.AccountId;
                            _state.Configuration.UserId = status.UserId?.Trim() ?? _state.Configuration.UserId;
                            _state.Configuration.BaseUrl = string.IsNullOrWhiteSpace(status.BaseUrl)
                                ? NormalizeBaseUrl(configuration.BaseUrl)
                                : NormalizeBaseUrl(status.BaseUrl);
                            _state.Configuration.ChannelVersion = NormalizeChannelVersion(configuration.ChannelVersion);
                            _state.Configuration.IsBound = !string.IsNullOrWhiteSpace(_state.Configuration.Token) &&
                                                           !string.IsNullOrWhiteSpace(_state.Configuration.AccountId);
                            _state.Configuration.BoundAccountName = _state.Configuration.AccountId;
                            _state.Configuration.SyncCursor = string.Empty;
                            _state.Configuration.TypingTicket = string.Empty;
                            _state.Configuration.TypingTicketUpdatedAt = null;
                            _state.Configuration.UpdatedAt = DateTimeOffset.UtcNow;
                            if (ShouldResetConversationContext(previousConfiguration, _state.Configuration))
                            {
                                _state.Configuration.LastExternalChatId = string.Empty;
                                _state.Configuration.LastContextToken = string.Empty;
                                _state.Messages.Clear();
                                _state.KnownContacts.Clear();
                                _state.LastPushResult = null;
                                _state.LastConnectionCheck = null;
                                _state.PendingAutoFill = null;
                                _state.LatestReplyText = string.Empty;
                            }
                            _state.ActiveBindingSession.Message = "绑定成功。";
                            RecordLogNoLock("Success", $"微信绑定成功，账号 {_state.Configuration.AccountId} 已接入。");
                            _state.ActiveBindingSession = null;
                            await PersistStateNoLockAsync(cancellationToken);
                            return;
                        default:
                            _state.ActiveBindingSession.Message = $"收到未知绑定状态：{status.Status}";
                            break;
                    }

                    await PersistStateNoLockAsync(cancellationToken);
                }
                finally
                {
                    _gate.Release();
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Binding watcher failed.");
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (_state.ActiveBindingSession is not null)
                {
                    _state.ActiveBindingSession.Message = $"绑定轮询失败：{exception.Message}";
                }

                RecordLogNoLock("Error", $"绑定轮询失败：{exception.Message}");
                await PersistStateNoLockAsync(CancellationToken.None);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private static bool ShouldResetConversationContext(DemoConfiguration previousConfiguration, DemoConfiguration currentConfiguration)
    {
        var previousAccountId = previousConfiguration.AccountId.Trim();
        var previousUserId = previousConfiguration.UserId.Trim();
        var currentAccountId = currentConfiguration.AccountId.Trim();
        var currentUserId = currentConfiguration.UserId.Trim();

        if (!string.IsNullOrWhiteSpace(previousAccountId) &&
            !string.IsNullOrWhiteSpace(currentAccountId) &&
            !string.Equals(previousAccountId, currentAccountId, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(previousUserId) &&
            !string.IsNullOrWhiteSpace(currentUserId) &&
            !string.Equals(previousUserId, currentUserId, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
